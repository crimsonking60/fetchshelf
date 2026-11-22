using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Organizer;

/// <summary>What one cover pass did: covers written, and books still waiting for theirs because the run's limit was reached.</summary>
public sealed record CoverRunSummary(int Written, int Waiting);

/// <summary>The settings the cover pass runs by.</summary>
/// <param name="MaxFetchesPerRun">
/// `covers_from_mam`: the most covers one run fetches from MAM. A first run over a large library would otherwise send
/// one request per book; the rest are fetched in later runs. 0 fetches none.
/// </param>
public sealed record CoverWriterOptions(int MaxFetchesPerRun);

/// <summary>
/// With `covers_from_mam` above 0, writes MAM's cover image into the folder of each linked book that has no image, at the end of
/// every Link run. A book is looked at once (<see cref="TorrentEntity.CoverCheckedAt"/>), and again only when it is
/// linked again or refreshed. Series libraries are left out: comics and manga carry their cover inside the file, and
/// one series folder holds many books.
/// </summary>
public sealed class CoverWriter(
    FetchshelfDbContext dbContext,
    ICoverSource coverSource,
    IReadOnlyList<Library> libraries,
    CoverWriterOptions options,
    LibraryWriteGate? writeGate = null,
    ILogger<CoverWriter>? logger = null)
{
    private readonly ILogger<CoverWriter> _logger = logger ?? NullLogger<CoverWriter>.Instance;
    private readonly LibraryWriteGate _writeGate = writeGate ?? new LibraryWriteGate();

    /// <summary>Books read from the database at a time while looking for those that need a cover.</summary>
    private const int PageSize = 500;

    /// <summary>
    /// Looks at every book not looked at yet, and fetches covers for those that need one until the run's limit is reached
    /// or MAM refuses the account. Past that point the rest are still looked at, without fetching: a book that doesn't
    /// need a cover (it has an image, or is in a series library) is settled, and one that does is counted and waits, so
    /// the number waiting is exact and later runs only look at those.
    /// </summary>
    public async Task<CoverRunSummary> RunAsync(CancellationToken cancellationToken = default)
    {
        var (fetches, written, waiting) = (0, 0, 0);
        var fetching = options.MaxFetchesPerRun > 0;
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
                if (!NeedsCover(torrent))
                {
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
                var result = await FetchAndWriteAsync(torrent, cancellationToken);
                if (result is null)
                {
                    // MAM refused the account: every later request would fail too, so the rest wait for the next run.
                    fetching = false;
                    waiting++;
                    continue;
                }

                if (result == true)
                {
                    written++;
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

        if (waiting > 0)
        {
            _logger.LogInformation(
                "Fetched {Fetched} covers from MAM in this run; {Count} more books without a cover will get theirs in the next runs",
                fetches, waiting);
        }

        return new CoverRunSummary(written, waiting);
    }

    /// <summary>
    /// Whether the book's folder needs a cover: it is a book folder of a configured library that places files, it exists,
    /// and it holds no image. A torrent MAM doesn't know (id 0) has no cover there.
    /// </summary>
    private bool NeedsCover(TorrentEntity torrent)
    {
        var folder = torrent.LibraryPath!;
        // A book of a split collection shares its MAM id with the collection: MAM's cover is the collection's, not the book's.
        return torrent.MamId > 0
            && torrent.CollectionTorrentId is null
            && LibraryFolders.IndexContaining(libraries, folder) is { } index
            && libraries[index] is { IsSeriesLayout: false, Method: not LibraryLinkMethod.NoLink }
            && Directory.Exists(folder)
            && CoverFile.IsNeeded(Directory.EnumerateFiles(folder).Select(Path.GetFileName)!);
    }

    /// <summary>
    /// Fetches the book's cover and writes it into its folder. True when written; false when MAM has none, or the
    /// request failed for a reason that says nothing about the account (logged, and not tried again); null when MAM
    /// refused the account, which ends the pass.
    /// </summary>
    private async Task<bool?> FetchAndWriteAsync(TorrentEntity torrent, CancellationToken cancellationToken)
    {
        CoverImage? image;
        try
        {
            image = await coverSource.FetchAsync(torrent, cancellationToken);
        }
        catch (CoverSourceRefusedException ex)
        {
            _logger.LogWarning("Stopped fetching covers: {Error} The rest are fetched in a later run", ex.Message);
            return null;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogInformation("No cover for {Title}: {Error}", torrent.Title, ex.Message);
            return false;
        }

        if (image is null || CoverFile.NameFor(image.ContentType) is not { } name)
        {
            _logger.LogInformation("No cover for {Title}: none was found as a JPEG, PNG or WebP image", torrent.Title);
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
        _logger.LogInformation("Wrote {Cover} for {Title} into {Folder}", name, torrent.Title, folder);
        return true;
    }
}
