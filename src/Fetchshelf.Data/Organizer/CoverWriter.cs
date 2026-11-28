using System.Diagnostics;
using System.Text.Json;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Opds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Organizer;

/// <summary>What one cover pass did: covers written from the books' own files and fetched, and books still waiting.</summary>
/// <param name="FromFiles">Covers taken out of a book's own files.</param>
/// <param name="Fetched">Covers fetched from a cover source (<see cref="ICoverSource"/>).</param>
/// <param name="Waiting">Books without a cover left for a later run, as this one reached a limit.</param>
public sealed record CoverRunSummary(int FromFiles, int Fetched, int Waiting)
{
    public int Written => FromFiles + Fetched;

    /// <summary>The fetched covers by the name of the source they came from, such as "Open Library".</summary>
    public IReadOnlyDictionary<string, int> FetchedFrom { get; init; } = new Dictionary<string, int>();
}

/// <summary>The settings the cover pass runs by.</summary>
/// <param name="MaxFetchesPerRun">
/// `covers`: the most covers one run fetches from a cover source, whose limits are requests per minute. 0 writes no covers
/// at all, from the files either.
/// </param>
/// <param name="FileBudget">
/// How long one run spends reading covers out of books' files, which costs no request: about 20 seconds by default. A time
/// rather than a count, as a read takes milliseconds on a local disk and many times that on a network share.
/// </param>
public sealed record CoverWriterOptions(int MaxFetchesPerRun, TimeSpan? FileBudget = null)
{
    public static readonly TimeSpan DefaultFileBudget = TimeSpan.FromSeconds(20);

    public TimeSpan EffectiveFileBudget => FileBudget ?? DefaultFileBudget;
}

/// <summary>
/// With `covers` above 0, writes a cover into the folder of each linked book that has no image, at the end of every Link
/// run: first one read out of the book's own files (an EPUB's or a MOBI, AZW or AZW3's, a comic's first page, an m4b's or mp3's embedded picture),
/// then, when the files have none, one fetched from the cover source, if there is one. A book is looked at once
/// (<see cref="TorrentEntity.CoverCheckedAt"/>), and again only when it is linked again or refreshed. Series libraries
/// are left out: one series folder holds many books. Never from MAM, whose image server refuses Fetchshelf.
/// </summary>
public sealed class CoverWriter(
    FetchshelfDbContext dbContext,
    IReadOnlyList<Library> libraries,
    CoverWriterOptions options,
    ICoverSource? coverSource = null,
    LibraryWriteGate? writeGate = null,
    ILogger<CoverWriter>? logger = null)
{
    private readonly ILogger<CoverWriter> _logger = logger ?? NullLogger<CoverWriter>.Instance;
    private readonly LibraryWriteGate _writeGate = writeGate ?? new LibraryWriteGate();

    /// <summary>Books read from the database at a time while looking for those that need a cover.</summary>
    private const int PageSize = 500;

    /// <summary>The file types a cover can be read out of, in the order they are tried.</summary>
    private static readonly string[] CoverBearing = [".epub", ".cbz", ".cbr", ".azw3", ".azw", ".mobi", ".m4b", ".m4a", ".mp3", ".pdf"];

    /// <summary>
    /// Looks at every book not looked at yet. A book that doesn't need a cover (it has an image, or is in a series
    /// library) is settled. One that does gets its files read while the run's file budget lasts, and a cover fetched when
    /// they have none, while the run's fetch limit lasts; past either limit it waits, counted, for a later run.
    /// </summary>
    public async Task<CoverRunSummary> RunAsync(CancellationToken cancellationToken = default)
    {
        var (fromFiles, fetches, waiting) = (0, 0, 0);
        var fetchedFrom = new Dictionary<string, int>();
        var reading = Stopwatch.StartNew();
        var fetching = coverSource is not null && options.MaxFetchesPerRun > 0;
        (long MamId, string Id)? after = null;
        while (true)
        {
            var query = dbContext.Torrents.Where(t => t.LibraryPath != null && t.CoverCheckedAt == null);
            if (after is var (mamId, id))
            {
                // Books that wait stay unchecked, so each page starts after the last one read rather than at the top.
                query = query.Where(t => t.MamId > mamId || (t.MamId == mamId && string.Compare(t.Id, id) > 0));
            }

            var page = await query
                .OrderBy(t => t.MamId).ThenBy(t => t.Id) // a stable order, so each run goes on where the last stopped
                .Take(PageSize)
                .ToListAsync(cancellationToken);
            foreach (var torrent in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!NeedsCover(torrent))
                {
                    torrent.CoverCheckedAt = DateTimeOffset.UtcNow;
                    continue;
                }

                // The files first, while the budget lasts: a book whose files weren't read waits, rather than having a
                // cover fetched that its files may have.
                if (reading.Elapsed >= options.EffectiveFileBudget)
                {
                    waiting++;
                    continue;
                }

                if (FromFiles(torrent) is { } embedded)
                {
                    if (await WriteAsync(torrent, embedded, "its files", cancellationToken))
                    {
                        fromFiles++;
                    }

                    torrent.CoverCheckedAt = DateTimeOffset.UtcNow;
                    continue;
                }

                if (coverSource is null || !MayFetch(torrent) || !coverSource.CanFind(CoverQuery.For(torrent)))
                {
                    // Nothing more to try: settled without a cover.
                    torrent.CoverCheckedAt = DateTimeOffset.UtcNow;
                    continue;
                }

                if (!fetching || fetches >= options.MaxFetchesPerRun)
                {
                    fetching = false;
                    waiting++;
                    continue;
                }

                fetches++;
                var (outcome, source) = await FetchAndWriteAsync(torrent, cancellationToken);
                if (outcome is FetchOutcome.Refused or FetchOutcome.Deferred)
                {
                    // Refused: every later request would fail too, so the rest wait for the next run. Deferred: the
                    // sources that could find this book refused, others still work.
                    fetching = outcome != FetchOutcome.Refused;
                    waiting++;
                    continue;
                }

                if (outcome == FetchOutcome.Written)
                {
                    fetchedFrom[source!] = fetchedFrom.GetValueOrDefault(source!) + 1;
                }

                torrent.CoverCheckedAt = DateTimeOffset.UtcNow;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (page.Count < PageSize)
            {
                break;
            }

            after = (page[^1].MamId, page[^1].Id);
        }

        var fetched = fetchedFrom.Values.Sum();
        if (waiting > 0)
        {
            _logger.LogInformation(
                "Covers: {FromFiles} written from the books' files and {Fetched} fetched in this run; {Count} more books without a cover will get theirs in the next runs",
                fromFiles, fetched, waiting);
        }

        return new CoverRunSummary(fromFiles, fetched, waiting) { FetchedFrom = fetchedFrom };
    }

    /// <summary>
    /// Whether the book's folder needs a cover: it is a book folder of a configured library that places files, it exists,
    /// and it holds no image.
    /// </summary>
    private bool NeedsCover(TorrentEntity torrent)
    {
        var folder = torrent.LibraryPath!;
        return LibraryFolders.IndexContaining(libraries, folder) is { } index
            && libraries[index] is { IsSeriesLayout: false, Method: not LibraryLinkMethod.NoLink }
            && Directory.Exists(folder)
            && CoverFile.IsNeeded(Directory.EnumerateFiles(folder).Select(Path.GetFileName)!);
    }

    /// <summary>
    /// Whether a cover may be fetched for the book: a cover source finds it by what MAM says about it, so not for a
    /// torrent MAM doesn't know (id 0), nor a book of a split collection, whose details are the collection's.
    /// </summary>
    private static bool MayFetch(TorrentEntity torrent) => torrent.MamId > 0 && torrent.CollectionTorrentId is null;

    /// <summary>
    /// The cover inside the first of the book's linked files that has one, among EPUBs, comics and m4b, m4a or mp3 files;
    /// of an audiobook in many files, only its first file of each type is read. Read without the library write gate: a file
    /// that went meanwhile is simply no cover.
    /// </summary>
    private CoverImage? FromFiles(TorrentEntity torrent)
    {
        var files = torrent.LinkedFilesJson is null ? [] : JsonSerializer.Deserialize<List<LinkedFileRecord>>(torrent.LinkedFilesJson) ?? [];
        var candidates = files
            .Select(file => file.RelativePath)
            .Where(path => CoverBearing.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .GroupBy(path => Path.GetExtension(path).ToLowerInvariant())
            .OrderBy(group => Array.IndexOf(CoverBearing, group.Key))
            .Select(group => group.Order(StringComparer.OrdinalIgnoreCase).First());
        foreach (var relative in candidates)
        {
            try
            {
                if (BookCoverExtractor.Extract(Path.Combine(torrent.LibraryPath!, relative)) is { } cover)
                {
                    return new CoverImage(cover.Bytes, cover.ContentType);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _logger.LogDebug("No cover read from {File} of {Title}: {Error}", relative, torrent.Title, ex.Message);
            }
        }

        return null;
    }

    private enum FetchOutcome { Written, NotWritten, Refused, Deferred }

    /// <summary>
    /// Fetches the book's cover from the cover source and writes it into its folder: Written, with the source's name;
    /// NotWritten when the source has none, or the request failed for a reason that says nothing about the source (logged,
    /// and not tried again); Refused when the source refused, which ends fetching for this run; Deferred when the sources
    /// that could find this book refused, and it waits.
    /// </summary>
    private async Task<(FetchOutcome Outcome, string? Source)> FetchAndWriteAsync(TorrentEntity torrent, CancellationToken cancellationToken)
    {
        CoverImage? image;
        try
        {
            image = await coverSource!.FetchAsync(CoverQuery.For(torrent), cancellationToken);
        }
        catch (CoverSourceRefusedException ex)
        {
            _logger.LogWarning("Stopped fetching covers: {Error} The rest are fetched in a later run", ex.Message);
            return (FetchOutcome.Refused, null);
        }
        catch (CoverSourceDeferredException)
        {
            return (FetchOutcome.Deferred, null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogInformation("No cover for {Title}: {Error}", torrent.Title, ex.Message);
            return (FetchOutcome.NotWritten, null);
        }

        if (image is null)
        {
            _logger.LogInformation("No cover for {Title}: none was found", torrent.Title);
            return (FetchOutcome.NotWritten, null);
        }

        var source = image.Source ?? "the cover source";
        return await WriteAsync(torrent, image, source, cancellationToken) ? (FetchOutcome.Written, source) : (FetchOutcome.NotWritten, null);
    }

    /// <summary>
    /// Writes the cover into the book's folder as <c>cover.jpg</c> (or <c>.png</c>, <c>.webp</c>), under the library write
    /// gate. False when the image is of none of those types, or the folder has gone or got an image meanwhile.
    /// </summary>
    private async Task<bool> WriteAsync(TorrentEntity torrent, CoverImage image, string from, CancellationToken cancellationToken)
    {
        if (CoverFile.NameFor(image.ContentType) is not { } name)
        {
            _logger.LogInformation("No cover for {Title}: {From} has none as a JPEG, PNG or WebP image", torrent.Title, from);
            return false;
        }

        // The Link and Clean jobs and bulk actions change library folders too; the folder is checked again under the gate.
        using var _ = await _writeGate.AcquireAsync(cancellationToken);
        var folder = torrent.LibraryPath!;
        if (!Directory.Exists(folder) || !CoverFile.IsNeeded(Directory.EnumerateFiles(folder).Select(Path.GetFileName)!))
        {
            return false;
        }

        // Through a temporary name, so a cut-short write never leaves a broken cover behind under the real one.
        var path = Path.Combine(folder, name);
        var temporary = Path.Combine(folder, $".{name}.part");
        await File.WriteAllBytesAsync(temporary, image.Bytes, CancellationToken.None);
        File.Move(temporary, path);
        _logger.LogInformation("Wrote {Cover} for {Title} into {Folder}, from {From}", name, torrent.Title, folder, from);
        return true;
    }
}
