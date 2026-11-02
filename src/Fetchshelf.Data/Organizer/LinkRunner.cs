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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Organizer;

/// <summary>What one link run did: torrents newly linked, and torrents that could not be linked.</summary>
public sealed record LinkRunSummary(int Linked, int Failed);

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

        foreach (var torrent in finished)
        {
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
        }
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
        var mappedSavePath = PathMapper.Translate(connection.PathMapping, torrent.SavePath);
        var libraryRules = libraries.Select(ToMatchRule).ToList();
        var matchedIndex = LibraryMatcher.FindMatchingLibraryIndex(libraryRules, torrent.Category, mappedSavePath, torrent.TagList);

        TorrentMetadata metadata;

        if (existing is not null)
        {
            existing.QBittorrentCategory = torrent.Category;
            existing.ClientStatus = torrent.State;
            metadata = TorrentMetadataSerializer.Deserialize(existing.MetadataJson);

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
            return; // tracked but no library claims it
        }

        var library = libraries[matchedIndex.Value];
        var files = await client.ListFilesAsync(torrent.Hash, cancellationToken);
        var audioTypes = library.AudioTypes ?? formatPreferenceLists.AudioSideFor(metadata.MediaType);
        var ebookTypes = library.EbookTypes ?? formatPreferenceLists.EbookTypes;
        var bestAudioExtension = FormatRanker.SelectBestExtension(audioTypes, files.Select(f => Path.GetExtension(f.Name)));
        var bestEbookExtension = FormatRanker.SelectBestExtension(ebookTypes, files.Select(f => Path.GetExtension(f.Name)));

        if (bestAudioExtension is null && bestEbookExtension is null)
        {
            await RecordLinkerErrorAsync(torrent, "None of the torrent's files has a format listed for its media type.", cancellationToken);
            return;
        }

        var destinationFolder = FolderNameComputer.Compute(library.LibraryDir, metadata, excludeNarratorInLibraryDir, Directory.Exists);
        var chosenFiles = files.Where(f => MatchesExtension(f.Name, bestAudioExtension) || MatchesExtension(f.Name, bestEbookExtension)).ToList();

        var linkedFiles = new List<LinkedFileRecord>();
        if (library.Method != LibraryLinkMethod.NoLink)
        {
            foreach (var file in chosenFiles)
            {
                var sourcePath = Path.Combine(mappedSavePath, file.Name);
                var destinationPath = LinkFilePlacer.DestinationPath(destinationFolder, file.Name);
                FileLinker.Link(sourcePath, destinationPath, library.Method);
                linkedFiles.Add(new LinkedFileRecord(Path.GetRelativePath(destinationFolder, destinationPath), file.Size));
            }

            File.WriteAllText(Path.Combine(destinationFolder, "metadata.json"), LibraryMetadataJsonBuilder.Build(metadata));
        }

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

        existing.LibraryPath = destinationFolder;
        existing.LinkedFilesJson = JsonSerializer.Serialize(linkedFiles);
        existing.ChosenAudioExtension = bestAudioExtension;
        existing.ChosenEbookExtension = bestEbookExtension;
        existing.QBittorrentCategory = torrent.Category;
        existing.LinkerName = library.Name;
        existing.ClientStatus = torrent.State;
        existing.LibraryMismatchState = null;

        if (absClient is not null && existing.AbsId is not null)
        {
            await absClient.UpdateItemMetadataAsync(existing.AbsId, metadata, cancellationToken);
        }

        await eventStore.AppendAsync(new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            TorrentId = existing.Id,
            MamId = existing.MamId,
            Kind = EventKind.Linked,
            LinkerName = library.Name,
            LibraryPath = destinationFolder,
        }, cancellationToken);

        await ErrorLog.ClearAsync(dbContext, ErrorStage.Linker, torrent.Hash, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        _linked++;
        _logger.LogInformation("Linked {Torrent} into {LibraryPath}", torrent.Name, destinationFolder);
    }

    private void SyncMismatch(TorrentEntity existing, QBittorrentConnection connection, QBittorrentTorrent torrent, TorrentMetadata metadata)
    {
        var mappedSavePath = PathMapper.Translate(connection.PathMapping, torrent.SavePath);
        var matchedIndex = LibraryMatcher.FindMatchingLibraryIndex(
            libraries.Select(ToMatchRule).ToList(), torrent.Category, mappedSavePath, torrent.TagList);

        string? recomputedLibraryDir = null;
        string? recomputedFullPath = null;
        if (matchedIndex is not null)
        {
            var library = libraries[matchedIndex.Value];
            existing.LinkerName = library.Name;
            recomputedLibraryDir = library.LibraryDir;
            recomputedFullPath = FolderNameComputer.Compute(library.LibraryDir, metadata, excludeNarratorInLibraryDir, Directory.Exists);
        }

        var state = LibraryMismatchCalculator.Compute(existing.LibraryPath!, recomputedLibraryDir, recomputedFullPath);
        existing.LibraryMismatchState = state == LibraryMismatchState.None ? null : state.ToString();
    }

    /// <summary>Re-runs the metadata lookup for a tracked torrent, storing changed fields as an Updated event.</summary>
    public async Task<bool> RefreshAsync(string torrentId, bool relink, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.Torrents.FirstOrDefaultAsync(t => t.Id == torrentId, cancellationToken)
            ?? throw new InvalidOperationException($"Torrent '{torrentId}' is not tracked.");

        var looked = await metadataLookup.TryLookupByInfoHashAsync(torrentId, cancellationToken);
        if (looked is null)
        {
            return false;
        }

        var current = TorrentMetadataSerializer.Deserialize(existing.MetadataJson);
        var changes = DiffFields(current, looked);
        if (changes.Count == 0 && !relink)
        {
            return false;
        }

        existing.MetadataJson = TorrentMetadataSerializer.Serialize(looked);
        existing.Title = looked.Title;
        existing.NormalizedTitleKey = TitleNormalizer.Normalize(looked.Title);

        if (changes.Count > 0)
        {
            await eventStore.AppendAsync(new EventEntity
            {
                OccurredAt = DateTimeOffset.UtcNow,
                TorrentId = existing.Id,
                MamId = existing.MamId,
                Kind = EventKind.Updated,
                FieldChangesJson = JsonSerializer.Serialize(changes),
            }, cancellationToken);

            if (absClient is not null && existing.AbsId is not null)
            {
                await absClient.UpdateItemMetadataAsync(existing.AbsId, looked, cancellationToken);
            }
        }

        if (relink && existing.LibraryPath is not null)
        {
            var matchedLibrary = libraries.FirstOrDefault(l => existing.LibraryPath!.StartsWith(l.LibraryDir, StringComparison.Ordinal));
            if (matchedLibrary is not null)
            {
                var newFolder = FolderNameComputer.Compute(matchedLibrary.LibraryDir, looked, excludeNarratorInLibraryDir, Directory.Exists);
                if (!string.Equals(newFolder, existing.LibraryPath, StringComparison.Ordinal))
                {
                    if (Directory.Exists(existing.LibraryPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(newFolder)!);
                        Directory.Move(existing.LibraryPath, newFolder);
                    }

                    existing.LibraryPath = newFolder;
                }

                File.WriteAllText(Path.Combine(newFolder, "metadata.json"), LibraryMetadataJsonBuilder.Build(looked));

                if (absClient is not null && existing.AbsId is not null && changes.Count == 0)
                {
                    await absClient.UpdateItemMetadataAsync(existing.AbsId, looked, cancellationToken);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

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

    private static bool MatchesExtension(string fileName, string? extension) =>
        extension is not null && string.Equals(Path.GetExtension(fileName).TrimStart('.'), extension, StringComparison.OrdinalIgnoreCase);

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

    private static List<FieldChange> DiffFields(TorrentMetadata from, TorrentMetadata to)
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

public sealed record FieldChange(string Field, string From, string To);
