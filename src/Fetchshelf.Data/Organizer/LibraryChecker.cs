using System.Text.Json;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Organizer;

/// <summary>A finding of the last Library check, with the id the page posts back.</summary>
/// <param name="Note">What happened when it was acted on and it stayed, such as why Relink couldn't; null until then.</param>
public sealed record NumberedFinding(int Id, LibraryFinding Finding, string? Note = null);

/// <summary>
/// The last Library check's findings, kept in memory: a snapshot to act on, stale as soon as anything links, and
/// forgotten on restart. A finding acted on is taken off the list.
/// </summary>
public sealed class LibraryCheckResults
{
    private readonly Lock _lock = new();
    private List<NumberedFinding> _findings = [];
    private int _nextId = 1;

    /// <summary>When the last check ended; null until one has.</summary>
    public DateTimeOffset? CheckedAt { get; private set; }

    public IReadOnlyList<NumberedFinding> Findings
    {
        get
        {
            lock (_lock)
            {
                return _findings;
            }
        }
    }

    public void Set(IEnumerable<LibraryFinding> findings, DateTimeOffset checkedAt)
    {
        lock (_lock)
        {
            _findings = findings.Select(f => new NumberedFinding(_nextId++, f)).ToList();
            CheckedAt = checkedAt;
        }
    }

    public NumberedFinding? Get(int id) => Findings.FirstOrDefault(f => f.Id == id);

    /// <summary>Puts what was learned acting on findings in their place, by id: the finding as it stands now, and a note.</summary>
    public void Update(IReadOnlyDictionary<int, (LibraryFinding Finding, string? Note)> updates)
    {
        lock (_lock)
        {
            _findings = _findings.Select(f => updates.TryGetValue(f.Id, out var update) ? f with { Finding = update.Finding, Note = update.Note } : f).ToList();
        }
    }

    public void Remove(IEnumerable<int> ids)
    {
        var gone = ids.ToHashSet();
        lock (_lock)
        {
            _findings = _findings.Where(f => !gone.Contains(f.Id)).ToList();
        }
    }
}

/// <summary>What one Library check found, by kind.</summary>
public sealed record LibraryCheckSummary(int UnknownFolders, int UnknownFiles, int FoldersMissing, int FilesMissing)
{
    public int Total => UnknownFolders + UnknownFiles + FoldersMissing + FilesMissing;
}

/// <summary>
/// The Library check (#25): walks every library's folder and compares it with the linked books, through
/// <see cref="LibraryCheckComparer"/>, keeping what doesn't match in <see cref="LibraryCheckResults"/>. It holds the
/// library write gate while it walks, so a Link or Clean run can't make a folder look unknown halfway. Hidden folders
/// and files (a name starting with <c>.</c> or <c>@</c>, such as a NAS's <c>@eaDir</c>) are left out, and folder
/// links aren't followed. Relative paths, in the configuration or on record, are taken from Fetchshelf's working folder,
/// as its file operations take them, and shown as full paths.
/// </summary>
public sealed class LibraryChecker(
    FetchshelfDbContext dbContext,
    IReadOnlyList<Library> libraries,
    LibraryCheckResults results,
    LibraryWriteGate? writeGate = null,
    TimeProvider? timeProvider = null,
    ILogger<LibraryChecker>? logger = null,
    LinkRunner? linkRunner = null)
{
    private readonly LibraryWriteGate _writeGate = writeGate ?? new LibraryWriteGate();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ILogger<LibraryChecker> _logger = logger ?? NullLogger<LibraryChecker>.Instance;

    /// <param name="connections">
    /// The qBittorrent connections, asked for each book whose folder or files are gone what would stop Relink
    /// (<see cref="LinkRunner.RelinkBlockerAsync"/>); without them, or without a <see cref="LinkRunner"/>, nobody is asked.
    /// </param>
    public async Task<LibraryCheckSummary> RunAsync(
        IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)>? connections = null, CancellationToken cancellationToken = default)
    {
        using var _ = await _writeGate.AcquireAsync(cancellationToken);
        var books = (await dbContext.Torrents.AsNoTracking()
                .Where(t => t.LibraryPath != null)
                .Select(t => new { t.Id, t.Title, t.MetadataJson, t.LibraryPath, t.LinkedFilesJson })
                .ToListAsync(cancellationToken))
            .Select(t => new KnownBook(
                t.Id, t.Title, TorrentMetadataSerializer.Deserialize(t.MetadataJson).Authors, Path.GetFullPath(t.LibraryPath!),
                (t.LinkedFilesJson is null ? [] : JsonSerializer.Deserialize<List<LinkedFileRecord>>(t.LinkedFilesJson) ?? [])
                    .Select(f => f.RelativePath).ToList()))
            .ToList();

        var folders = new List<CheckedFolder>();
        var walked = new List<string>();
        foreach (var root in libraries.Select(l => Path.GetFullPath(l.LibraryDir)).Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(root) && !walked.Any(w => PathPrefix.IsSameOrUnder(root, w)))
            {
                Walk(root, folders, cancellationToken);
                walked.Add(root);
            }
        }

        // A book outside every library's folder (its `library_dir` changed since) is checked by its own folder.
        foreach (var path in books.Select(b => b.LibraryPath).Distinct(StringComparer.Ordinal)
                     .Where(p => !walked.Any(w => PathPrefix.IsSameOrUnder(p, w)) && Directory.Exists(p)))
        {
            Walk(path, folders, cancellationToken);
        }

        var checkedLibraries = libraries
            .Select(l => new CheckedLibrary(l.Name ?? l.LibraryDir, Path.GetFullPath(l.LibraryDir), l.IsSeriesLayout, l.Method != LibraryLinkMethod.NoLink))
            .ToList();
        var findings = LibraryCheckComparer.Compare(checkedLibraries, folders, books, ReadIdentity);
        if (linkRunner is not null && connections is not null)
        {
            findings = await WithRelinkBlockersAsync(findings, connections, cancellationToken);
        }

        results.Set(findings, _time.GetUtcNow());
        var summary = new LibraryCheckSummary(
            findings.Count(f => f.Kind == LibraryFindingKind.UnknownFolder), findings.Count(f => f.Kind == LibraryFindingKind.UnknownFile),
            findings.Count(f => f.Kind == LibraryFindingKind.FolderMissing), findings.Count(f => f.Kind == LibraryFindingKind.FilesMissing));
        _logger.LogInformation(
            "{Folders} folders walked, {Books} linked books; {Findings} findings",
            folders.Count, books.Count, summary.Total);
        return summary;
    }

    /// <summary>Each book's finding with what would stop Relink; a qBittorrent that doesn't answer makes it unknown.</summary>
    private async Task<List<LibraryFinding>> WithRelinkBlockersAsync(
        List<LibraryFinding> findings, IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)> connections,
        CancellationToken cancellationToken)
    {
        var result = new List<LibraryFinding>(findings.Count);
        foreach (var finding in findings)
        {
            if (finding.TorrentId is not { } torrentId)
            {
                result.Add(finding);
                continue;
            }

            try
            {
                var (blocker, category) = await linkRunner!.RelinkBlockerAsync(torrentId, connections, cancellationToken);
                result.Add(finding with { Blocker = blocker, BlockerDetail = category });
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                result.Add(finding with { Blocker = RelinkBlocker.Unknown, BlockerDetail = ex.Message });
            }
        }

        return result;
    }

    /// <summary>Lists <paramref name="root"/> and every folder below it, each with the files directly in it.</summary>
    private static void Walk(string root, List<CheckedFolder> folders, CancellationToken cancellationToken)
    {
        var pending = new Stack<DirectoryInfo>([new DirectoryInfo(root)]);
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = new List<CheckedFile>();
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (IsHidden(entry.Name))
                {
                    continue;
                }

                if (entry is FileInfo file)
                {
                    files.Add(new CheckedFile(file.Name, file.Length, file.LastWriteTimeUtc));
                }
                else if (entry is DirectoryInfo sub && !sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    pending.Push(sub);
                }
            }

            folders.Add(new CheckedFolder(directory.FullName, files));
        }
    }

    private static bool IsHidden(string name) => name.StartsWith('.') || name.StartsWith('@');

    /// <summary>The title and authors of a folder's `metadata.json`, if it has one that can be read.</summary>
    private static BookIdentity? ReadIdentity(string folder)
    {
        try
        {
            var path = Path.Combine(folder, "metadata.json");
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                ? new BookIdentity(
                    title.GetString()!,
                    root.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array
                        ? authors.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()!).ToList()
                        : [])
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>Deletes what the Library check found unknown.</summary>
public static class LibraryCheckDeleter
{
    /// <summary>
    /// Deletes an unknown folder or file, checked again now rather than trusted from the check: it must still be inside a
    /// library's folder and not be one, and no book may point at it or inside it now, as the Link job may have used it
    /// since. Folders left empty above it are removed too, up to the library's folder. Recorded as an event.
    /// </summary>
    /// <returns>Null when deleted, else why not.</returns>
    public static async Task<string?> DeleteAsync(
        LibraryFinding finding, IReadOnlyList<Library> libraries, FetchshelfDbContext dbContext, EventStore eventStore,
        CancellationToken cancellationToken = default)
    {
        if (finding.Kind is not (LibraryFindingKind.UnknownFolder or LibraryFindingKind.UnknownFile))
        {
            return "Only unknown folders and files can be deleted.";
        }

        var path = Path.GetFullPath(finding.Path).TrimEnd('/', '\\');
        var library = libraries
            .Where(l => PathPrefix.IsSameOrUnder(path, Path.GetFullPath(l.LibraryDir)))
            .MaxBy(l => l.LibraryDir.Length);
        if (library is null || path == Path.GetFullPath(library.LibraryDir).TrimEnd('/', '\\'))
        {
            return $"{finding.Path} is not inside a library's folder.";
        }

        var linked = await dbContext.Torrents.AsNoTracking()
            .Where(t => t.LibraryPath != null)
            .Select(t => new { t.LibraryPath, t.LinkedFilesJson })
            .ToListAsync(cancellationToken);
        if (finding.Kind == LibraryFindingKind.UnknownFolder)
        {
            if (!Directory.Exists(path))
            {
                return $"{finding.Path} is gone already.";
            }

            if (linked.Any(t => PathPrefix.IsSameOrUnder(t.LibraryPath!.TrimEnd('/', '\\'), path) || PathPrefix.IsSameOrUnder(path, t.LibraryPath!)))
            {
                return $"A book is linked in {finding.Path} now; it was left alone.";
            }

            Directory.Delete(path, recursive: true);
        }
        else
        {
            if (!File.Exists(path))
            {
                return $"{finding.Path} is gone already.";
            }

            var folder = Path.GetDirectoryName(path)!;
            var listed = linked.Any(t =>
                PathPrefix.IsSameOrUnder(folder, t.LibraryPath!)
                && (t.LinkedFilesJson is null ? [] : JsonSerializer.Deserialize<List<LinkedFileRecord>>(t.LinkedFilesJson) ?? [])
                    .Any(f => Path.GetFullPath(Path.Combine(t.LibraryPath!, f.RelativePath)) == path));
            if (listed)
            {
                return $"A book lists {finding.Path} now; it was left alone.";
            }

            File.Delete(path);
        }

        LibraryFileRemover.RemoveEmptyParents(path, library.LibraryDir);
        await eventStore.AppendAsync(new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Kind = EventKind.DeletedFromLibrary,
            LinkerName = library.Name,
            LibraryPath = path,
        }, cancellationToken);
        return null;
    }
}
