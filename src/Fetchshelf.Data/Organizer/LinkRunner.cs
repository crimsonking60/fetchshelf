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

/// <summary>What one link run did: torrents newly linked, and torrents that could not be linked.</summary>
public sealed record LinkRunSummary(int Linked, int Failed);

/// <summary>Whether Refresh and relink placed the torrent's files again, and a message saying what happened.</summary>
public sealed record RelinkResult(bool Succeeded, string Message);

/// <summary>What a Refresh found: changed details, the same details, or no torrent on MAM to refresh from.</summary>
public enum RefreshOutcome
{
    Changed,
    Unchanged,
    NotFoundOnMam,
}

/// <summary>
/// Runs the link run, linking, library mismatch detection, refresh, and manual metadata override.
/// </summary>
public sealed class LinkRunner(
    FetchshelfDbContext dbContext,
    EventStore eventStore,
    IMetadataLookup metadataLookup,
    IReadOnlyList<Library> libraries,
    FormatPreferenceLists formatPreferenceLists,
    bool excludeNarratorInLibraryDir,
    AbsClient? absClient = null,
    ILogger<LinkRunner>? logger = null)
{
    private readonly ILogger<LinkRunner> _logger = logger ?? NullLogger<LinkRunner>.Instance;
    private int _linked;
    private int _failed;

    /// <summary>The open transaction of the run's current batch of torrents, if any. See <see cref="RunCoreAsync"/>.</summary>
    private IDbContextTransaction? _batch;
    private int _batchCount;
    private const int BatchSize = 100;

    /// <summary>Processes every 100%-complete torrent reported by one configured qBittorrent connection; a failure on one torrent is recorded as a linker error and the run continues.</summary>
    public async Task<LinkRunSummary> RunAsync(QBittorrentConnection connection, QBittorrentClient client, CancellationToken cancellationToken = default)
    {
        var (linkedBefore, failedBefore) = (_linked, _failed);
        await RunCoreAsync(connection, client, cancellationToken);
        return new LinkRunSummary(_linked - linkedBefore, _failed - failedBefore);
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
                _batch ??= await dbContext.Database.BeginTransactionAsync(cancellationToken);
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

                if (++_batchCount >= BatchSize)
                {
                    await CommitBatchAsync();
                }
            }
        }
        finally
        {
            // Also when the run ends early: the torrents already processed keep their changes.
            await CommitBatchAsync();
        }
    }

    /// <summary>Commits the current batch, if one is open; later saves commit on their own until the next batch starts.</summary>
    private async Task CommitBatchAsync()
    {
        if (_batch is null)
        {
            return;
        }

        await _batch.CommitAsync(CancellationToken.None);
        await _batch.DisposeAsync();
        _batch = null;
        _batchCount = 0;
    }

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
        var (mappedSavePath, matchedIndex) = MatchLibrary(connection, torrent);

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

            if (existing.LibraryPath is not null)
            {
                // Already linked: sync and recompute mismatch; do not touch files or metadata here.
                SyncMismatch(existing, connection, torrent, metadata);
                await ErrorLog.ClearAsync(dbContext, ErrorStage.Linker, torrent.Hash, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            // Tracked (metadata assigned, e.g. manually) but not yet linked - fall through and link now.
        }
        else
        {
            if (matchedIndex is null)
            {
                return; // no library matches; ignored
            }

            // A lookup can wait on MAM or on another MAM job: the batch must not hold the write lock meanwhile.
            await CommitBatchAsync();
            var looked = await metadataLookup.TryLookupByInfoHashAsync(torrent.Hash, cancellationToken);
            if (looked is null)
            {
                await RecordLinkerErrorAsync(torrent, "MAM has no torrent with this info-hash.", cancellationToken);
                return;
            }

            metadata = looked;
        }

        if (metadata.Authors.Count == 0)
        {
            await RecordLinkerErrorAsync(torrent, "Torrent has no author; cannot be linked.", cancellationToken);
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
        var (bestAudioExtension, bestEbookExtension) = ChooseFormats(library, metadata, files);
        if (bestAudioExtension is null && bestEbookExtension is null)
        {
            await RecordLinkerErrorAsync(torrent, NoWantedFormat, cancellationToken);
            return;
        }

        var placement = PlaceFiles(library, metadata, files, mappedSavePath, bestAudioExtension, bestEbookExtension);

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

    private const string NoWantedFormat = "None of the torrent's files has a format listed for its media type.";

    /// <summary>Where a torrent's files were placed, what was recorded for each, and what happened to it.</summary>
    private sealed record Placement(string Folder, List<LinkedFileRecord> LinkedFiles, List<LinkOutcome> Outcomes);

    /// <summary>The best audio and the best ebook format among the torrent's files, by the library's or the global lists.</summary>
    private (string? Audio, string? Ebook) ChooseFormats(Library library, TorrentMetadata metadata, IReadOnlyList<QBittorrentFile> files) =>
        LinkedFormats.Choose(
            library.AudioTypes ?? formatPreferenceLists.AudioSideFor(metadata.MediaType),
            library.EbookTypes ?? formatPreferenceLists.EbookTypes,
            files.Select(f => f.Name));

    private static List<QBittorrentFile> ChosenFiles(IReadOnlyList<QBittorrentFile> files, string? audio, string? ebook) =>
        files.Where(f => LinkedFormats.IsChosen(f.Name, audio, ebook)).ToList();

    /// <summary>The torrent's save path as seen from here, and the index of the first library that accepts it, if any.</summary>
    private (string MappedSavePath, int? LibraryIndex) MatchLibrary(QBittorrentConnection connection, QBittorrentTorrent torrent)
    {
        var mappedSavePath = PathMapper.Translate(connection.PathMapping, torrent.SavePath);
        return (mappedSavePath, LibraryMatcher.FindMatchingLibraryIndex(
            libraries.Select(ToMatchRule).ToList(), torrent.Category, mappedSavePath, torrent.TagList));
    }

    /// <summary>Links the chosen files into the book's folder with the library's method and writes its `metadata.json`.</summary>
    private Placement PlaceFiles(
        Library library, TorrentMetadata metadata, IReadOnlyList<QBittorrentFile> files, string mappedSavePath, string? audio, string? ebook)
    {
        var destinationFolder = FolderNameComputer.Compute(library.LibraryDir, metadata, excludeNarratorInLibraryDir, Directory.Exists);
        var linkedFiles = new List<LinkedFileRecord>();
        var outcomes = new List<LinkOutcome>();
        if (library.Method != LibraryLinkMethod.NoLink)
        {
            foreach (var file in ChosenFiles(files, audio, ebook))
            {
                var sourcePath = Path.Combine(mappedSavePath, file.Name);
                var destinationPath = LinkFilePlacer.DestinationPath(destinationFolder, file.Name);
                outcomes.Add(FileLinker.Link(sourcePath, destinationPath, library.Method));
                linkedFiles.Add(new LinkedFileRecord(Path.GetRelativePath(destinationFolder, destinationPath), file.Size));
            }

            File.WriteAllText(Path.Combine(destinationFolder, "metadata.json"), LibraryMetadataJsonBuilder.Build(metadata));
        }

        return new Placement(destinationFolder, linkedFiles, outcomes);
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

        var (action, detail) = LinkOutcomeDescription.Describe(placement.Outcomes);
        return $"{action} {torrentName} into {placement.Folder}{detail}";
    }

    private void SyncMismatch(TorrentEntity existing, QBittorrentConnection connection, QBittorrentTorrent torrent, TorrentMetadata metadata)
    {
        var (_, matchedIndex) = MatchLibrary(connection, torrent);

        // The library it is linked into, from where its folder is: the library its category points to now can be
        // another one, which the mismatch below reports. This also repairs names an earlier version set that way.
        if (LibraryContaining(existing.LibraryPath!) is { } linkedInto)
        {
            existing.LinkerName = linkedInto.Name;
        }

        string? recomputedLibraryDir = null;
        string? recomputedFullPath = null;
        if (matchedIndex is not null)
        {
            var library = libraries[matchedIndex.Value];
            recomputedLibraryDir = library.LibraryDir;
            recomputedFullPath = FolderNameComputer.Compute(library.LibraryDir, metadata, excludeNarratorInLibraryDir, Directory.Exists);
        }

        var state = LibraryMismatchCalculator.Compute(existing.LibraryPath!, recomputedLibraryDir, recomputedFullPath);
        existing.LibraryMismatchState = state == LibraryMismatchState.None ? null : state.ToString();
    }

    /// <summary>The library whose folder holds <paramref name="libraryPath"/>, the deepest one if folders nest.</summary>
    private Library? LibraryContaining(string libraryPath) =>
        libraries
            .Where(l => !string.IsNullOrEmpty(l.LibraryDir) && IsSameOrUnder(libraryPath, l.LibraryDir))
            .OrderByDescending(l => l.LibraryDir.Length)
            .FirstOrDefault();

    private static bool IsSameOrUnder(string path, string folder)
    {
        var trimmed = folder.TrimEnd('/', '\\');
        return path == trimmed
            || (path.StartsWith(trimmed, StringComparison.Ordinal) && path.Length > trimmed.Length && path[trimmed.Length] is '/' or '\\');
    }

    /// <summary>
    /// Re-runs the metadata lookup for a tracked torrent, storing changed fields as an Updated event and pushing them to
    /// Audiobookshelf.
    /// </summary>
    public async Task<RefreshOutcome> RefreshAsync(string torrentId, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.Torrents.FirstOrDefaultAsync(t => t.Id == torrentId, cancellationToken)
            ?? throw new InvalidOperationException($"Torrent '{torrentId}' is not tracked.");

        var looked = await metadataLookup.TryLookupByInfoHashAsync(torrentId, cancellationToken);
        if (looked is null)
        {
            return RefreshOutcome.NotFoundOnMam;
        }

        var changed = await ApplyRefreshedMetadataAsync(existing, looked, cancellationToken);
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

        var (mappedSavePath, matchedIndex) = MatchLibrary(connection, torrent);
        if (matchedIndex is null)
        {
            return Refuse(existing, "No library accepts its qBittorrent category, save path and tags.");
        }

        var library = libraries[matchedIndex.Value];

        // Metadata edited by hand is what the book is linked by; only its VIP status follows MAM.
        var stored = TorrentMetadataSerializer.Deserialize(existing.MetadataJson);
        var metadata = stored;
        if (refreshFromMam)
        {
            var looked = await metadataLookup.TryLookupByInfoHashAsync(torrentId, cancellationToken);
            if (looked is null && !HandEdits.AreIn(stored))
            {
                return Refuse(existing, "MAM has no torrent with this info-hash.");
            }

            metadata = looked is null ? stored : HandEdits.KeepOver(stored, looked);
        }

        if (metadata.Authors.Count == 0)
        {
            return Refuse(existing, "It has no author.");
        }

        var files = await client.ListFilesAsync(torrentId, cancellationToken);
        var (audio, ebook) = ChooseFormats(library, metadata, files);
        if (audio is null && ebook is null)
        {
            return Refuse(existing, NoWantedFormat);
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

        var oldPath = existing.LibraryPath;
        var oldPathShared = oldPath is not null && await LibraryFileRemover.IsFolderSharedAsync(dbContext, oldPath, existing.Id, cancellationToken);
        Placement placement;
        try
        {
            if (oldPath is not null)
            {
                LibraryFileRemover.Remove(oldPath, DeserializeLinkedFiles(existing.LinkedFilesJson), oldPathShared);
            }

            placement = PlaceFiles(library, metadata, files, mappedSavePath, audio, ebook);
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

    /// <summary>Applies a manual metadata override, creating the Torrent record if it does not yet exist.</summary>
    public async Task SetManualMetadataAsync(string torrentId, TorrentMetadata metadata, CancellationToken cancellationToken = default)
    {
        var manualMetadata = metadata with { MetadataSource = MetadataSource.Manual };
        var existing = await dbContext.Torrents.FirstOrDefaultAsync(t => t.Id == torrentId, cancellationToken);

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

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static LibraryMatchRule ToMatchRule(Library library) => new()
    {
        Category = library.Category,
        DownloadDir = library.DownloadDir,
        AllowTags = library.AllowTags,
        DenyTags = library.DenyTags,
    };

    private async Task RecordLinkerErrorAsync(QBittorrentTorrent torrent, string errorText, CancellationToken cancellationToken)
    {
        _failed++;
        _logger.LogWarning("Could not link {Torrent}: {Error}", torrent.Name, errorText);
        await ErrorLog.RecordAsync(dbContext, ErrorStage.Linker, torrent.Hash, torrent.Name, errorText, cancellationToken: cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
