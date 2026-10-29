using System.Text.Json;
using Fetchshelf.Clients.Goodreads;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>
/// Imports a `[[goodreads_list]]` entry: fetches the shelf RSS feed, tracks one `ListItem` per book,
/// and feeds MaM matches through the existing selection pipeline, Fetchshelf-spec.md §7.8.
/// </summary>
public sealed class GoodreadsListImporter(
    FetchshelfDbContext dbContext,
    MamClient mamClient,
    MamAccessGate accessGate,
    SelectionRunner selectionRunner,
    GoodreadsClient goodreadsClient,
    FormatPreferenceLists preferenceLists,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private static readonly TimeSpan PolitenessDelay = TimeSpan.FromMilliseconds(400);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((wait, ct) => Task.Delay(wait, ct));

    /// <summary>Returns the number of torrents selected during this run.</summary>
    public async Task<int> RunAsync(
        GoodreadsListEntry entry,
        int globalUnsatBuffer,
        int globalWedgeBuffer,
        IReadOnlyList<long> ignoreTorrents,
        CancellationToken cancellationToken = default)
    {
        using var _ = await accessGate.AcquireAsync(cancellationToken);

        var account = await mamClient.GetAccountStateAsync(cancellationToken);
        var unsatBuffer = entry.UnsatBuffer ?? globalUnsatBuffer;
        var wedgeBuffer = entry.WedgeBuffer ?? globalWedgeBuffer;
        var budget = SelectionBudget.Compute(account.UnsatLimit, account.UnsatCount, unsatBuffer, maxActiveDownloads: null, currentActiveForGrabber: 0);
        if (budget <= 0)
        {
            return 0;
        }

        var listId = GoodreadsListId.Derive(entry.Url);
        var (channelTitle, feedItems) = await goodreadsClient.FetchShelfAsync(entry.Url, cancellationToken);

        if (!entry.DryRun)
        {
            await UpsertListAsync(listId, channelTitle, cancellationToken);
        }

        var grabFilters = entry.Grab.Select(SharedFilterConfigMapper.ToTorrentFilter).ToList();
        var allowAudio = grabFilters.Any(f => AllowsSide(f.Categories, FilterMainType.Audio));
        var allowEbook = grabFilters.Any(f => AllowsSide(f.Categories, FilterMainType.Ebook));

        var selectedCount = 0;
        foreach (var feedItem in feedItems)
        {
            if (selectedCount >= budget)
            {
                break;
            }

            var listItem = await UpsertListItemAsync(listId, entry, feedItem, allowAudio, allowEbook, cancellationToken);
            if (!entry.DryRun)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            selectedCount += await ProcessItemAsync(entry, grabFilters, listItem, unsatBuffer, wedgeBuffer, ignoreTorrents, cancellationToken);

            await _delay(PolitenessDelay, cancellationToken);
        }

        return selectedCount;
    }

    private async Task UpsertListAsync(string listId, string channelTitle, CancellationToken cancellationToken)
    {
        var list = await dbContext.Lists.FindAsync([listId], cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (list is null)
        {
            dbContext.Lists.Add(new ListEntity { Id = listId, Name = channelTitle, ListType = "Goodreads", CreatedAt = now, UpdatedAt = now, BuildDate = now });
        }
        else
        {
            list.Name = channelTitle;
            list.UpdatedAt = now;
            list.BuildDate = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ListItemEntity> UpsertListItemAsync(
        string listId, GoodreadsListEntry entry, GoodreadsFeedItem feedItem, bool allowAudio, bool allowEbook, CancellationToken cancellationToken)
    {
        var existing = await dbContext.ListItems.FindAsync([listId, feedItem.Guid], cancellationToken);
        var preferFormat = entry.PreferFormat?.ToString();

        if (existing is not null)
        {
            existing.BookTitle = feedItem.Title;
            existing.AuthorsJson = JsonSerializer.Serialize(feedItem.Authors);
            existing.SeriesName = feedItem.SeriesName;
            existing.SeriesNumber = feedItem.SeriesNumber;
            existing.CoverUrl = feedItem.CoverUrl;
            existing.BookUrl = feedItem.BookUrl;
            existing.Isbn = feedItem.Isbn;
            existing.PreferFormat = preferFormat;
            existing.AllowAudio = allowAudio;
            existing.AllowEbook = allowEbook;
            return existing;
        }

        var created = new ListItemEntity
        {
            ListId = listId,
            Guid = feedItem.Guid,
            BookTitle = feedItem.Title,
            AuthorsJson = JsonSerializer.Serialize(feedItem.Authors),
            SeriesName = feedItem.SeriesName,
            SeriesNumber = feedItem.SeriesNumber,
            CoverUrl = feedItem.CoverUrl,
            BookUrl = feedItem.BookUrl,
            Isbn = feedItem.Isbn,
            PreferFormat = preferFormat,
            AllowAudio = allowAudio,
            AllowEbook = allowEbook,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.ListItems.Add(created);
        return created;
    }

    private async Task<int> ProcessItemAsync(
        GoodreadsListEntry entry,
        IReadOnlyList<TorrentFilter> grabFilters,
        ListItemEntity listItem,
        int unsatBuffer,
        int wedgeBuffer,
        IReadOnlyList<long> ignoreTorrents,
        CancellationToken cancellationToken)
    {
        if (listItem.MarkedDoneAt is not null)
        {
            return 0;
        }

        var needAudio = listItem.AllowAudio && !IsResolved(listItem.AudioStatus);
        var needEbook = listItem.AllowEbook && !IsResolved(listItem.EbookStatus);
        if (!needAudio && !needEbook)
        {
            return 0;
        }

        var authors = JsonSerializer.Deserialize<List<string>>(listItem.AuthorsJson) ?? [];
        var series = listItem.SeriesName is null || listItem.SeriesNumber is null
            ? ((string, double)?)null
            : (listItem.SeriesName, listItem.SeriesNumber.Value);

        if (needAudio)
        {
            needAudio = !await TryMatchLibraryAsync(listItem, authors, FilterMainType.Audio, cancellationToken);
        }

        if (needEbook)
        {
            needEbook = !await TryMatchLibraryAsync(listItem, authors, FilterMainType.Ebook, cancellationToken);
        }

        if (!entry.DryRun)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (!needAudio && !needEbook)
        {
            return 0;
        }

        ListCandidate<MamTorrentRecord>? audioWinner = null;
        GrabEntry? audioWinnerGrab = null;
        ListCandidate<MamTorrentRecord>? ebookWinner = null;
        GrabEntry? ebookWinnerGrab = null;

        var queryText = ListMamQueryBuilder.Build(listItem.BookTitle, authors);
        for (var i = 0; i < entry.Grab.Count; i++)
        {
            if (!needAudio && !needEbook)
            {
                break;
            }

            var narrowedFilter = ListGrabSearchTranslator.NarrowToNeededSides(grabFilters[i], needAudio, needEbook);
            var query = ListGrabSearchTranslator.ToSearchQuery(narrowedFilter, queryText, pageOffset: 0);
            var result = await mamClient.SearchAsync(query, cancellationToken);

            var candidates = result.Data
                .Select(record =>
                {
                    try
                    {
                        return (Record: record, Metadata: (TorrentMetadata?)MamTorrentMapper.Map(record));
                    }
                    catch (UnrecognizedReferenceDataException)
                    {
                        return (Record: record, Metadata: null);
                    }
                })
                .Where(c => c.Metadata is not null)
                .Where(c => TorrentFilterMatcher.Matches(narrowedFilter, ToFilterCandidate(c.Record, c.Metadata!)))
                .Select(c => new ListCandidate<MamTorrentRecord> { Metadata = c.Metadata!, Payload = c.Record })
                .ToList();

            if (needAudio && audioWinner is null)
            {
                var ranked = ListCandidateRanker.RankAndFilter(
                    candidates.Where(c => FilterMainTypeMapper.FromMediaType(c.Metadata.MediaType) == FilterMainType.Audio).ToList(),
                    listItem.BookTitle, authors, series, preferenceLists.AudioTypes);
                if (ranked.Count > 0)
                {
                    audioWinner = ranked[0];
                    audioWinnerGrab = entry.Grab[i];
                }
            }

            if (needEbook && ebookWinner is null)
            {
                var ranked = ListCandidateRanker.RankAndFilter(
                    candidates.Where(c => FilterMainTypeMapper.FromMediaType(c.Metadata.MediaType) == FilterMainType.Ebook).ToList(),
                    listItem.BookTitle, authors, series, preferenceLists.EbookTypes);
                if (ranked.Count > 0)
                {
                    ebookWinner = ranked[0];
                    ebookWinnerGrab = entry.Grab[i];
                }
            }
        }

        if (audioWinner is not null && ebookWinner is not null && entry.PreferFormat is { } preferFormat)
        {
            if (preferFormat == FilterMainType.Audio)
            {
                MarkNotWanted(listItem, FilterMainType.Ebook, ebookWinner.Payload.MamId);
                ebookWinner = null;
            }
            else if (preferFormat == FilterMainType.Ebook)
            {
                MarkNotWanted(listItem, FilterMainType.Audio, audioWinner.Payload.MamId);
                audioWinner = null;
            }
        }

        var selectedCount = 0;
        if (audioWinner is not null)
        {
            selectedCount += await SelectAndRecordAsync(
                entry, listItem, FilterMainType.Audio, audioWinner, audioWinnerGrab!.Cost, unsatBuffer, wedgeBuffer, ignoreTorrents, cancellationToken);
        }

        if (ebookWinner is not null)
        {
            selectedCount += await SelectAndRecordAsync(
                entry, listItem, FilterMainType.Ebook, ebookWinner, ebookWinnerGrab!.Cost, unsatBuffer, wedgeBuffer, ignoreTorrents, cancellationToken);
        }

        if (!entry.DryRun)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return selectedCount;
    }

    private async Task<int> SelectAndRecordAsync(
        GoodreadsListEntry entry,
        ListItemEntity listItem,
        FilterMainType side,
        ListCandidate<MamTorrentRecord> winner,
        ConfiguredCost cost,
        int unsatBuffer,
        int wedgeBuffer,
        IReadOnlyList<long> ignoreTorrents,
        CancellationToken cancellationToken)
    {
        if (entry.DryRun)
        {
            return 0;
        }

        var outcome = await selectionRunner.ProcessCandidateAsync(
            winner.Payload, winner.Metadata, ignoreTorrents, cost, autogrebCategory: null, grabberName: entry.Name,
            unsatBuffer, wedgeBuffer, cancellationToken: cancellationToken);

        if (outcome == SelectionOutcome.Selected)
        {
            SetSide(listItem, side, "Selected", winner.Payload.MamId);
            return 1;
        }

        if (outcome == SelectionOutcome.CostNotFree)
        {
            SetSide(listItem, side, "Wanted", winner.Payload.MamId);
        }

        return 0;
    }

    private async Task<bool> TryMatchLibraryAsync(ListItemEntity listItem, IReadOnlyList<string> authors, FilterMainType side, CancellationToken cancellationToken)
    {
        var normalizedTitle = TitleNormalizer.Normalize(listItem.BookTitle);
        var candidates = await dbContext.Torrents
            .Where(t => t.NormalizedTitleKey == normalizedTitle)
            .Select(t => new { t.MamId, t.MetadataJson })
            .ToListAsync(cancellationToken);

        var wrapped = candidates.Select(c => new ListCandidate<long>
        {
            Metadata = TorrentMetadataSerializer.Deserialize(c.MetadataJson),
            Payload = c.MamId,
        });

        var match = ListLibraryMatcher.FindMatch(listItem.BookTitle, authors, side, wrapped);
        if (match is null)
        {
            return false;
        }

        SetSide(listItem, side, "Existing", match.Payload);
        return true;
    }

    private static void SetSide(ListItemEntity listItem, FilterMainType side, string status, long mamId)
    {
        var now = DateTimeOffset.UtcNow;
        if (side == FilterMainType.Audio)
        {
            listItem.AudioStatus = status;
            listItem.AudioMamId = mamId;
            listItem.AudioAt = now;
        }
        else
        {
            listItem.EbookStatus = status;
            listItem.EbookMamId = mamId;
            listItem.EbookAt = now;
        }
    }

    private static void MarkNotWanted(ListItemEntity listItem, FilterMainType side, long mamId) =>
        SetSide(listItem, side, "NotWanted", mamId);

    private static bool IsResolved(string? status) => status is "Selected" or "Existing" or "NotWanted";

    private static bool AllowsSide(CategoryFilter categories, FilterMainType side)
    {
        var group = side switch
        {
            FilterMainType.Audio => categories.Audio,
            FilterMainType.Ebook => categories.Ebook,
            FilterMainType.Musicology => categories.Musicology,
            FilterMainType.Radio => categories.Radio,
            _ => throw new ArgumentOutOfRangeException(nameof(side)),
        };
        return group.Allowed is null || group.Allowed.Count > 0;
    }

    private static TorrentFilterCandidate ToFilterCandidate(MamTorrentRecord record, TorrentMetadata metadata) => new()
    {
        Metadata = metadata,
        UploaderName = record.UploaderName,
        Seeders = record.Seeders,
        Leechers = record.Leechers,
        TimesCompleted = record.TimesCompleted,
    };
}
