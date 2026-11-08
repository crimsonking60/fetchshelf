using System.Text.Json;
using Fetchshelf.Clients.Hardcover;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>What one run of a `[[hardcover_list]]` did.</summary>
public sealed record HardcoverListRunSummary(int Selected, int Failed);

/// <summary>
/// Runs one `[[hardcover_list]]`: reads its books from Hardcover, keeps one <see cref="ListItemEntity"/> per book, and
/// searches MAM for the formats still missing, passing the best match through the usual selection rules.
/// </summary>
/// <remarks>
/// Hardcover is read before the MAM gate is taken, so a slow or failing Hardcover never holds up other MAM jobs. A
/// book that is still missing a format is searched for again at most once per <see cref="SearchAgainAfter"/>. A dry
/// run writes nothing: list rows are only built in memory.
/// </remarks>
public sealed class HardcoverListImporter(
    FetchshelfDbContext dbContext,
    MamClient mamClient,
    MamAccessGate accessGate,
    SelectionRunner selectionRunner,
    IHardcoverBookSource bookSource,
    FormatPreferenceLists preferenceLists,
    TimeProvider? timeProvider = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    ILogger<HardcoverListImporter>? logger = null)
{
    public static readonly TimeSpan SearchAgainAfter = TimeSpan.FromDays(1);

    private static readonly TimeSpan PolitenessDelay = TimeSpan.FromMilliseconds(400);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((wait, ct) => Task.Delay(wait, ct));
    private readonly ILogger<HardcoverListImporter> _logger = logger ?? NullLogger<HardcoverListImporter>.Instance;

    /// <summary>The list row's id: one per reading status or Hardcover list.</summary>
    public static string ListIdFor(HardcoverListEntry entry) =>
        entry.List is { } slug ? $"hardcover:list:{slug}" : $"hardcover:status:{StatusName(entry.Status!.Value)}";

    /// <summary>The name shown on the Lists page and in the log: the entry's own name, else the status or list.</summary>
    public static string DisplayNameFor(HardcoverListEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Name) ? entry.Name
        : entry.List is { } slug ? slug
        : HardcoverClient.Describe(HardcoverBookSource.ToReadingStatus(entry.Status!.Value));

    public async Task<HardcoverListRunSummary> RunAsync(
        HardcoverListEntry entry, int unsatBuffer, IReadOnlyList<long> ignoreTorrents, CancellationToken cancellationToken = default)
    {
        var name = DisplayNameFor(entry);
        var books = await bookSource.GetBooksAsync(entry, cancellationToken);
        if (entry.DryRun)
        {
            _logger.LogInformation("Hardcover list {Name}: dry run, nothing will be selected or written", name);
        }

        var grabFilters = entry.Grab.Select(SharedFilterConfigMapper.ToTorrentFilter).ToList();
        var allowAudio = grabFilters.Any(f => AllowsSide(f.Categories, FilterMainType.Audio));
        var allowEbook = grabFilters.Any(f => AllowsSide(f.Categories, FilterMainType.Ebook));
        var items = await SyncItemsAsync(entry, name, books, allowAudio, allowEbook, cancellationToken);

        using var _ = await accessGate.AcquireAsync(cancellationToken);

        var account = await mamClient.GetAccountStateAsync(cancellationToken);
        var budget = SelectionBudget.Compute(account.UnsatLimit, account.UnsatCount, unsatBuffer, maxActiveDownloads: null, currentActiveForGrabber: 0);
        if (budget <= 0)
        {
            var explanation = SelectionBudget.Explain(account.UnsatLimit, account.UnsatCount, unsatBuffer, maxActiveDownloads: null, currentActiveForGrabber: 0);
            _logger.LogInformation(
                "Hardcover list {Name}: {Books}, nothing searched, this run's limit is 0 selections ({BudgetExplanation})",
                name, Books(books.Count), explanation);
            return new HardcoverListRunSummary(0, 0);
        }

        var selected = 0;
        var failed = 0;
        var searched = 0;
        foreach (var item in items.Where(i => i.RemovedAt is null && i.MarkedDoneAt is null).OrderBy(i => i.Position))
        {
            if (selected >= budget)
            {
                _logger.LogInformation("Hardcover list {Name}: stopped at this run's limit of {Budget} selections", name, budget);
                break;
            }

            try
            {
                var outcome = await ProcessItemAsync(entry, name, grabFilters, item, ignoreTorrents, cancellationToken);
                selected += outcome.Selected;
                searched += outcome.Searched ? 1 : 0;
                if (!entry.DryRun)
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failed++;
                _logger.LogWarning(ex, "Hardcover list {Name}: \"{Title}\" failed: {Reason}", name, item.BookTitle, ex.Message);
                // What the failed book changed must not be saved along with the next one.
                dbContext.DiscardPendingChanges();
            }
        }

        _logger.LogInformation(
            "Hardcover list {Name}: {Books}, {Searched} searched on MAM, {Selected} {Verb}{Failures}",
            name, Books(books.Count), searched, selected, entry.DryRun ? "would have been selected (dry run)" : "selected",
            failed == 0 ? string.Empty : $", {failed} failed");
        return new HardcoverListRunSummary(entry.DryRun ? 0 : selected, failed);
    }

    /// <summary>
    /// Brings the stored rows in line with the books Hardcover returned: new books are added, known ones refreshed,
    /// and books no longer on the shelf or list marked removed (and active again if they come back).
    /// </summary>
    private async Task<List<ListItemEntity>> SyncItemsAsync(
        HardcoverListEntry entry, string name, IReadOnlyList<HardcoverBook> books, bool allowAudio, bool allowEbook, CancellationToken cancellationToken)
    {
        var listId = ListIdFor(entry);
        var now = _time.GetUtcNow();
        var tracked = !entry.DryRun;
        var query = tracked ? dbContext.ListItems.AsQueryable() : dbContext.ListItems.AsNoTracking();
        var existing = await query.Where(i => i.ListId == listId).ToDictionaryAsync(i => i.ExternalId, cancellationToken);

        if (tracked)
        {
            var list = await dbContext.Lists.FindAsync([listId], cancellationToken);
            if (list is null)
            {
                dbContext.Lists.Add(new ListEntity { Id = listId, Name = name, ListType = "Hardcover", CreatedAt = now, UpdatedAt = now });
            }
            else
            {
                list.Name = name;
                list.UpdatedAt = now;
            }
        }

        var preferFormat = entry.PreferFormat?.ToString();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var position = 0; position < books.Count; position++)
        {
            var book = books[position];
            var externalId = book.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!seen.Add(externalId))
            {
                continue;
            }

            if (!existing.TryGetValue(externalId, out var item))
            {
                item = new ListItemEntity
                {
                    ListId = listId,
                    ExternalId = externalId,
                    BookTitle = book.Title,
                    AuthorsJson = "[]",
                    CreatedAt = now,
                };
                existing[externalId] = item;
                if (tracked)
                {
                    dbContext.ListItems.Add(item);
                }
            }

            var series = book.Series.FirstOrDefault();
            item.Position = position;
            item.BookTitle = book.Title;
            item.AuthorsJson = JsonSerializer.Serialize(book.Authors);
            item.SeriesName = series?.Name;
            item.SeriesNumber = series?.Position;
            item.CoverUrl = book.CoverUrl;
            item.BookUrl = book.Url;
            item.PreferFormat = preferFormat;
            item.AllowAudio = allowAudio;
            item.AllowEbook = allowEbook;
            item.RemovedAt = null;
        }

        foreach (var gone in existing.Values.Where(i => !seen.Contains(i.ExternalId) && i.RemovedAt is null))
        {
            gone.RemovedAt = now;
        }

        if (tracked)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return existing.Values.ToList();
    }

    private async Task<(int Selected, bool Searched)> ProcessItemAsync(
        HardcoverListEntry entry, string name, IReadOnlyList<TorrentFilter> grabFilters, ListItemEntity item,
        IReadOnlyList<long> ignoreTorrents, CancellationToken cancellationToken)
    {
        var needAudio = item.AllowAudio && !ListItemSideStatus.IsResolved(item.AudioStatus);
        var needEbook = item.AllowEbook && !ListItemSideStatus.IsResolved(item.EbookStatus);
        if (!needAudio && !needEbook)
        {
            return (0, false);
        }

        var authors = JsonSerializer.Deserialize<List<string>>(item.AuthorsJson) ?? [];
        if (needAudio && await TryMatchLibraryAsync(item, authors, FilterMainType.Audio, cancellationToken))
        {
            needAudio = false;
        }

        if (needEbook && await TryMatchLibraryAsync(item, authors, FilterMainType.Ebook, cancellationToken))
        {
            needEbook = false;
        }

        var now = _time.GetUtcNow();
        if ((!needAudio && !needEbook) || item.LastSearchedAt > now - SearchAgainAfter)
        {
            return (0, false);
        }

        var series = item.SeriesName is null || item.SeriesNumber is null
            ? ((string, double)?)null
            : (item.SeriesName, item.SeriesNumber.Value);
        var queryText = ListMamQueryBuilder.Build(item.BookTitle, authors);

        ListCandidate<MamTorrentRecord>? audioWinner = null;
        GrabEntry? audioGrab = null;
        ListCandidate<MamTorrentRecord>? ebookWinner = null;
        GrabEntry? ebookGrab = null;

        // A match no block's cost accepts so far, such as a ratio torrent seen by a free block: reported as wanted if no
        // later block takes the book either.
        ListCandidate<MamTorrentRecord>? audioTooExpensive = null;
        ListCandidate<MamTorrentRecord>? ebookTooExpensive = null;
        for (var i = 0; i < entry.Grab.Count && ((needAudio && audioWinner is null) || (needEbook && ebookWinner is null)); i++)
        {
            var filter = ListGrabSearchTranslator.NarrowToNeededSides(grabFilters[i], needAudio && audioWinner is null, needEbook && ebookWinner is null);
            var result = await mamClient.SearchAsync(ListGrabSearchTranslator.ToSearchQuery(filter, queryText, pageOffset: 0), cancellationToken);
            await _delay(PolitenessDelay, cancellationToken);

            var candidates = result.Data
                .Select(record => (Record: record, Metadata: TryMap(record)))
                .Where(c => c.Metadata is not null && TorrentFilterMatcher.Matches(filter, ToFilterCandidate(c.Record, c.Metadata)))
                .Select(c => new ListCandidate<MamTorrentRecord> { Metadata = c.Metadata!, Payload = c.Record })
                .ToList();

            // A block only takes a torrent its own cost accepts. The search isn't limited by cost, so a free block also
            // sees uploads that aren't free; those are left for the later blocks, which may pay for them.
            var grab = entry.Grab[i];
            var affordable = candidates.Where(c => Accepts(grab.Cost, c.Payload)).ToList();
            if (needAudio && audioWinner is null)
            {
                if (Best(affordable, FilterMainType.Audio, item, authors, series, preferenceLists.AudioTypes) is { } audio)
                {
                    (audioWinner, audioGrab) = (audio, grab);
                }
                else
                {
                    audioTooExpensive ??= Best(candidates, FilterMainType.Audio, item, authors, series, preferenceLists.AudioTypes);
                }
            }

            if (needEbook && ebookWinner is null)
            {
                if (Best(affordable, FilterMainType.Ebook, item, authors, series, preferenceLists.EbookTypes) is { } ebook)
                {
                    (ebookWinner, ebookGrab) = (ebook, grab);
                }
                else
                {
                    ebookTooExpensive ??= Best(candidates, FilterMainType.Ebook, item, authors, series, preferenceLists.EbookTypes);
                }
            }
        }

        if (audioWinner is not null && ebookWinner is not null && entry.PreferFormat is { } preferred)
        {
            if (preferred == FilterMainType.Audio)
            {
                SetSide(item, FilterMainType.Ebook, ListItemSideStatus.NotWanted, ebookWinner.Payload.MamId);
                (ebookWinner, needEbook) = (null, false);
            }
            else
            {
                SetSide(item, FilterMainType.Audio, ListItemSideStatus.NotWanted, audioWinner.Payload.MamId);
                (audioWinner, needAudio) = (null, false);
            }
        }

        var selected = 0;
        if (needAudio)
        {
            selected += await RecordSideAsync(entry, name, item, FilterMainType.Audio, audioWinner, audioGrab, audioTooExpensive, ignoreTorrents, cancellationToken);
        }

        if (needEbook)
        {
            selected += await RecordSideAsync(entry, name, item, FilterMainType.Ebook, ebookWinner, ebookGrab, ebookTooExpensive, ignoreTorrents, cancellationToken);
        }

        item.LastSearchedAt = now;
        return (selected, true);
    }

    private async Task<int> RecordSideAsync(
        HardcoverListEntry entry, string name, ListItemEntity item, FilterMainType side,
        ListCandidate<MamTorrentRecord>? winner, GrabEntry? grab, ListCandidate<MamTorrentRecord>? tooExpensive,
        IReadOnlyList<long> ignoreTorrents, CancellationToken cancellationToken)
    {
        var format = side == FilterMainType.Audio ? "audiobook" : "ebook";
        if (winner is null && tooExpensive is not null)
        {
            SetSide(item, side, ListItemSideStatus.Wanted, tooExpensive.Payload.MamId);
            _logger.Log(
                Detail(entry), "Hardcover list {Name}: \"{Title}\": {Format} MAM torrent {MamId} matches, but no grab block's cost accepts it",
                name, item.BookTitle, format, tooExpensive.Payload.MamId);
            return 0;
        }

        if (winner is null)
        {
            SetSide(item, side, ListItemSideStatus.NotFound, mamId: null);
            _logger.Log(Detail(entry), "Hardcover list {Name}: \"{Title}\": no {Format} on MAM matches", name, item.BookTitle, format);
            return 0;
        }

        var outcome = await selectionRunner.ProcessCandidateAsync(
            winner.Payload, winner.Metadata, ignoreTorrents, grab!.Cost, autograbCategory: entry.Category, grabberName: entry.Name,
            dryRun: entry.DryRun, cancellationToken: cancellationToken);
        var status = outcome switch
        {
            SelectionOutcome.Selected or SelectionOutcome.DuplicateOfSelected or SelectionOutcome.RefreshedSelected => ListItemSideStatus.Selected,
            SelectionOutcome.DuplicateOfLibrary or SelectionOutcome.RefreshedLibrary => ListItemSideStatus.Existing,
            SelectionOutcome.CostNotFree => ListItemSideStatus.Wanted,
            _ => ListItemSideStatus.NotFound,
        };
        SetSide(item, side, status, winner.Payload.MamId);
        _logger.Log(
            Detail(entry), "Hardcover list {Name}: \"{Title}\": {Format} MAM torrent {MamId}: {Outcome}",
            name, item.BookTitle, format, winner.Payload.MamId, outcome.Describe(entry.DryRun));
        return outcome == SelectionOutcome.Selected ? 1 : 0;
    }

    private static string Books(int count) => count == 1 ? "1 book" : $"{count} books";

    /// <summary>Whether a grab block with <paramref name="cost"/> may take <paramref name="record"/>: the rule selection applies.</summary>
    private static bool Accepts(ConfiguredCost cost, MamTorrentRecord record) =>
        CostPolicy.Derive(record.IsVip, record.IsPersonalFreeleech, record.IsFree, cost) is not null;

    private static LogLevel Detail(HardcoverListEntry entry) => entry.DryRun ? LogLevel.Information : LogLevel.Debug;

    private static ListCandidate<MamTorrentRecord>? Best(
        IReadOnlyList<ListCandidate<MamTorrentRecord>> candidates, FilterMainType side, ListItemEntity item,
        IReadOnlyList<string> authors, (string Name, double Number)? series, IReadOnlyList<string> preferredTypes)
    {
        var ofSide = candidates.Where(c => FilterMainTypeMapper.FromMediaType(c.Metadata.MediaType) == side).ToList();
        // The ranker keeps the closest results; the library matcher's thresholds then make sure it is the same book.
        var ranked = ListCandidateRanker.RankAndFilter(ofSide, item.BookTitle, authors, series, preferredTypes);
        return ranked.FirstOrDefault(c => ListLibraryMatcher.FindMatch(item.BookTitle, authors, side, [c]) is not null);
    }

    private async Task<bool> TryMatchLibraryAsync(ListItemEntity item, IReadOnlyList<string> authors, FilterMainType side, CancellationToken cancellationToken)
    {
        var normalizedTitle = TitleNormalizer.Normalize(item.BookTitle);
        var candidates = await dbContext.Torrents.AsNoTracking()
            .Where(t => t.NormalizedTitleKey == normalizedTitle)
            .Select(t => new { t.MamId, t.MetadataJson })
            .ToListAsync(cancellationToken);

        var match = ListLibraryMatcher.FindMatch(
            item.BookTitle, authors, side,
            candidates.Select(c => new ListCandidate<long> { Metadata = TorrentMetadataSerializer.Deserialize(c.MetadataJson), Payload = c.MamId }));
        if (match is null)
        {
            return false;
        }

        SetSide(item, side, ListItemSideStatus.Existing, match.Payload);
        return true;
    }

    private void SetSide(ListItemEntity item, FilterMainType side, string status, long? mamId)
    {
        var now = _time.GetUtcNow();
        if (side == FilterMainType.Audio)
        {
            (item.AudioStatus, item.AudioMamId, item.AudioAt) = (status, mamId, now);
        }
        else
        {
            (item.EbookStatus, item.EbookMamId, item.EbookAt) = (status, mamId, now);
        }
    }

    private static TorrentMetadata? TryMap(MamTorrentRecord record)
    {
        try
        {
            return MamTorrentMapper.Map(record);
        }
        catch (UnrecognizedReferenceDataException)
        {
            return null;
        }
    }

    private static bool AllowsSide(CategoryFilter categories, FilterMainType side)
    {
        var group = side == FilterMainType.Audio ? categories.Audio : categories.Ebook;
        return group.Allowed is null || group.Allowed.Count > 0;
    }

    private static TorrentFilterCandidate ToFilterCandidate(MamTorrentRecord record, TorrentMetadata? metadata) => new()
    {
        Metadata = metadata!,
        UploaderName = record.UploaderName,
        Seeders = record.Seeders,
        Leechers = record.Leechers,
        TimesCompleted = record.TimesCompleted,
    };

    private static string StatusName(HardcoverListStatus status) => status switch
    {
        HardcoverListStatus.WantToRead => "want_to_read",
        HardcoverListStatus.CurrentlyReading => "currently_reading",
        HardcoverListStatus.Read => "read",
        HardcoverListStatus.Paused => "paused",
        HardcoverListStatus.DidNotFinish => "did_not_finish",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
