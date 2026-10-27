using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>
/// Runs selection against one candidate, Fetchshelf-spec.md §7.2. One call processes one search result;
/// the caller (<see cref="AutograbberRunner"/>) loops over results and stops once the run's budget
/// of <see cref="SelectionOutcome.Selected"/> outcomes is reached.
/// </summary>
public sealed class SelectionRunner(
    FetchshelfDbContext dbContext,
    EventStore eventStore,
    IReadOnlyList<TagRule> tagRules,
    FormatPreferenceLists preferenceLists)
{
    public async Task<SelectionOutcome> ProcessCandidateAsync(
        MamTorrentRecord record,
        TorrentMetadata metadata,
        IReadOnlyList<long> ignoreTorrents,
        ConfiguredCost configuredCost,
        string? autogrebCategory,
        string? grabberName,
        int unsatBuffer,
        int wedgeBuffer,
        CancellationToken cancellationToken = default)
    {
        if (ignoreTorrents.Contains(record.MamId))
        {
            return SelectionOutcome.Ignored;
        }

        var existingSelected = await dbContext.SelectedTorrents
            .FirstOrDefaultAsync(s => s.MamId == record.MamId && s.RemovedAt == null, cancellationToken);
        if (existingSelected is not null)
        {
            RefreshSelectedMetadata(existingSelected, metadata, unsatBuffer, wedgeBuffer);
            await dbContext.SaveChangesAsync(cancellationToken);
            return SelectionOutcome.RefreshedSelected;
        }

        var existingTorrent = await dbContext.Torrents.FirstOrDefaultAsync(t => t.MamId == record.MamId, cancellationToken);
        if (existingTorrent is not null)
        {
            await RefreshLibraryMetadataAsync(existingTorrent, metadata, cancellationToken);
            return SelectionOutcome.RefreshedLibrary;
        }

        if (configuredCost == ConfiguredCost.MetadataOnlyAdd)
        {
            CreateMetadataOnlyEntry(record.MamId, metadata);
            await dbContext.SaveChangesAsync(cancellationToken);
            return SelectionOutcome.MetadataOnlyCreated;
        }

        if (configuredCost is ConfiguredCost.MetadataOnly)
        {
            return SelectionOutcome.MetadataOnlyStop;
        }

        var preferenceList = preferenceLists.For(metadata.MediaType);
        var candidateRank = FormatRanker.Rank(preferenceList, metadata.FileTypes);
        if (candidateRank == FormatRanker.NotPresent)
        {
            return SelectionOutcome.NoPreferredFormat;
        }

        var normalizedTitle = TitleNormalizer.Normalize(metadata.Title);

        var duplicateAgainstSelected = await CheckDuplicateAgainstSelectedAsync(
            record.MamId, metadata, normalizedTitle, candidateRank, cancellationToken);
        if (duplicateAgainstSelected)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return SelectionOutcome.DuplicateOfSelected;
        }

        var duplicateAgainstLibrary = await CheckDuplicateAgainstLibraryAsync(
            record.MamId, metadata, normalizedTitle, candidateRank, cancellationToken);
        if (duplicateAgainstLibrary)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return SelectionOutcome.DuplicateOfLibrary;
        }

        var candidate = new TorrentFilterCandidate
        {
            Metadata = metadata,
            UploaderName = record.UploaderName,
            Seeders = record.Seeders,
            Leechers = record.Leechers,
            TimesCompleted = record.TimesCompleted,
        };
        var (category, tags) = TagRuleResolver.Resolve(candidate, autogrebCategory, tagRules);

        var cost = CostPolicy.Derive(record.IsVip, record.IsPersonalFreeleech, record.IsFree, configuredCost);
        if (cost is null)
        {
            return SelectionOutcome.CostNotFree;
        }

        CreateSelected(record, metadata, cost.Value, category, tags, grabberName, unsatBuffer, wedgeBuffer);
        await dbContext.SaveChangesAsync(cancellationToken);
        return SelectionOutcome.Selected;
    }

    private static void RefreshSelectedMetadata(SelectedTorrentEntity existing, TorrentMetadata metadata, int unsatBuffer, int wedgeBuffer)
    {
        existing.MetadataJson = TorrentMetadataSerializer.Serialize(metadata);
        existing.NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title);
        existing.UnsatBuffer = unsatBuffer;
        existing.WedgeBuffer = wedgeBuffer;
    }

    private async Task RefreshLibraryMetadataAsync(TorrentEntity existing, TorrentMetadata metadata, CancellationToken cancellationToken)
    {
        var current = TorrentMetadataSerializer.Deserialize(existing.MetadataJson);
        var changes = DiffMetadata(current, metadata);
        if (changes.Count == 0)
        {
            return;
        }

        existing.MetadataJson = TorrentMetadataSerializer.Serialize(metadata);
        existing.Title = metadata.Title;
        existing.NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title);

        await eventStore.AppendAsync(new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            TorrentId = existing.Id,
            MamId = existing.MamId,
            Kind = EventKind.Updated,
            FieldChangesJson = System.Text.Json.JsonSerializer.Serialize(changes),
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void CreateMetadataOnlyEntry(long mamId, TorrentMetadata metadata)
    {
        dbContext.Torrents.Add(new TorrentEntity
        {
            Id = MetadataOnlyTorrentId.For(mamId),
            MamId = mamId,
            NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
            Title = metadata.Title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private async Task<bool> CheckDuplicateAgainstSelectedAsync(
        long candidateMamId, TorrentMetadata candidateMeta, string normalizedTitle, int candidateRank, CancellationToken cancellationToken)
    {
        var sameTitle = await dbContext.SelectedTorrents
            .Where(s => s.RemovedAt == null && s.NormalizedTitleKey == normalizedTitle)
            .ToListAsync(cancellationToken);

        foreach (var other in sameTitle)
        {
            var otherMeta = TorrentMetadataSerializer.Deserialize(other.MetadataJson);
            if (!SameBookComparer.AreSameBook(candidateMeta, otherMeta))
            {
                continue;
            }

            var otherRank = FormatRanker.Rank(preferenceLists.For(otherMeta.MediaType), otherMeta.FileTypes);
            if (otherRank <= candidateRank)
            {
                RecordDuplicate(candidateMamId, candidateMeta, duplicateOfTorrentId: other.MamId.ToString());
                return true;
            }

            other.RemovedAt = DateTimeOffset.UtcNow;
            RecordDuplicate(other.MamId, otherMeta, duplicateOfTorrentId: candidateMamId.ToString());
        }

        return false;
    }

    private async Task<bool> CheckDuplicateAgainstLibraryAsync(
        long candidateMamId, TorrentMetadata candidateMeta, string normalizedTitle, int candidateRank, CancellationToken cancellationToken)
    {
        var sameTitle = await dbContext.Torrents
            .Where(t => t.NormalizedTitleKey == normalizedTitle)
            .ToListAsync(cancellationToken);

        foreach (var other in sameTitle)
        {
            var otherMeta = TorrentMetadataSerializer.Deserialize(other.MetadataJson);
            if (!SameBookComparer.AreSameBook(candidateMeta, otherMeta))
            {
                continue;
            }

            var otherRank = FormatRanker.Rank(preferenceLists.For(otherMeta.MediaType), otherMeta.FileTypes);
            if (otherRank <= candidateRank)
            {
                RecordDuplicate(candidateMamId, candidateMeta, duplicateOfTorrentId: other.Id);
                return true;
            }

            // Candidate is better; proceed without touching the library entry - the cleaner
            // (library-organizer) retires it later (Fetchshelf-spec.md §7.2 step 7, §8.6).
        }

        return false;
    }

    private void RecordDuplicate(long mamId, TorrentMetadata metadata, string duplicateOfTorrentId) =>
        dbContext.DuplicateTorrents.Add(new DuplicateTorrentEntity
        {
            MamId = mamId,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            DuplicateOfTorrentId = duplicateOfTorrentId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private void CreateSelected(
        MamTorrentRecord record,
        TorrentMetadata metadata,
        SelectedTorrentCost cost,
        string? category,
        IReadOnlySet<string> tags,
        string? grabberName,
        int unsatBuffer,
        int wedgeBuffer)
    {
        dbContext.SelectedTorrents.Add(new SelectedTorrentEntity
        {
            MamId = record.MamId,
            DownloadLink = record.DownloadHash ?? string.Empty,
            CostDecision = cost.ToString(),
            Category = category,
            TagsJson = System.Text.Json.JsonSerializer.Serialize(tags),
            UnsatBuffer = unsatBuffer,
            WedgeBuffer = wedgeBuffer,
            GrabberName = grabberName,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static List<FieldChange> DiffMetadata(TorrentMetadata from, TorrentMetadata to)
    {
        var changes = new List<FieldChange>();
        AddIfChanged(changes, "Title", from.Title, to.Title);
        AddIfChanged(changes, "Authors", string.Join(", ", from.Authors), string.Join(", ", to.Authors));
        AddIfChanged(changes, "Narrators", string.Join(", ", from.Narrators), string.Join(", ", to.Narrators));
        AddIfChanged(changes, "Language", from.Language.ToString(), to.Language.ToString());
        AddIfChanged(changes, "LegacyCategory", from.LegacyCategory.ToString(), to.LegacyCategory.ToString());
        return changes;
    }

    private static void AddIfChanged(List<FieldChange> changes, string field, string from, string to)
    {
        if (!string.Equals(from, to, StringComparison.Ordinal))
        {
            changes.Add(new FieldChange(field, from, to));
        }
    }
}
