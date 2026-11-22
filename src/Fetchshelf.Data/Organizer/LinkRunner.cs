using System.Diagnostics;
using System.Text.Json;
using Fetchshelf.Clients.Abs;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Organizer;

/// <summary>What one link run did: torrents newly linked, torrents that could not be linked, and the backlog it left.</summary>
/// <param name="WaitingForLookup">Torrents not looked up on MAM because the run reached <see cref="LinkRunner.MaxLookupsPerRun"/>: the next runs do.</param>
/// <param name="WaitingForMam">Torrents MAM didn't know when last asked, looked up again once a day has passed.</param>
public sealed record LinkRunSummary(int Linked, int Failed, int WaitingForLookup = 0, int WaitingForMam = 0);

/// <summary>Whether Refresh and relink placed the torrent's files again, and a message saying what happened.</summary>
public sealed record RelinkResult(bool Succeeded, string Message);

/// <summary>What a Refresh found: changed details, the same details, or no torrent on MAM to refresh from.</summary>
public enum RefreshOutcome
{
    Changed,
    Unchanged,
    NotFoundOnMam,
}

/// <summary>The settings the Link job links by, besides the libraries and the format lists.</summary>
/// <param name="ExcludeNarratorInLibraryDir">`exclude_narrator_in_library_dir`: folder names leave out the narrator.</param>
/// <param name="FirstLanguage">The first of the top-level `languages`, which `language_in_folder = "other"` leaves unmarked.</param>
/// <param name="BatchYield">
/// How long the write lock is left free between two batches of a run; <see cref="SqliteWriteBatch.YieldTime"/> unless
/// given. Tests whose lookups answer at once pass zero: there a batch ends at nearly every torrent.
/// </param>
/// <param name="MamGenreNames">`genres = true`: metadata.json and metadata.opf name the genres as MAM does, with Fiction or Nonfiction.</param>
public sealed record LinkRunnerOptions(bool ExcludeNarratorInLibraryDir, Language? FirstLanguage = null, TimeSpan? BatchYield = null, bool MamGenreNames = false);

/// <summary>
/// Runs the link run, linking, library mismatch detection, refresh, and manual metadata override.
/// </summary>
public sealed class LinkRunner(
    FetchshelfDbContext dbContext,
    EventStore eventStore,
    IMetadataLookup metadataLookup,
    IReadOnlyList<Library> libraries,
    FormatPreferenceLists formatPreferenceLists,
    LinkRunnerOptions options,
    AbsClient? absClient = null,
    ILogger<LinkRunner>? logger = null,
    LibraryWriteGate? writeGate = null)
{
    private readonly ILogger<LinkRunner> _logger = logger ?? NullLogger<LinkRunner>.Instance;
    private readonly LibraryWriteGate _writeGate = writeGate ?? new LibraryWriteGate();
    private int _linked;
    private int _failed;

    /// <summary>The run's batches of torrents. See <see cref="RunCoreAsync"/>.</summary>
    private readonly SqliteWriteBatch _batch = new(dbContext, BatchSize, BatchMaxAge, options.BatchYield);
    private const int BatchSize = 100;

    /// <summary>
    /// The longest a batch stays open. Torrents whose files are checked or placed on a slow disk can make 100 of them take
    /// a minute, and the batch's transaction holds the write lock all that time: another job's save then waited out its
    /// 30 second busy timeout and failed with "database is locked". Between two batches the lock is left free a moment
    /// (<see cref="SqliteWriteBatch.YieldTime"/>), or a waiting save could miss every gap and fail the same way.
    /// </summary>
    private static readonly TimeSpan BatchMaxAge = TimeSpan.FromSeconds(2);

    /// <summary>The error a torrent gets when MAM has no torrent with its info-hash. Such a torrent is looked up again only after <see cref="NotOnMamRetryAfter"/>.</summary>
    public const string NotOnMamError = "MAM has no torrent with this info-hash.";

    /// <summary>
    /// A torrent MAM doesn't know is usually from another tracker, or was removed from MAM; neither changes within
    /// minutes. Asking again every run would only cost MAM requests.
    /// </summary>
    public static readonly TimeSpan NotOnMamRetryAfter = TimeSpan.FromDays(1);

    /// <summary>
    /// The most MAM lookups one Link job makes. Pointing Fetchshelf at a client full of torrents it doesn't know yet
    /// would otherwise send MAM one lookup per torrent in a single run; this way the backlog is worked through over
    /// several runs, and known torrents keep being processed meanwhile.
    /// </summary>
    public const int MaxLookupsPerRun = 200;

    private int _lookups;
    private int _lookupsDeferred;
    private int _waitingForRetry;

    /// <summary>Processes every 100%-complete torrent reported by one configured qBittorrent connection; a failure on one torrent is recorded as a linker error and the run continues.</summary>
    public async Task<LinkRunSummary> RunAsync(QBittorrentConnection connection, QBittorrentClient client, CancellationToken cancellationToken = default)
    {
        var (linkedBefore, failedBefore) = (_linked, _failed);
        await RunCoreAsync(connection, client, cancellationToken);
        var summary = new LinkRunSummary(_linked - linkedBefore, _failed - failedBefore, _lookupsDeferred, _waitingForRetry);
        (_waitingForRetry, _lookupsDeferred) = (0, 0);
        return summary;
    }

    private async Task RunCoreAsync(QBittorrentConnection connection, QBittorrentClient client, CancellationToken cancellationToken)
    {
        var torrents = await client.ListTorrentsAsync(cancellationToken);
        var finished = torrents.Where(t => t.IsFinished).ToList();
        await RetireFinishedSelectionsAsync(finished, cancellationToken);

        // Most torrents only get a status update, and committing each one on its own would cost a disk sync per
        // torrent: thousands per run in a large library. They are committed in batches instead. A batch's transaction
        // holds the database's write lock, so it is committed before anything that can wait, such as a MAM lookup,
        // and right after a torrent's files are placed, so a crash can't leave files in a library without a record.
        try
        {
            foreach (var torrent in finished)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // One torrent at a time with a bulk action or the Clean job. Waiting for the gate can take a while, so the
                // batch is committed first: its transaction would hold the database's write lock that the holder needs.
                var gate = _writeGate.TryAcquire();
                if (gate is null)
                {
                    await CommitBatchAsync();
                    gate = await _writeGate.AcquireAsync(cancellationToken);
                }

                using var heldGate = gate;
                await _batch.BeginAsync(cancellationToken);

                try
                {
                    await ProcessFinishedTorrentAsync(connection, client, torrent, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested
                    && ex is not (MamRateLimitedException or MamAccountBlockedException))
                {
                    // One bad torrent (e.g. a lookup or file error) must not stop the rest of the run.
                    // Rate limiting and account blocks still end the run: every later lookup would fail too.
                    dbContext.ChangeTracker.Clear();
                    await RecordLinkerErrorAsync(torrent, ex.Message, cancellationToken);
                }
                finally
                {
                    // Every path above saves what it changed, so nothing is lost here. Without this, every torrent of
                    // the run would stay tracked, and each later save would scan them all: a large library's run would
                    // grow with the square of its size.
                    dbContext.ChangeTracker.Clear();
                }

                await _batch.ItemDoneAsync();
            }
        }
        finally
        {
            // Also when the run ends early: the torrents already processed keep their changes.
            await CommitBatchAsync();
        }

        LogSkippedLookups();
    }

    private void LogSkippedLookups()
    {
        if (_waitingForRetry > 0)
        {
            _logger.LogInformation(
                "{Count} {Torrents} MAM didn't know when last asked will be looked up again once a day has passed",
                _waitingForRetry, _waitingForRetry == 1 ? "torrent" : "torrents");
        }

        if (_lookupsDeferred > 0)
        {
            _logger.LogInformation(
                "Looked up {Max} torrents on MAM, the most for one run; {Count} more will be looked up in the next runs",
                MaxLookupsPerRun, _lookupsDeferred);
        }
    }

    /// <summary>Whether MAM had no torrent with this info-hash when asked less than <see cref="NotOnMamRetryAfter"/> ago.</summary>
    private async Task<bool> NotOnMamRecentlyAsync(string hash, CancellationToken cancellationToken) =>
        await dbContext.ErroredTorrents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Stage == ErrorStage.Linker && e.Key == hash, cancellationToken) is { ErrorText: NotOnMamError } error
        && DateTimeOffset.UtcNow - error.OccurredAt < NotOnMamRetryAfter;

    /// <summary>Commits the current batch, if one is open; later saves commit on their own until the next batch starts.</summary>
    private Task CommitBatchAsync() => _batch.CommitAsync();

    /// <summary>
    /// A selection that was started has done its job once its torrent finishes: the Torrents list takes over, so the
    /// selection leaves the queue. Done up front and saved on its own, so a later linking failure cannot undo it.
    /// </summary>
    private async Task RetireFinishedSelectionsAsync(IReadOnlyList<QBittorrentTorrent> finished, CancellationToken cancellationToken)
    {
        var hashes = finished.Select(t => t.Hash).ToList();
        var mamIds = await dbContext.Torrents.Where(t => hashes.Contains(t.Id)).Select(t => t.MamId).ToListAsync(cancellationToken);
        if (await SelectionRetirement.RetireStartedAsync(dbContext, mamIds, cancellationToken) > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ProcessFinishedTorrentAsync(
        QBittorrentConnection connection, QBittorrentClient client, QBittorrentTorrent torrent, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Torrents.FirstOrDefaultAsync(t => t.Id == torrent.Hash, cancellationToken);

        // The library match needs only the client's category, save path and tags, so it comes before
        // the MAM lookup: a torrent no library claims must cost no request.
        var (mappedSavePath, candidates) = MatchLibrary(connection, torrent);

        TorrentMetadata metadata;

        if (existing is not null)
        {
            existing.QBittorrentCategory = torrent.Category;
            ClientStatuses.Update(existing, torrent.State);
            metadata = TorrentMetadataSerializer.Deserialize(existing.MetadataJson);

            if ((existing.ReplacedWithTorrentId is not null || existing.UnlinkedAt is not null) && existing.LibraryPath is null)
            {
                // Retired in favour of a better copy, or unlinked by hand; only an explicit relink brings it back.
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            if (existing.CollectionBookCount is > 0)
            {
                // Linked as its books: they follow the collection's torrent in qBittorrent.
                await SyncBooksAsync(existing, torrent, cancellationToken);
                await ErrorLog.ClearAsync(dbContext, ErrorStage.Linker, torrent.Hash, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            if (existing.LibraryPath is not null)
            {
                // Already linked: sync and recompute mismatch; do not touch files or metadata here.
                await SyncMismatchAsync(existing, connection, torrent, metadata, cancellationToken);
                await ErrorLog.ClearAsync(dbContext, ErrorStage.Linker, torrent.Hash, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            // Tracked (metadata assigned, e.g. manually) but not yet linked - fall through and link now.
        }
        else
        {
            if (candidates.Count == 0)
            {
                return; // no library matches; ignored
            }

            if (await NotOnMamRecentlyAsync(torrent.Hash, cancellationToken))
            {
                _waitingForRetry++;
                return;
            }

            if (_lookups >= MaxLookupsPerRun)
            {
                _lookupsDeferred++;
                return;
            }

            // A lookup can wait on MAM or on another MAM job: the batch must not hold the write lock meanwhile.
            await CommitBatchAsync();
            _lookups++;
            TorrentMetadata? looked;
            try
            {
                looked = await metadataLookup.TryLookupByInfoHashAsync(torrent.Hash, cancellationToken);
            }
            catch (UnrecognizedReferenceDataException ex)
            {
                await HandleUnreadableDetailsAsync(connection, client, torrent, ex, cancellationToken);
                return;
            }

            if (looked is null)
            {
                await RecordLinkerErrorAsync(torrent, NotOnMamError, cancellationToken);
                return;
            }

            metadata = looked;
        }

        // Of the libraries its category, folder and tags give it, the first whose genre condition takes the book.
        var matchedIndex = ChooseLibrary(candidates, metadata);

        // The series layout names nothing after the author, so it links books without one.
        if (metadata.Authors.Count == 0 && !(matchedIndex is { } index && libraries[index].IsSeriesLayout))
        {
            // Without a record, the Errors page's Edit needs what MAM said to start from: it becomes the record when saved.
            var lookedUpMetadata = existing is null ? TorrentMetadataSerializer.Serialize(metadata) : null;
            await RecordLinkerErrorAsync(torrent, NoAuthorError, cancellationToken, lookedUpMetadata);
            return;
        }

        if (matchedIndex is null && candidates.Count > 0)
        {
            await RecordNoLibraryForGenresAsync(existing, torrent, metadata, cancellationToken);
            return;
        }

        if (matchedIndex is null)
        {
            // Known but unclaimed: flagged as a problem rather than left waiting without a word. Linking it later,
            // once a library accepts it, clears the flag.
            existing!.LibraryMismatchState = nameof(LibraryMismatchState.NoLibrary);
            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "{Title}: no library accepts its qBittorrent category {Category}, save path {SavePath} or tags",
                existing.Title, string.IsNullOrEmpty(torrent.Category) ? "(none)" : torrent.Category, mappedSavePath);
            return;
        }

        var library = libraries[matchedIndex.Value];
        var files = await client.ListFilesAsync(torrent.Hash, cancellationToken);
        if (SplitCollection(library, metadata, files, mappedSavePath, existing?.SplitCollection) is { } books)
        {
            await LinkBooksAsync(existing, torrent, library, metadata, books, files, mappedSavePath);
            return;
        }

        var (bestAudioExtension, bestEbookExtension, formatError) = ChooseFormats(library, metadata, files);
        if (formatError is not null)
        {
            await RecordLinkerErrorAsync(torrent, formatError, cancellationToken);
            return;
        }

        var bookFolder = await BookFolderAsync(library, metadata, torrent.Hash, Directory.Exists, cancellationToken);
        var placement = PlaceFiles(library, metadata, files, mappedSavePath, bestAudioExtension, bestEbookExtension, bookFolder);

        // The files are in the library now: recording them must not be cut short by a cancellation.
        cancellationToken = CancellationToken.None;
        if (existing is null)
        {
            existing = new TorrentEntity
            {
                Id = torrent.Hash,
                MamId = metadata.MamId,
                NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
                Title = metadata.Title,
                MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            dbContext.Torrents.Add(existing);
        }

        RecordPlacement(existing, torrent, library, placement, bestAudioExtension, bestEbookExtension);

        if (absClient is not null && existing.AbsId is not null)
        {
            await absClient.UpdateItemMetadataAsync(existing.AbsId, metadata, cancellationToken);
        }

        await AppendLinkedEventAsync(existing, library, placement.Folder, cancellationToken);
        await ErrorLog.ClearAsync(dbContext, ErrorStage.Linker, torrent.Hash, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitBatchAsync(); // the files are in place: their record must not wait for the rest of the batch
        _linked++;
        _logger.LogInformation("{Placement}", DescribePlacement(torrent.Name, placement));
    }

    /// <summary>
    /// The books of a collection, when the torrent's own choice, or else <paramref name="library"/>'s `split_collections`
    /// (the global one unless it sets its own), says to split, and the torrent is one (<see cref="CollectionSplitter"/>); null otherwise. An epub is read in the
    /// download folder for its own title, series number, ISBN and description.
    /// </summary>
    /// <param name="choice">The torrent's own choice from its page (<see cref="TorrentEntity.SplitCollection"/>); null follows the library.</param>
    private IReadOnlyList<CollectionBook>? SplitCollection(
        Library library, TorrentMetadata metadata, IReadOnlyList<QBittorrentFile> files, string mappedSavePath, bool? choice)
    {
        if (!(choice ?? library.SplitCollections))
        {
            return null;
        }

        EpubInfo? ReadEpub(string relativePath)
        {
            try
            {
                using var stream = File.OpenRead(Path.Combine(mappedSavePath, relativePath));
                return EpubMetadataReader.Read(stream);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return CollectionSplitter.Split(
            metadata, files.Select(f => new TorrentFileEntry(f.Name, f.Size)).ToList(), library.EbookTypes ?? formatPreferenceLists.EbookTypes, ReadEpub);
    }

    /// <summary>
    /// Links a collection as its books: each placed as a single download of it would be, with its own folder (or, in a
    /// series library, its own file in the series folder), metadata files, cover, record and Linked event. The
    /// collection's record keeps what qBittorrent and MAM know of the torrent, and no folder of its own.
    /// </summary>
    private async Task LinkBooksAsync(
        TorrentEntity? collection, QBittorrentTorrent torrent, Library library, TorrentMetadata metadata, IReadOnlyList<CollectionBook> books,
        IReadOnlyList<QBittorrentFile> files, string mappedSavePath)
    {
        // Files are placed from here on: recording them must not be cut short by a cancellation.
        var cancellationToken = CancellationToken.None;
        if (collection is null)
        {
            collection = new TorrentEntity
            {
                Id = torrent.Hash,
                MamId = metadata.MamId,
                NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
                Title = metadata.Title,
                MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            dbContext.Torrents.Add(collection);
        }

        var known = await dbContext.Torrents.Where(t => t.CollectionTorrentId == torrent.Hash).ToDictionaryAsync(t => t.Id, cancellationToken);
        var placements = new List<string>();
        foreach (var book in books)
        {
            var id = CollectionBookId.For(torrent.Hash, book.Position);
            var chosen = files.Where(f => book.Files.Contains(f.Name, StringComparer.Ordinal)).ToList();

            // Details edited by hand on a book are kept, as for any torrent, and place it: its folder follows the edit.
            var bookMetadata = known.TryGetValue(id, out var record)
                ? HandEdits.KeepOver(TorrentMetadataSerializer.Deserialize(record.MetadataJson), book.Metadata)
                : book.Metadata;
            var folder = await BookFolderAsync(library, bookMetadata, id, Directory.Exists, cancellationToken);
            var placement = PlaceFiles(library, bookMetadata, chosen, mappedSavePath, null, book.Format, folder);
            LinkCover(library, book, placement.Folder, mappedSavePath);

            if (record is null)
            {
                record = new TorrentEntity
                {
                    Id = id,
                    MamId = metadata.MamId,
                    NormalizedTitleKey = TitleNormalizer.Normalize(bookMetadata.Title),
                    Title = bookMetadata.Title,
                    MetadataJson = TorrentMetadataSerializer.Serialize(bookMetadata),
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                dbContext.Torrents.Add(record);
            }
            else
            {
                record.MetadataJson = TorrentMetadataSerializer.Serialize(bookMetadata);
                record.Title = bookMetadata.Title;
                record.NormalizedTitleKey = TitleNormalizer.Normalize(bookMetadata.Title);
            }

            record.CollectionTorrentId = torrent.Hash;
            record.UnlinkedAt = null;
            record.ReplacedWithTorrentId = null;
            record.ReplacedAt = null;
            RecordPlacement(record, torrent, library, placement, null, book.Format);
            await AppendLinkedEventAsync(record, library, placement.Folder, cancellationToken);
            placements.Add(DescribePlacement(bookMetadata.Title, placement));
        }

        // A book of an earlier split that is no longer one of them (the torrent's files changed) is unlinked.
        var positions = books.Select(b => CollectionBookId.For(torrent.Hash, b.Position)).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in known.Values.Where(r => !positions.Contains(r.Id) && r.LibraryPath is not null))
        {
            gone.LibraryPath = null;
            gone.LinkedFilesJson = null;
            gone.UnlinkedAt = DateTimeOffset.UtcNow;
        }

        collection.LibraryPath = null;
        collection.LinkedFilesJson = null;
        collection.CollectionBookCount = books.Count;
        collection.ChosenAudioExtension = null;
        collection.ChosenEbookExtension = null;
        collection.QBittorrentCategory = torrent.Category;
        collection.LinkerName = library.Name;
        collection.LibraryMismatchState = null;
        ClientStatuses.Update(collection, torrent.State);
        await ErrorLog.ClearAsync(dbContext, ErrorStage.Linker, torrent.Hash, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitBatchAsync(); // the files are in place: their records must not wait for the rest of the batch
        _linked += books.Count;
        _logger.LogInformation(
            "Linked the collection {Torrent} as {Count} books: {Books}", torrent.Name, books.Count, string.Join("; ", placements));
    }

    /// <summary>
    /// Links a book's cover, an image from its own folder in the torrent, as <c>cover.jpg</c> (or <c>.png</c>,
    /// <c>.webp</c>) beside its files, unless the folder has one already. Not in a series library, whose folders hold
    /// many books, nor without files placed.
    /// </summary>
    private static void LinkCover(Library library, CollectionBook book, string folder, string mappedSavePath)
    {
        if (book.CoverFile is not { } cover || library.IsSeriesLayout || library.Method == LibraryLinkMethod.NoLink)
        {
            return;
        }

        var extension = Path.GetExtension(cover).ToLowerInvariant() is ".jpeg" ? ".jpg" : Path.GetExtension(cover).ToLowerInvariant();
        var destination = Path.Combine(folder, "cover" + extension);
        if (!File.Exists(destination))
        {
            FileLinker.Link(Path.Combine(mappedSavePath, cover), destination, library.Method);
        }
    }

    /// <summary>A split collection's books follow its torrent: its category and its state in qBittorrent.</summary>
    private async Task SyncBooksAsync(TorrentEntity collection, QBittorrentTorrent torrent, CancellationToken cancellationToken)
    {
        foreach (var book in await dbContext.Torrents.Where(t => t.CollectionTorrentId == collection.Id).ToListAsync(cancellationToken))
        {
            book.QBittorrentCategory = torrent.Category;
            ClientStatuses.Update(book, torrent.State);
        }
    }

    /// <summary>
    /// MAM has the torrent, but its details hold a value Fetchshelf doesn't know, such as a new media type. With
    /// `on_invalid_torrent`, its category and tags are applied in qBittorrent: a category no library uses keeps the
    /// torrent from being looked up again on every run. Either way the Link error is recorded.
    /// </summary>
    private async Task HandleUnreadableDetailsAsync(
        QBittorrentConnection connection, QBittorrentClient client, QBittorrentTorrent torrent,
        UnrecognizedReferenceDataException exception, CancellationToken cancellationToken)
    {
        if (connection.OnInvalidTorrent is not { } setting)
        {
            await RecordLinkerErrorAsync(torrent, exception.Message, cancellationToken);
            return;
        }

        await QBittorrentActions.ApplyAsync(client, torrent.Hash, setting, cancellationToken);
        await RecordLinkerErrorAsync(
            torrent,
            $"MAM's details for this torrent can't be read (unrecognized {exception.FieldName}: '{exception.Value}'); "
            + $"applied on_invalid_torrent: {QBittorrentActions.Describe(setting)}.",
            cancellationToken);
    }

    private const string NoAuthorError = "Torrent has no author; cannot be linked.";

    private const string NoWantedFormat = "None of the torrent's files has a format listed for its media type.";

    private const string SeriesLayoutEbookOnly = "This library's series layout links only ebook formats, and the torrent has none.";

    /// <summary>Where a torrent's files were placed, what was recorded for each, and what happened to it.</summary>
    private sealed record Placement(string Folder, List<LinkedFileRecord> LinkedFiles, List<LinkOutcome> Outcomes, List<string> PlacedUnderOtherName);

    /// <summary>
    /// The best audio and the best ebook format among the torrent's files, by the library's or the global lists, or why
    /// there is none to link. A series library links only the ebook side: comics and manga are what it is for, and a
    /// series folder of audio files from many torrents is of no use.
    /// </summary>
    private (string? Audio, string? Ebook, string? Error) ChooseFormats(Library library, TorrentMetadata metadata, IReadOnlyList<QBittorrentFile> files)
    {
        var (audio, ebook) = LinkedFormats.Choose(
            library.AudioTypes ?? formatPreferenceLists.AudioSideFor(metadata.MediaType),
            library.EbookTypes ?? formatPreferenceLists.EbookTypes,
            files.Select(f => f.Name));
        if (library.IsSeriesLayout)
        {
            return ebook is not null ? (null, ebook, null) : (null, null, audio is null ? NoWantedFormat : SeriesLayoutEbookOnly);
        }

        return (audio, ebook, audio is null && ebook is null ? NoWantedFormat : null);
    }

    private static List<QBittorrentFile> ChosenFiles(IReadOnlyList<QBittorrentFile> files, string? audio, string? ebook) =>
        files.Where(f => LinkedFormats.IsChosen(f.Name, audio, ebook)).ToList();

    /// <summary>
    /// The torrent's save path as seen from here, and the libraries its category, save path and tags give it, in the
    /// order of the configuration. Which of them takes the book depends on its genres (<see cref="ChooseLibrary"/>).
    /// </summary>
    private (string MappedSavePath, IReadOnlyList<int> Candidates) MatchLibrary(QBittorrentConnection connection, QBittorrentTorrent torrent)
    {
        var mappedSavePath = PathMapper.Translate(connection.PathMapping, torrent.SavePath);
        return (mappedSavePath, LibraryMatcher.CandidateIndexes(MatchRules, torrent.Category, mappedSavePath, torrent.TagList));
    }

    private IReadOnlyList<LibraryMatchRule> MatchRules => _matchRules ??= libraries.Select(ToMatchRule).ToList();

    private IReadOnlyList<LibraryMatchRule>? _matchRules;

    /// <summary>The first of <paramref name="candidates"/> whose genre condition takes the book; null when none does.</summary>
    private int? ChooseLibrary(IReadOnlyList<int> candidates, TorrentMetadata metadata) =>
        LibraryMatcher.ChooseForBook(MatchRules, candidates, metadata);

    /// <summary>Why no library takes a book its category gives libraries: its main category and genres, by MAM's names.</summary>
    public static string NoLibraryForGenres(TorrentMetadata metadata)
    {
        var fiction = metadata.FictionCategory is { } main ? GenreLabels.FictionLabel(main) : "neither fiction nor nonfiction";
        var genres = metadata.GenreCategories is { Count: > 0 } list
            ? string.Join(", ", list.Order().Select(GenreLabels.Label))
            : "no genres";
        return $"No library takes its genres: {fiction}; {genres}.";
    }

    /// <summary>
    /// A book whose category gives it libraries, none of which takes its genres: not linked, said on the Errors page and
    /// as the record's "no library" problem. A torrent seen for the first time gets its record here, so the next runs
    /// read its metadata from it instead of asking MAM again; a change to the configuration or the metadata links it.
    /// </summary>
    private async Task RecordNoLibraryForGenresAsync(
        TorrentEntity? existing, QBittorrentTorrent torrent, TorrentMetadata metadata, CancellationToken cancellationToken)
    {
        if (existing is null)
        {
            existing = new TorrentEntity
            {
                Id = torrent.Hash,
                MamId = metadata.MamId,
                NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
                Title = metadata.Title,
                MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
                CreatedAt = DateTimeOffset.UtcNow,
                QBittorrentCategory = torrent.Category,
            };
            ClientStatuses.Update(existing, torrent.State);
            dbContext.Torrents.Add(existing);
        }

        existing.LibraryMismatchState = nameof(LibraryMismatchState.NoLibrary);
        await RecordLinkerErrorAsync(torrent, NoLibraryForGenres(metadata), cancellationToken);
    }

    /// <summary>
    /// The folder of a book in a book-layout library, by `folder_template`, ending with the book's language when
    /// `language_in_folder` asks for it: with <c>"other"</c> for a book not in the first of `languages`, and always when the
    /// plain folder already holds a linked book in another language, so two editions of a title don't share a folder.
    /// The edition linked there first keeps the plain folder. Null for a series library, whose books share the series'.
    /// </summary>
    /// <param name="directoryExists">The narrator fallback's check, as for <see cref="FolderNameComputer.Compute"/>.</param>
    private async Task<string?> BookFolderAsync(
        Library library, TorrentMetadata metadata, string torrentId, Func<string, bool> directoryExists, CancellationToken cancellationToken)
    {
        if (library.IsSeriesLayout)
        {
            return null;
        }

        string Compute(bool markLanguage) => FolderNameComputer.Compute(
            library.LibraryDir, metadata, options.ExcludeNarratorInLibraryDir, directoryExists, library.TemplateLayout, library.FolderNames, markLanguage);

        if (MarksLanguage(library, metadata))
        {
            return Compute(markLanguage: true);
        }

        var plain = Compute(markLanguage: false);
        var language = metadata.Language.ToString();
        var clash = await dbContext.Torrents.AnyAsync(
            t => t.LibraryPath == plain && t.Id != torrentId && t.Language != null && t.Language != language, cancellationToken);
        return clash ? Compute(markLanguage: true) : plain;
    }

    /// <summary>Whether `language_in_folder = "other"` marks the book: it isn't in the first of the top-level `languages`.</summary>
    private bool MarksLanguage(Library library, TorrentMetadata metadata) =>
        library.LanguageInFolder == LanguageInFolder.Other && options.FirstLanguage is { } first && metadata.Language != first;

    /// <summary>
    /// Links the chosen files into the book's folder with the library's method and writes the metadata files the library
    /// asks for. In a series library the folder is the series', shared by its books (<see cref="PlaceInSeriesFolder"/>).
    /// </summary>
    /// <param name="bookFolder">The book's folder in a book-layout library (<see cref="BookFolderAsync"/>).</param>
    private Placement PlaceFiles(
        Library library, TorrentMetadata metadata, IReadOnlyList<QBittorrentFile> files, string mappedSavePath, string? audio, string? ebook,
        string? bookFolder)
    {
        if (library.IsSeriesLayout)
        {
            return PlaceInSeriesFolder(library, metadata, ChosenFiles(files, audio, ebook), mappedSavePath, MarksLanguage(library, metadata));
        }

        var destinationFolder = bookFolder!;
        var placement = new Placement(destinationFolder, [], [], []);
        if (library.Method != LibraryLinkMethod.NoLink)
        {
            var chosen = ChosenFiles(files, audio, ebook);
            LinkChosen(placement, chosen, LinkFilePlacer.DestinationPaths(destinationFolder, chosen.Select(f => f.Name).ToList()), mappedSavePath, library.Method);

            if (library.MetadataJson)
            {
                File.WriteAllText(Path.Combine(destinationFolder, "metadata.json"), LibraryMetadataJsonBuilder.Build(metadata, options.MamGenreNames));
            }

            if (library.MetadataOpf)
            {
                File.WriteAllText(Path.Combine(destinationFolder, "metadata.opf"), LibraryMetadataOpfBuilder.Build(metadata, options.MamGenreNames));
            }
        }

        return placement;
    }

    /// <summary>
    /// Places a book in a series library: a single chosen file as <c>Series #001 - Title.ext</c> in the series folder,
    /// several (a pack, or a volume split into chapters) under their own names there, as their order can't be told. A
    /// different file already at the name, another book's, makes this one <c>name (2).ext</c>. With `series_json`, the
    /// series folder gets a `series.json`; the one-shots folder gets none. With <paramref name="markLanguage"/>, the file
    /// name ends with the book's language.
    /// </summary>
    private static Placement PlaceInSeriesFolder(
        Library library, TorrentMetadata metadata, List<QBittorrentFile> chosen, string mappedSavePath, bool markLanguage)
    {
        var extension = chosen is [var only] ? Path.GetExtension(only.Name) : string.Empty;
        var place = SeriesLayoutNamer.Compute(library.LibraryDir, library.OneshotsDir, metadata, library.FolderNames, extension, markLanguage);
        var placement = new Placement(place.Folder, [], [], []);
        if (library.Method == LibraryLinkMethod.NoLink)
        {
            return placement;
        }

        var destinations = chosen.Count == 1
            ? [Path.Combine(place.Folder, place.FileBaseName + extension)]
            : LinkFilePlacer.DestinationPaths(place.Folder, chosen.Select(f => f.Name).ToList());
        LinkChosen(placement, chosen, destinations, mappedSavePath, library.Method);
        if (library.SeriesJson && !place.IsOneShot)
        {
            File.WriteAllText(Path.Combine(place.Folder, "series.json"), MylarSeriesJsonBuilder.Build(FolderNameComputer.SelectSeries(metadata.Series)!.Name));
        }

        return placement;
    }

    /// <summary>Links each chosen file to its destination, recording on <paramref name="placement"/> what happened and the name it got.</summary>
    private static void LinkChosen(
        Placement placement, List<QBittorrentFile> chosen, IReadOnlyList<string> destinations, string mappedSavePath, LibraryLinkMethod method)
    {
        for (var i = 0; i < chosen.Count; i++)
        {
            var result = FileLinker.Link(Path.Combine(mappedSavePath, chosen[i].Name), destinations[i], method);
            placement.Outcomes.Add(result.Outcome);
            if (result.PlacedAt != destinations[i])
            {
                placement.PlacedUnderOtherName.Add(Path.GetFileName(result.PlacedAt));
            }

            // The path actually used, so Unlink, Clean and OPDS find the file under the name it got.
            placement.LinkedFiles.Add(new LinkedFileRecord(Path.GetRelativePath(placement.Folder, result.PlacedAt), chosen[i].Size));
        }
    }

    private static void RecordPlacement(
        TorrentEntity existing, QBittorrentTorrent torrent, Library library, Placement placement, string? audio, string? ebook)
    {
        existing.LibraryPath = placement.Folder;
        existing.LinkedFilesJson = JsonSerializer.Serialize(placement.LinkedFiles);
        existing.ChosenAudioExtension = audio;
        existing.ChosenEbookExtension = ebook;
        existing.QBittorrentCategory = torrent.Category;
        existing.LinkerName = library.Name;
        ClientStatuses.Update(existing, torrent.State);
        existing.LibraryMismatchState = null;
        existing.CoverCheckedAt = null; // a new folder, or the same one emptied: its cover is looked at again
    }

    private Task AppendLinkedEventAsync(TorrentEntity existing, Library library, string folder, CancellationToken cancellationToken) =>
        eventStore.AppendAsync(new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            TorrentId = existing.Id,
            MamId = existing.MamId,
            Kind = EventKind.Linked,
            LinkerName = library.Name,
            LibraryPath = folder,
        }, cancellationToken);

    private static string DescribePlacement(string torrentName, Placement placement)
    {
        if (placement.Outcomes.Count == 0)
        {
            return $"Recorded {torrentName} for {placement.Folder} without placing any files (no_link)";
        }

        var (action, detail) = LinkOutcomeDescription.Describe(placement.Outcomes, placement.PlacedUnderOtherName);
        return $"{action} {torrentName} into {placement.Folder}{detail}";
    }

    private async Task SyncMismatchAsync(
        TorrentEntity existing, QBittorrentConnection connection, QBittorrentTorrent torrent, TorrentMetadata metadata, CancellationToken cancellationToken)
    {
        var matchedIndex = ChooseLibrary(MatchLibrary(connection, torrent).Candidates, metadata);

        // The library it is linked into, from where its folder is: the library its category points to now can be
        // another one, which the mismatch below reports. This also repairs names an earlier version set that way.
        if (LibraryContaining(existing.LibraryPath!) is { } linkedInto)
        {
            existing.LinkerName = linkedInto.Name;
        }

        string? recomputedLibraryDir = null;
        string? recomputedFullPath = null;
        string? expectedFileName = null;
        var linkedFiles = DeserializeLinkedFiles(existing.LinkedFilesJson);
        if (matchedIndex is not null && libraries[matchedIndex.Value] is { IsSeriesLayout: true } seriesLibrary)
        {
            // A series folder holds many books, so a book's own place is its file: a single file's name is compared too.
            recomputedLibraryDir = seriesLibrary.LibraryDir;
            var extension = linkedFiles is [var only] ? Path.GetExtension(only.RelativePath) : string.Empty;
            var place = SeriesLayoutNamer.Compute(
                seriesLibrary.LibraryDir, seriesLibrary.OneshotsDir, metadata, seriesLibrary.FolderNames, extension, MarksLanguage(seriesLibrary, metadata));
            recomputedFullPath = place.Folder;
            expectedFileName = linkedFiles.Count == 1 ? place.FileBaseName + extension : null;
        }
        else if (matchedIndex is not null)
        {
            var library = libraries[matchedIndex.Value];
            recomputedLibraryDir = library.LibraryDir;
            // The book's own folder is no collision: a book linked without its narrator would otherwise be told to move.
            var linkedPath = existing.LibraryPath!;
            recomputedFullPath = await BookFolderAsync(
                library, metadata, existing.Id, path => !string.Equals(path, linkedPath, StringComparison.Ordinal) && Directory.Exists(path),
                cancellationToken);
        }

        var state = LibraryMismatchCalculator.Compute(existing.LibraryPath!, recomputedLibraryDir, recomputedFullPath);
        if (state == LibraryMismatchState.None && expectedFileName is not null && !IsNamed(linkedFiles[0].RelativePath, expectedFileName))
        {
            state = LibraryMismatchState.NewPath;
        }

        existing.LibraryMismatchState = state == LibraryMismatchState.None ? null : state.ToString();
    }

    /// <summary>Whether a file is at <paramref name="expected"/>, or at the <c>name (2).ext</c> it got for another book's file there.</summary>
    private static bool IsNamed(string relativePath, string expected)
    {
        if (string.Equals(relativePath, expected, StringComparison.Ordinal))
        {
            return true;
        }

        var stem = Path.GetFileNameWithoutExtension(expected);
        var extension = Path.GetExtension(expected);
        return relativePath.StartsWith(stem + " (", StringComparison.Ordinal) && relativePath.EndsWith(")" + extension, StringComparison.Ordinal)
            && int.TryParse(relativePath[(stem.Length + 2)..^(extension.Length + 1)], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    /// <summary>The library whose folder holds <paramref name="libraryPath"/>, the deepest one if folders nest.</summary>
    private Library? LibraryContaining(string libraryPath) =>
        LibraryFolders.IndexContaining(libraries, libraryPath) is { } index ? libraries[index] : null;

    /// <summary>
    /// Re-runs the metadata lookup for a tracked torrent, storing changed fields as an Updated event and pushing them to
    /// Audiobookshelf.
    /// </summary>
    /// <remarks>A book of a split collection refreshes its collection, the torrent MAM knows; Relink re-derives the books.</remarks>
    public async Task<RefreshOutcome> RefreshAsync(string torrentId, CancellationToken cancellationToken = default)
    {
        torrentId = CollectionBookId.HashOf(torrentId);
        var existing = await dbContext.Torrents.FirstOrDefaultAsync(t => t.Id == torrentId, cancellationToken)
            ?? throw new InvalidOperationException($"Torrent '{torrentId}' is not tracked.");

        var looked = await metadataLookup.TryLookupByInfoHashAsync(torrentId, cancellationToken);
        if (looked is null)
        {
            return RefreshOutcome.NotFoundOnMam;
        }

        var changed = await ApplyRefreshedMetadataAsync(existing, looked, cancellationToken);
        existing.CoverCheckedAt = null; // MAM's cover may have changed or come since: the next Link run tries again
        if (changed && absClient is not null && existing.AbsId is not null)
        {
            await absClient.UpdateItemMetadataAsync(existing.AbsId, looked, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return changed ? RefreshOutcome.Changed : RefreshOutcome.Unchanged;
    }

    /// <summary>
    /// Refreshes a torrent's metadata from MAM and links it into its library again, as <see cref="RelinkAsync"/> does.
    /// </summary>
    public Task<RelinkResult> RefreshAndRelinkAsync(
        string torrentId,
        IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)> connections,
        CancellationToken cancellationToken = default) =>
        RelinkCoreAsync(torrentId, connections, refreshFromMam: true, cancellationToken);

    /// <summary>
    /// Links a torrent into its library again by its stored metadata, from the files qBittorrent has: the library
    /// comes from the torrent's current category, save path and tags, the files Fetchshelf placed before are removed,
    /// and the wanted files are placed in the folder the metadata calls for. Everything that could stop it is checked
    /// before any file is touched, so a refusal leaves the library as it was. A replaced or unlinked torrent stops being
    /// marked so; a replaced one is then weighed against the other copies again by the Clean job.
    /// </summary>
    public Task<RelinkResult> RelinkAsync(
        string torrentId,
        IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)> connections,
        CancellationToken cancellationToken = default) =>
        RelinkCoreAsync(torrentId, connections, refreshFromMam: false, cancellationToken);

    private async Task<RelinkResult> RelinkCoreAsync(
        string torrentId,
        IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)> connections,
        bool refreshFromMam,
        CancellationToken cancellationToken)
    {
        // A book of a split collection is relinked with its collection, the torrent qBittorrent has.
        torrentId = CollectionBookId.HashOf(torrentId);
        var existing = await dbContext.Torrents.FirstOrDefaultAsync(t => t.Id == torrentId, cancellationToken)
            ?? throw new InvalidOperationException($"Torrent '{torrentId}' is not tracked.");

        var found = await FindInQBittorrentAsync(torrentId, connections, cancellationToken);
        if (found is not { } inClient)
        {
            return Refuse(existing, "It is not in qBittorrent, so there is nothing to link from.");
        }

        var (connection, client, torrent) = inClient;
        if (!torrent.IsFinished)
        {
            return Refuse(existing, "It has not finished downloading.");
        }

        var (mappedSavePath, candidates) = MatchLibrary(connection, torrent);
        if (candidates.Count == 0)
        {
            return Refuse(existing, "No library accepts its qBittorrent category, save path and tags.");
        }

        // Metadata edited by hand is what the book is linked by; only its VIP status follows MAM.
        var stored = TorrentMetadataSerializer.Deserialize(existing.MetadataJson);
        var metadata = stored;
        if (refreshFromMam)
        {
            var looked = await metadataLookup.TryLookupByInfoHashAsync(torrentId, cancellationToken);
            if (looked is null && !HandEdits.AreIn(stored))
            {
                return Refuse(existing, NotOnMamError);
            }

            metadata = looked is null ? stored : HandEdits.KeepOver(stored, looked);
        }

        // By the metadata it is linked with, refreshed or not: its genres pick among the libraries.
        if (ChooseLibrary(candidates, metadata) is not { } matchedIndex)
        {
            return Refuse(existing, NoLibraryForGenres(metadata));
        }

        var library = libraries[matchedIndex];
        if (metadata.Authors.Count == 0 && !library.IsSeriesLayout)
        {
            return Refuse(existing, "It has no author.");
        }

        var files = await client.ListFilesAsync(torrentId, cancellationToken);
        var books = SplitCollection(library, metadata, files, mappedSavePath, existing.SplitCollection);
        if (books is not null)
        {
            return await RelinkAsBooksAsync(existing, torrent, library, metadata, books, files, mappedSavePath, cancellationToken);
        }

        var (audio, ebook, formatError) = ChooseFormats(library, metadata, files);
        if (formatError is not null)
        {
            return Refuse(existing, formatError);
        }

        if (library.Method != LibraryLinkMethod.NoLink)
        {
            var missing = ChosenFiles(files, audio, ebook).Count(f => !File.Exists(Path.Combine(mappedSavePath, f.Name)));
            if (missing > 0)
            {
                return Refuse(existing, $"{missing} of its files are not in {mappedSavePath}; check path_mapping.");
            }
        }

        await ApplyRefreshedMetadataAsync(existing, metadata, cancellationToken);
        await RemoveBooksAsync(existing, cancellationToken); // linked as its books before, and now as one

        var oldPath = existing.LibraryPath;
        var oldPathUse = oldPath is null ? null : await LibraryFileRemover.FolderUseByOthersAsync(dbContext, oldPath, existing.Id, cancellationToken);
        Placement placement;
        try
        {
            if (oldPath is not null)
            {
                LibraryFileRemover.Remove(
                    oldPath, DeserializeLinkedFiles(existing.LinkedFilesJson), oldPathUse!.Shared, oldPathUse.Files,
                    LibraryContaining(oldPath)?.LibraryDir);
            }

            // After the old files went: a folder the book itself left is no narrator collision.
            var bookFolder = await BookFolderAsync(library, metadata, existing.Id, Directory.Exists, cancellationToken);
            placement = PlaceFiles(library, metadata, files, mappedSavePath, audio, ebook, bookFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError("Could not relink {Title}: {Error}", existing.Title, ex.Message);
            await ErrorLog.RecordAsync(dbContext, ErrorStage.Linker, torrent.Hash, torrent.Name, ex.Message, cancellationToken: cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new RelinkResult(false, $"Relinking failed part-way: {ex.Message} Fix the cause and relink again.");
        }

        RecordPlacement(existing, torrent, library, placement, audio, ebook);
        existing.ReplacedWithTorrentId = null;
        existing.ReplacedAt = null;
        existing.UnlinkedAt = null;
        await UpdateAudiobookshelfAfterRelinkAsync(existing, metadata, folderChanged: oldPath != placement.Folder, cancellationToken);
        await AppendLinkedEventAsync(existing, library, placement.Folder, cancellationToken);
        await ErrorLog.ClearAsync(dbContext, ErrorStage.Linker, torrentId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var description = DescribePlacement(torrent.Name, placement);
        _logger.LogInformation("Relink: {Placement}", description);
        return new RelinkResult(true, description + ".");
    }

    /// <summary>
    /// What would stop <see cref="RelinkAsync"/> placing the torrent's files again, found the way it finds it, without
    /// changing anything: whether qBittorrent has it and has finished it, whether a library accepts it, and whether it
    /// has the author a book-layout library needs. With the torrent's category, for a library to be given, or for
    /// <see cref="RelinkBlocker.NoLibraryTakesGenres"/> why no library takes its genres.
    /// </summary>
    public async Task<(RelinkBlocker Blocker, string? Detail)> RelinkBlockerAsync(
        string torrentId, IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)> connections,
        CancellationToken cancellationToken = default)
    {
        if (await FindInQBittorrentAsync(CollectionBookId.HashOf(torrentId), connections, cancellationToken) is not var (connection, _, torrent))
        {
            return (RelinkBlocker.NotInQBittorrent, null);
        }

        if (!torrent.IsFinished)
        {
            return (RelinkBlocker.NotFinished, torrent.Category);
        }

        var candidates = MatchLibrary(connection, torrent).Candidates;
        if (candidates.Count == 0)
        {
            return (RelinkBlocker.NoLibraryAccepts, torrent.Category);
        }

        var metadataJson = await dbContext.Torrents.AsNoTracking().Where(t => t.Id == torrentId).Select(t => t.MetadataJson).SingleAsync(cancellationToken);
        var metadata = TorrentMetadataSerializer.Deserialize(metadataJson);
        if (ChooseLibrary(candidates, metadata) is not { } index)
        {
            return (RelinkBlocker.NoLibraryTakesGenres, NoLibraryForGenres(metadata));
        }

        return metadata.Authors.Count == 0 && !libraries[index].IsSeriesLayout
            ? (RelinkBlocker.NoAuthor, torrent.Category)
            : (RelinkBlocker.None, torrent.Category);
    }

    /// <summary>
    /// Relinks a collection as its books: the files of its earlier placement, as one book or as its books, are removed,
    /// and the books placed again (<see cref="LinkBooksAsync"/>). Refused, changing nothing, while any book's files are
    /// missing from the download folder.
    /// </summary>
    private async Task<RelinkResult> RelinkAsBooksAsync(
        TorrentEntity collection, QBittorrentTorrent torrent, Library library, TorrentMetadata metadata, IReadOnlyList<CollectionBook> books,
        IReadOnlyList<QBittorrentFile> files, string mappedSavePath, CancellationToken cancellationToken)
    {
        if (library.Method != LibraryLinkMethod.NoLink
            && books.SelectMany(b => b.Files).Count(f => !File.Exists(Path.Combine(mappedSavePath, f))) is var missing and > 0)
        {
            return Refuse(collection, $"{missing} of its files are not in {mappedSavePath}; check path_mapping.");
        }

        await ApplyRefreshedMetadataAsync(collection, metadata, cancellationToken);
        try
        {
            if (collection.LibraryPath is { } oldPath)
            {
                var use = await LibraryFileRemover.FolderUseByOthersAsync(dbContext, oldPath, collection.Id, cancellationToken);
                LibraryFileRemover.Remove(oldPath, DeserializeLinkedFiles(collection.LinkedFilesJson), use.Shared, use.Files, LibraryContaining(oldPath)?.LibraryDir);
            }

            foreach (var book in await dbContext.Torrents.Where(t => t.CollectionTorrentId == collection.Id && t.LibraryPath != null).ToListAsync(cancellationToken))
            {
                await RemoveFilesAsync(book, cancellationToken);
            }

            await LinkBooksAsync(collection, torrent, library, metadata, books, files, mappedSavePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError("Could not relink {Title}: {Error}", collection.Title, ex.Message);
            await ErrorLog.RecordAsync(dbContext, ErrorStage.Linker, torrent.Hash, torrent.Name, ex.Message, cancellationToken: cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new RelinkResult(false, $"Relinking failed part-way: {ex.Message} Fix the cause and relink again.");
        }

        return new RelinkResult(true, $"Linked {collection.Title} as {books.Count} books.");
    }

    /// <summary>
    /// A collection linked as its books before, now relinked as one: the books' files and records go, as the collection's
    /// own record takes over.
    /// </summary>
    private async Task RemoveBooksAsync(TorrentEntity collection, CancellationToken cancellationToken)
    {
        if (collection.CollectionBookCount is not > 0)
        {
            return;
        }

        foreach (var book in await dbContext.Torrents.Where(t => t.CollectionTorrentId == collection.Id).ToListAsync(cancellationToken))
        {
            if (book.LibraryPath is not null)
            {
                await RemoveFilesAsync(book, cancellationToken);
            }

            dbContext.Torrents.Remove(book);
        }

        collection.CollectionBookCount = null;
    }

    /// <summary>Removes a linked record's files from its folder, keeping what another record there still uses.</summary>
    private async Task RemoveFilesAsync(TorrentEntity record, CancellationToken cancellationToken)
    {
        var path = record.LibraryPath!;
        var use = await LibraryFileRemover.FolderUseByOthersAsync(dbContext, path, record.Id, cancellationToken);
        LibraryFileRemover.Remove(path, DeserializeLinkedFiles(record.LinkedFilesJson), use.Shared, use.Files, LibraryContaining(path)?.LibraryDir);
    }

    private RelinkResult Refuse(TorrentEntity existing, string reason)
    {
        _logger.LogWarning("Did not relink {Title}: {Reason} Nothing was changed", existing.Title, reason);
        return new RelinkResult(false, $"Not relinked: {reason} Nothing was changed.");
    }

    private static async Task<(QBittorrentConnection Connection, QBittorrentClient Client, QBittorrentTorrent Torrent)?> FindInQBittorrentAsync(
        string hash, IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)> connections, CancellationToken cancellationToken)
    {
        foreach (var (connection, client) in connections)
        {
            if (await client.GetTorrentAsync(hash, cancellationToken) is { } torrent)
            {
                return (connection, client, torrent);
            }
        }

        return null;
    }

    /// <summary>Stores <paramref name="looked"/> as the torrent's metadata, recording changed fields as an Updated event.</summary>
    private async Task<bool> ApplyRefreshedMetadataAsync(TorrentEntity existing, TorrentMetadata looked, CancellationToken cancellationToken)
    {
        var changes = MetadataDiff.Between(TorrentMetadataSerializer.Deserialize(existing.MetadataJson), looked);
        existing.MetadataJson = TorrentMetadataSerializer.Serialize(looked);
        existing.Title = looked.Title;
        existing.NormalizedTitleKey = TitleNormalizer.Normalize(looked.Title);
        if (changes.Count == 0)
        {
            return false;
        }

        await eventStore.AppendAsync(new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            TorrentId = existing.Id,
            MamId = existing.MamId,
            Kind = EventKind.Updated,
            FieldChangesJson = JsonSerializer.Serialize(changes),
        }, cancellationToken);
        return true;
    }

    /// <summary>
    /// In the same folder, the Audiobookshelf item gets the refreshed metadata. In a new folder Audiobookshelf sees a new
    /// item, so the old one is deleted and the stored id cleared for the matcher to find the new one.
    /// </summary>
    private async Task UpdateAudiobookshelfAfterRelinkAsync(
        TorrentEntity existing, TorrentMetadata metadata, bool folderChanged, CancellationToken cancellationToken)
    {
        if (absClient is null || existing.AbsId is null)
        {
            return;
        }

        if (!folderChanged)
        {
            await absClient.UpdateItemMetadataAsync(existing.AbsId, metadata, cancellationToken);
            return;
        }

        try
        {
            await absClient.DeleteItemAsync(existing.AbsId, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Could not delete the old Audiobookshelf item {AbsId} of {Title}: {Error}", existing.AbsId, existing.Title, ex.Message);
        }

        existing.AbsId = null;
    }

    private static List<LinkedFileRecord> DeserializeLinkedFiles(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<List<LinkedFileRecord>>(json) ?? [];

    /// <summary>
    /// Applies a manual metadata override, creating the Torrent record if it does not yet exist, and records what changed
    /// as an Updated event edited by hand.
    /// </summary>
    /// <param name="torrentId">The torrent's info hash.</param>
    /// <param name="metadata">The details as edited.</param>
    /// <param name="before">
    /// The details the edit started from, for a torrent without a record yet, such as MAM's from the Link error; a
    /// recorded torrent is compared with its stored details.
    /// </param>
    /// <param name="cancellationToken">Cancels the save.</param>
    public async Task SetManualMetadataAsync(
        string torrentId, TorrentMetadata metadata, TorrentMetadata? before = null, CancellationToken cancellationToken = default)
    {
        var manualMetadata = metadata with { MetadataSource = MetadataSource.Manual };
        var existing = await dbContext.Torrents.FirstOrDefaultAsync(t => t.Id == torrentId, cancellationToken);

        // What the edit changed, against the details stored before (or MAM's, for a torrent without a record yet).
        var changes = MetadataDiff.Between(
            existing is null ? before ?? manualMetadata : TorrentMetadataSerializer.Deserialize(existing.MetadataJson), manualMetadata);

        if (existing is null)
        {
            existing = new TorrentEntity
            {
                Id = torrentId,
                MamId = manualMetadata.MamId,
                NormalizedTitleKey = TitleNormalizer.Normalize(manualMetadata.Title),
                Title = manualMetadata.Title,
                MetadataJson = TorrentMetadataSerializer.Serialize(manualMetadata),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            dbContext.Torrents.Add(existing);
        }
        else
        {
            existing.Title = manualMetadata.Title;
            existing.NormalizedTitleKey = TitleNormalizer.Normalize(manualMetadata.Title);
            existing.MetadataJson = TorrentMetadataSerializer.Serialize(manualMetadata);
        }

        if (changes.Count > 0)
        {
            await eventStore.AppendAsync(new EventEntity
            {
                OccurredAt = DateTimeOffset.UtcNow,
                TorrentId = existing.Id,
                MamId = existing.MamId,
                Kind = EventKind.Updated,
                FieldChangesJson = JsonSerializer.Serialize(changes),
                EditedByHand = true,
            }, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static LibraryMatchRule ToMatchRule(Library library) => new()
    {
        Category = library.Category,
        DownloadDir = library.DownloadDir,
        AllowTags = library.AllowTags,
        DenyTags = library.DenyTags,
        Genres = library.Genres,
    };

    /// <param name="lookedUpMetadataJson">The torrent's metadata from MAM, kept on the error for a torrent without a record.</param>
    private async Task RecordLinkerErrorAsync(
        QBittorrentTorrent torrent, string errorText, CancellationToken cancellationToken, string? lookedUpMetadataJson = null)
    {
        _failed++;
        _logger.LogWarning("Could not link {Torrent}: {Error}", torrent.Name, errorText);
        await ErrorLog.RecordAsync(dbContext, ErrorStage.Linker, torrent.Hash, torrent.Name, errorText, lookedUpMetadataJson, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
