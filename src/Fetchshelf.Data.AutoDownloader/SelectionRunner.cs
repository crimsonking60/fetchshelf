using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>
/// Runs selection against one candidate. One call processes one search result;
/// the caller (<see cref="AutograbberRunner"/>) loops over results and stops once the run's budget
/// of <see cref="SelectionOutcome.Selected"/> outcomes is reached.
/// </summary>
/// <remarks>
/// With <c>dryRun</c>, every check runs and the same outcome is returned, but nothing is written. Torrents a dry run
/// would have selected are remembered for the rest of this instance's life (one job run), so a later copy of the same
/// book in that run is reported as a duplicate, as it would be in a real run.
/// </remarks>
public sealed class SelectionRunner(
    FetchshelfDbContext dbContext,
    EventStore eventStore,
    IReadOnlyList<TagRule> tagRules,
    FormatPreferenceLists preferenceLists)
{
    private readonly List<DryRunSelection> _dryRunSelections = [];

    public async Task<SelectionOutcome> ProcessCandidateAsync(
        MamTorrentRecord record,
        TorrentMetadata metadata,
        IReadOnlyList<long> ignoreTorrents,
        ConfiguredCost configuredCost,
        string? autograbCategory,
        string? grabberName,
        int unsatBuffer,
        int wedgeBuffer,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        if (ignoreTorrents.Contains(record.MamId))
        {
            return SelectionOutcome.Ignored;
        }

        var existingSelected = await dbContext.SelectedTorrents
            .FirstOrDefaultAsync(s => s.MamId == record.MamId && s.RemovedAt == null, cancellationToken);
        if (dryRun && (existingSelected is not null || _dryRunSelections.Any(d => d.MamId == record.MamId)))
        {
            return SelectionOutcome.RefreshedSelected;
        }

        if (existingSelected is not null)
        {
            RefreshSelectedMetadata(existingSelected, metadata, unsatBuffer, wedgeBuffer);
            await dbContext.SaveChangesAsync(cancellationToken);
            return SelectionOutcome.RefreshedSelected;
        }

        var existingTorrent = await dbContext.Torrents.FirstOrDefaultAsync(t => t.MamId == record.MamId, cancellationToken);
        if (existingTorrent is not null)
        {
            if (!dryRun)
            {
                await RefreshLibraryMetadataAsync(existingTorrent, metadata, cancellationToken);
            }

            return SelectionOutcome.RefreshedLibrary;
        }

        if (configuredCost == ConfiguredCost.MetadataOnlyAdd)
        {
            if (!dryRun)
            {
                CreateMetadataOnlyEntry(record.MamId, metadata);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

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
            record.MamId, metadata, normalizedTitle, candidateRank, dryRun, cancellationToken);
        if (duplicateAgainstSelected)
        {
            if (!dryRun)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return SelectionOutcome.DuplicateOfSelected;
        }

        var duplicateAgainstLibrary = await CheckDuplicateAgainstLibraryAsync(
            record.MamId, metadata, normalizedTitle, candidateRank, dryRun, cancellationToken);
        if (duplicateAgainstLibrary)
        {
            if (!dryRun)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

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
        var (category, tags) = TagRuleResolver.Resolve(candidate, autograbCategory, tagRules);

        var cost = CostPolicy.Derive(record.IsVip, record.IsPersonalFreeleech, record.IsFree, configuredCost);
        if (cost is null)
        {
            return SelectionOutcome.CostNotFree;
        }

        if (dryRun)
        {
            _dryRunSelections.Add(new DryRunSelection(record.MamId, metadata, normalizedTitle));
            return SelectionOutcome.Selected;
        }

        CreateSelected(record, metadata, cost.Value, category, tags, grabberName, unsatBuffer, wedgeBuffer);
        await dbContext.SaveChangesAsync(cancellationToken);
        return SelectionOutcome.Selected;
    }

    private sealed record DryRunSelection(long MamId, TorrentMetadata Metadata, string NormalizedTitle);

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
        var changes = MetadataDiff.Between(current, metadata);
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
        long candidateMamId, TorrentMetadata candidateMeta, string normalizedTitle, int candidateRank, bool dryRun, CancellationToken cancellationToken)
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
                if (!dryRun)
                {
                    RecordDuplicate(candidateMamId, candidateMeta, duplicateOfTorrentId: other.MamId.ToString());
                }

                return true;
            }

            if (!dryRun)
            {
                other.RemovedAt = DateTimeOffset.UtcNow;
                RecordDuplicate(other.MamId, otherMeta, duplicateOfTorrentId: candidateMamId.ToString());
            }
        }

        if (dryRun)
        {
            foreach (var other in _dryRunSelections.Where(d => d.NormalizedTitle == normalizedTitle).ToList())
            {
                if (!SameBookComparer.AreSameBook(candidateMeta, other.Metadata))
                {
                    continue;
                }

                if (FormatRanker.Rank(preferenceLists.For(other.Metadata.MediaType), other.Metadata.FileTypes) <= candidateRank)
                {
                    return true;
                }

                _dryRunSelections.Remove(other);
            }
        }

        return false;
    }

    private async Task<bool> CheckDuplicateAgainstLibraryAsync(
        long candidateMamId, TorrentMetadata candidateMeta, string normalizedTitle, int candidateRank, bool dryRun, CancellationToken cancellationToken)
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
                if (!dryRun)
                {
                    RecordDuplicate(candidateMamId, candidateMeta, duplicateOfTorrentId: other.Id);
                }

                return true;
            }

            // Candidate is better; proceed without touching the library entry - the cleaner
            // (library-organizer) retires it later.
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
}
