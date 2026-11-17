using System.Text.Json;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Lists;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>What one run of a list did.</summary>
/// <param name="Selected">Torrents selected; in a dry run, those that would have been.</param>
/// <param name="Failed">Books whose search failed.</param>
/// <param name="Books">Books on the list.</param>
/// <param name="Searched">Books searched for on MAM.</param>
/// <param name="DryRun">Whether the run was a dry run, which selects and writes nothing.</param>
/// <param name="UnreadableLines">Lines of a list file that couldn't be read as a book.</param>
public sealed record ListRunSummary(int Selected, int Failed, int Books = 0, int Searched = 0, bool DryRun = false, int UnreadableLines = 0)
{
    /// <summary>The selections now waiting to be downloaded: none in a dry run.</summary>
    public int SelectedForDownload => DryRun ? 0 : Selected;
}

/// <summary>
/// Runs one list of wanted books, such as a `[[hardcover_list]]`: reads its books from its source, keeps one
/// <see cref="ListItemEntity"/> per book, and searches MAM for the formats still missing, passing the best match through
/// the usual selection rules.
/// </summary>
/// <remarks>
/// The source is read before the MAM gate is taken, so a slow or failing source never holds up other MAM jobs. A
/// book that is still missing a format is searched for again a day later, or a week later once MAM has had nothing for
/// it for a week (<see cref="ListSearchSchedule"/>). A format whose selection was removed, or whose copy is gone from the
/// library, is looked for again. A dry run writes nothing: list rows are only built in memory.
/// </remarks>
public sealed class ListImporter(
    FetchshelfDbContext dbContext,
    MamClient mamClient,
    MamAccessGate accessGate,
    SelectionRunner selectionRunner,
    FormatPreferenceLists preferenceLists,
    TimeProvider? timeProvider = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    ILogger<ListImporter>? logger = null)
{
    private static readonly TimeSpan PolitenessDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>Where the second page of results starts: MAM's page holds 100.</summary>
    private const long SecondPageOffset = 100;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((wait, ct) => Task.Delay(wait, ct));
    private readonly ILogger<ListImporter> _logger = logger ?? NullLogger<ListImporter>.Instance;

    /// <summary>Whether this run is a dry run: the configured `dry_run`, or the Jobs page's <b>Dry run</b>. Set at the start of a run.</summary>
    private bool _dryRun;

    /// <param name="list">The list.</param>
    /// <param name="bookSource">Where the list's books are read from.</param>
    /// <param name="unsatBuffer">The top-level `unsat_buffer`, for a list that sets none of its own.</param>
    /// <param name="ignoreTorrents">`ignore_torrents`.</param>
    /// <param name="cancellationToken">Stops the run.</param>
    /// <param name="dryRun">Overrides the list's own `dry_run`, for the Jobs page's <b>Dry run</b>.</param>
    public async Task<ListRunSummary> RunAsync(
        ListDefinition list, IListBookSource bookSource, int unsatBuffer, IReadOnlyList<long> ignoreTorrents, CancellationToken cancellationToken = default,
        bool? dryRun = null)
    {
        _dryRun = dryRun ?? list.DryRun;
        var name = list.DisplayName;
        var grabberName = list.JobName;
        var books = await bookSource.GetBooksAsync(cancellationToken);
        var unreadableLines = bookSource.UnreadableLines;
        if (_dryRun)
        {
            _logger.LogInformation("List {Name}: dry run, nothing will be selected or written", name);
        }

        var grabFilters = list.Grab.Select(SharedFilterConfigMapper.ToTorrentFilter).ToList();
        var allowAudio = grabFilters.Any(f => AllowsSide(f.Categories, FilterMainType.Audio));
        var allowEbook = grabFilters.Any(f => AllowsSide(f.Categories, FilterMainType.Ebook));
        var items = await SyncItemsAsync(list, name, books, allowAudio, allowEbook, cancellationToken);
        await ReopenLostSidesAsync(list, name, items, cancellationToken);

        using var _ = await accessGate.AcquireAsync($"List {name}", cancellationToken);

        var account = await mamClient.GetAccountStateAsync(cancellationToken);
        unsatBuffer = list.UnsatBuffer ?? unsatBuffer;
        var currentActive = list.MaxActiveDownloads is null
            ? 0
            : await SelectionQueue.ActiveForGrabberAsync(dbContext, grabberName, cancellationToken: cancellationToken);
        var budget = SelectionBudget.Compute(account.UnsatLimit, account.UnsatCount, unsatBuffer, list.MaxActiveDownloads, currentActive);
        if (budget <= 0)
        {
            var explanation = SelectionBudget.Explain(account.UnsatLimit, account.UnsatCount, unsatBuffer, list.MaxActiveDownloads, currentActive);
            _logger.LogInformation(
                "List {Name}: {Books}, nothing searched, this run's limit is 0 selections ({BudgetExplanation})",
                name, Books(books.Count), explanation);
            return new ListRunSummary(0, 0, books.Count, 0, _dryRun, unreadableLines);
        }

        var selected = 0;
        var failed = 0;
        var searched = 0;
        var errors = await ItemErrors.LoadAsync(dbContext, list.ErrorStage, _dryRun, cancellationToken);
        foreach (var item in items.Where(i => i.RemovedAt is null && i.MarkedDoneAt is null).OrderBy(i => i.Position))
        {
            if (selected >= budget)
            {
                _logger.LogInformation("List {Name}: stopped at this run's limit of {Budget} selections", name, budget);
                break;
            }

            var failure = await errors.RunAsync(
                ErrorKey(item), item.BookTitle,
                async () =>
                {
                    var outcome = await ProcessItemAsync(list, name, grabberName, grabFilters, item, ignoreTorrents, (int)(budget - selected), cancellationToken);
                    selected += outcome.Selected;
                    searched += outcome.Searched ? 1 : 0;
                },
                markFailed: null,
                cancellationToken);
            if (failure is not null)
            {
                failed++;
                _logger.LogWarning(failure, "List {Name}: \"{Title}\" failed: {Reason}", name, item.BookTitle, failure.Message);
            }
        }

        _logger.LogInformation(
            "List {Name}: {Books}, {Searched} searched on MAM, {Selected} {Verb}{Failures}",
            name, Books(books.Count), searched, selected, _dryRun ? "would have been selected (dry run)" : "selected",
            failed == 0 ? string.Empty : $", {failed} failed");
        return new ListRunSummary(selected, failed, books.Count, searched, _dryRun, unreadableLines);
    }

    private static string ErrorKey(ListItemEntity item) => ErrorLog.ListItemKey(item.ListId, item.ExternalId);

    /// <summary>
    /// Brings the stored rows in line with the books the source returned: new books are added, known ones refreshed,
    /// and books no longer on the shelf or list marked removed (and active again if they come back).
    /// </summary>
    private async Task<List<ListItemEntity>> SyncItemsAsync(
        ListDefinition list, string name, IReadOnlyList<ListBook> books, bool allowAudio, bool allowEbook, CancellationToken cancellationToken)
    {
        var listId = list.ListId;
        var now = _time.GetUtcNow();
        var tracked = !_dryRun;
        var query = tracked ? dbContext.ListItems.AsQueryable() : dbContext.ListItems.AsNoTracking();
        var existing = await query.Where(i => i.ListId == listId).ToDictionaryAsync(i => i.ExternalId, cancellationToken);

        if (tracked)
        {
            var stored = await dbContext.Lists.FindAsync([listId], cancellationToken);
            if (stored is null)
            {
                dbContext.Lists.Add(new ListEntity { Id = listId, Name = name, ListType = list.ListType, CreatedAt = now, UpdatedAt = now });
            }
            else
            {
                stored.Name = name;
                stored.UpdatedAt = now;
            }
        }

        var preferFormat = list.PreferFormat?.ToString();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var position = 0; position < books.Count; position++)
        {
            var book = books[position];
            var externalId = book.ExternalId;
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

            item.Position = position;
            item.BookTitle = book.Title;
            item.AuthorsJson = JsonSerializer.Serialize(book.Authors);
            item.SeriesName = book.SeriesName;
            item.SeriesNumber = book.SeriesNumber;
            item.CoverUrl = book.CoverUrl;
            item.BookUrl = book.BookUrl;
            var identifiers = BookIdentifiers.From(book.Identifiers).Order(StringComparer.Ordinal).ToList();
            item.IdentifiersJson = identifiers.Count == 0 ? null : JsonSerializer.Serialize(identifiers);
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

    /// <summary>
    /// Looks again for a format whose result no longer holds, so the list doesn't keep saying *selected* or *in the
    /// library* about something that is gone:
    /// <list type="bullet">
    /// <item>*Selected*: the list's own selection of that torrent was removed from the queue, and no torrent of it was
    /// recorded, so it was never downloaded. A side the selection's duplicate check set, which never had a selection of
    /// its own, is left alone.</item>
    /// <item>*Existing*: no torrent of that MAM id is recorded, none is noted as its duplicate, and the library no
    /// longer has the book by its title either.</item>
    /// </list>
    /// Such a side goes back to not looked for (its MAM id kept as the last one seen), and the book is searched on this run.
    /// </summary>
    private async Task ReopenLostSidesAsync(ListDefinition list, string name, List<ListItemEntity> items, CancellationToken cancellationToken)
    {
        var active = items.Where(i => i.RemovedAt is null && i.MarkedDoneAt is null).ToList();
        var mamIds = active
            .SelectMany(i => new[] { (i.AudioStatus, i.AudioMamId), (i.EbookStatus, i.EbookMamId) })
            .Where(side => side.Item1 is ListItemSideStatus.Selected or ListItemSideStatus.Existing && side.Item2 is not null)
            .Select(side => side.Item2!.Value)
            .Distinct()
            .ToList();
        if (mamIds.Count == 0)
        {
            return;
        }

        var selections = await dbContext.SelectedTorrents.AsNoTracking()
            .Where(s => mamIds.Contains(s.MamId))
            .Select(s => new { s.MamId, Active = s.RemovedAt == null })
            .ToListAsync(cancellationToken);
        var everSelected = selections.Select(s => s.MamId).ToHashSet();
        var stillSelected = selections.Where(s => s.Active).Select(s => s.MamId).ToHashSet();
        var recorded = (await dbContext.Torrents.AsNoTracking().Where(t => mamIds.Contains(t.MamId)).Select(t => t.MamId).ToListAsync(cancellationToken)).ToHashSet();
        var duplicates = (await dbContext.DuplicateTorrents.AsNoTracking().Where(d => mamIds.Contains(d.MamId)).Select(d => d.MamId).ToListAsync(cancellationToken)).ToHashSet();

        foreach (var item in active)
        {
            var authors = JsonSerializer.Deserialize<List<string>>(item.AuthorsJson) ?? [];
            foreach (var side in new[] { FilterMainType.Audio, FilterMainType.Ebook })
            {
                var (status, mamId) = side == FilterMainType.Audio ? (item.AudioStatus, item.AudioMamId) : (item.EbookStatus, item.EbookMamId);
                if (mamId is not { } id)
                {
                    continue;
                }

                var lost = status switch
                {
                    ListItemSideStatus.Selected => everSelected.Contains(id) && !stillSelected.Contains(id) && !recorded.Contains(id),
                    ListItemSideStatus.Existing => !recorded.Contains(id) && !duplicates.Contains(id)
                        && await FindLibraryMatchAsync(item, authors, side, cancellationToken) is null,
                    _ => false,
                };
                if (!lost)
                {
                    continue;
                }

                if (side == FilterMainType.Audio)
                {
                    item.AudioStatus = null;
                }
                else
                {
                    item.EbookStatus = null;
                }

                (item.LastSearchedAt, item.MissingSince) = (null, null);
                _logger.LogInformation(
                    "List {Name}: \"{Title}\": the {Format} {What} MAM torrent {MamId} is gone; searching for it again",
                    name, item.BookTitle, side == FilterMainType.Audio ? "audiobook" : "ebook",
                    status == ListItemSideStatus.Selected ? "selection of" : "copy from", id);
            }
        }

        if (!_dryRun)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <param name="budgetLeft">
    /// The selections this run may still make. A book found in both formats may need two; with room for only one, the
    /// second format is left for the next run, which searches the book again rather than going over the limit.
    /// </param>
    private async Task<(int Selected, bool Searched)> ProcessItemAsync(
        ListDefinition list, string name, string grabberName, IReadOnlyList<TorrentFilter> grabFilters, ListItemEntity item,
        IReadOnlyList<long> ignoreTorrents, int budgetLeft, CancellationToken cancellationToken)
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
        if ((!needAudio && !needEbook) || !ListSearchSchedule.IsDue(item.LastSearchedAt, item.MissingSince, now))
        {
            return (0, false);
        }

        var identifiers = (item.IdentifiersJson is { } json ? JsonSerializer.Deserialize<List<string>>(json) : null)?
            .ToHashSet(StringComparer.Ordinal) ?? [];

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
        for (var i = 0; i < list.Grab.Count && ((needAudio && audioWinner is null) || (needEbook && ebookWinner is null)); i++)
        {
            var filter = ListGrabSearchTranslator.NarrowToNeededSides(grabFilters[i], needAudio && audioWinner is null, needEbook && ebookWinner is null);
            var grab = list.Grab[i];

            // A full first page that holds nothing like the book: very common title words. The second page is read once.
            for (var pageOffset = 0L; pageOffset <= SecondPageOffset; pageOffset += SecondPageOffset)
            {
                var query = ListGrabSearchTranslator.ToSearchQuery(filter, queryText, pageOffset);
                var result = await mamClient.SearchAsync(query, cancellationToken);
                await _delay(PolitenessDelay, cancellationToken);

                var candidates = result.Data
                    .Select(record => (Record: record, Metadata: GrabMatching.TryMap(record)))
                    .Where(c => c.Metadata is not null && TorrentFilterMatcher.Matches(filter, GrabMatching.ToFilterCandidate(c.Record, c.Metadata!)))
                    .Select(c => new ListCandidate<MamTorrentRecord> { Metadata = c.Metadata!, Payload = c.Record })
                    .ToList();

                // A block only takes a torrent its own cost accepts. The search isn't limited by cost, so a free block also
                // sees uploads that aren't free; those are left for the later blocks, which may pay for them.
                var affordable = candidates.Where(c => GrabMatching.Accepts(grab.Cost, c.Payload)).ToList();
                if (needAudio && audioWinner is null)
                {
                    if (Best(affordable, FilterMainType.Audio, item, authors, series, identifiers, preferenceLists.AudioTypes) is { } audio)
                    {
                        (audioWinner, audioGrab) = (audio, grab);
                    }
                    else
                    {
                        audioTooExpensive ??= Best(candidates, FilterMainType.Audio, item, authors, series, identifiers, preferenceLists.AudioTypes);
                    }
                }

                if (needEbook && ebookWinner is null)
                {
                    if (Best(affordable, FilterMainType.Ebook, item, authors, series, identifiers, preferenceLists.EbookTypes) is { } ebook)
                    {
                        (ebookWinner, ebookGrab) = (ebook, grab);
                    }
                    else
                    {
                        ebookTooExpensive ??= Best(candidates, FilterMainType.Ebook, item, authors, series, identifiers, preferenceLists.EbookTypes);
                    }
                }

                var stillLooking = (needAudio && audioWinner is null && audioTooExpensive is null)
                    || (needEbook && ebookWinner is null && ebookTooExpensive is null);
                if (!stillLooking || result.Data.Count < query.PerPage)
                {
                    break;
                }
            }
        }

        if (audioWinner is not null && ebookWinner is not null && list.PreferFormat is { } preferred)
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
        var leftForNextRun = false;
        if (needAudio)
        {
            selected += await RecordSideAsync(list, name, grabberName, item, FilterMainType.Audio, audioWinner, audioGrab, audioTooExpensive, ignoreTorrents, cancellationToken);
        }

        if (needEbook && ebookWinner is not null && selected >= budgetLeft)
        {
            // The audiobook took the run's last selection: the ebook waits, and isn't recorded as not found.
            leftForNextRun = true;
            _logger.Log(
                Detail(), "List {Name}: \"{Title}\": the ebook is left for the next run, which has room to select it",
                name, item.BookTitle);
        }
        else if (needEbook)
        {
            selected += await RecordSideAsync(list, name, grabberName, item, FilterMainType.Ebook, ebookWinner, ebookGrab, ebookTooExpensive, ignoreTorrents, cancellationToken);
        }

        if (leftForNextRun)
        {
            // Not searched as far as the schedule goes, so the next run looks for the ebook again; it is on MAM.
            item.MissingSince = null;
            return (selected, true);
        }

        // Missing from MAM only when nothing turned up for any format looked for: a match too dear, or one left out for
        // the preferred format, is still something.
        var sidesLookedFor = new[] { (needAudio, item.AudioStatus), (needEbook, item.EbookStatus) }.Where(s => s.Item1).ToList();
        item.MissingSince = sidesLookedFor.All(s => s.Item2 == ListItemSideStatus.NotFound) ? item.MissingSince ?? now : null;
        item.LastSearchedAt = now;
        return (selected, true);
    }

    private async Task<int> RecordSideAsync(
        ListDefinition list, string name, string grabberName, ListItemEntity item, FilterMainType side,
        ListCandidate<MamTorrentRecord>? winner, GrabEntry? grab, ListCandidate<MamTorrentRecord>? tooExpensive,
        IReadOnlyList<long> ignoreTorrents, CancellationToken cancellationToken)
    {
        var format = side == FilterMainType.Audio ? "audiobook" : "ebook";
        if (winner is null && tooExpensive is not null)
        {
            SetSide(item, side, ListItemSideStatus.Wanted, tooExpensive.Payload.MamId);
            _logger.Log(
                Detail(), "List {Name}: \"{Title}\": {Format} MAM torrent {MamId} matches, but no grab block's cost accepts it",
                name, item.BookTitle, format, tooExpensive.Payload.MamId);
            return 0;
        }

        if (winner is null)
        {
            SetSide(item, side, ListItemSideStatus.NotFound, mamId: null);
            _logger.Log(Detail(), "List {Name}: \"{Title}\": no {Format} on MAM matches", name, item.BookTitle, format);
            return 0;
        }

        var outcome = await selectionRunner.ProcessCandidateAsync(
            winner.Payload, winner.Metadata, ignoreTorrents, grab!.Cost, autograbCategory: list.Category, grabberName: grabberName,
            dryRun: _dryRun, cancellationToken: cancellationToken);
        var status = outcome switch
        {
            SelectionOutcome.Selected or SelectionOutcome.SelectedAsUpgrade or SelectionOutcome.DuplicateOfSelected or SelectionOutcome.RefreshedSelected => ListItemSideStatus.Selected,
            SelectionOutcome.DuplicateOfLibrary or SelectionOutcome.RefreshedLibrary => ListItemSideStatus.Existing,
            SelectionOutcome.CostNotFree => ListItemSideStatus.Wanted,
            _ => ListItemSideStatus.NotFound,
        };
        SetSide(item, side, status, winner.Payload.MamId);
        _logger.Log(
            Detail(), "List {Name}: \"{Title}\": {Format} MAM torrent {MamId}: {Outcome}",
            name, item.BookTitle, format, winner.Payload.MamId, outcome.Describe(_dryRun));
        return outcome.IsSelected() ? 1 : 0;
    }

    private static string Books(int count) => count == 1 ? "1 book" : $"{count} books";


    private LogLevel Detail() => _dryRun ? LogLevel.Information : LogLevel.Debug;

    private static ListCandidate<MamTorrentRecord>? Best(
        IReadOnlyList<ListCandidate<MamTorrentRecord>> candidates, FilterMainType side, ListItemEntity item,
        IReadOnlyList<string> authors, (string Name, double Number)? series, IReadOnlySet<string> identifiers, IReadOnlyList<string> preferredTypes)
    {
        var ofSide = candidates.Where(c => FilterMainTypeMapper.FromMediaType(c.Metadata.MediaType) == side).ToList();
        // An upload with one of the book's ISBNs or ASINs is the book, however far its title is from the list's.
        if (ListCandidateRanker.IdentifierMatches(ofSide, identifiers, preferredTypes) is [var byIdentifier, ..])
        {
            return byIdentifier;
        }

        // The ranker keeps the closest results; the library matcher's thresholds then make sure it is the same book.
        var ranked = ListCandidateRanker.RankAndFilter(ofSide, item.BookTitle, authors, series, preferredTypes);
        return ranked.FirstOrDefault(c => ListLibraryMatcher.FindMatch(item.BookTitle, authors, side, [c]) is not null);
    }

    private async Task<bool> TryMatchLibraryAsync(ListItemEntity item, IReadOnlyList<string> authors, FilterMainType side, CancellationToken cancellationToken)
    {
        if (await FindLibraryMatchAsync(item, authors, side, cancellationToken) is not { } mamId)
        {
            return false;
        }

        SetSide(item, side, ListItemSideStatus.Existing, mamId);
        return true;
    }

    /// <summary>The MAM id of a recorded torrent that is the book in this format, by its title; null when there is none.</summary>
    private async Task<long?> FindLibraryMatchAsync(ListItemEntity item, IReadOnlyList<string> authors, FilterMainType side, CancellationToken cancellationToken)
    {
        var normalizedTitle = TitleNormalizer.Normalize(item.BookTitle);
        var candidates = await dbContext.Torrents.AsNoTracking()
            .Where(t => t.NormalizedTitleKey == normalizedTitle)
            .Select(t => new { t.MamId, t.MetadataJson })
            .ToListAsync(cancellationToken);

        return ListLibraryMatcher.FindMatch(
            item.BookTitle, authors, side,
            candidates.Select(c => new ListCandidate<long> { Metadata = TorrentMetadataSerializer.Deserialize(c.MetadataJson), Payload = c.MamId }))?.Payload;
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

    private static bool AllowsSide(CategoryFilter categories, FilterMainType side)
    {
        var group = side == FilterMainType.Audio ? categories.Audio : categories.Ebook;
        return group.Allowed is null || group.Allowed.Count > 0;
    }
}
