using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Organizer;

/// <summary>What one cover pass did: covers written, and books still waiting for theirs because the run's limit was reached.</summary>
public sealed record CoverRunSummary(int Written, int Waiting);

/// <summary>
/// With `covers_from_mam`, writes MAM's cover image into the folder of each linked book that has no image, at the end of
/// every Link run. A book is looked at once (<see cref="TorrentEntity.CoverCheckedAt"/>), and again only when it is
/// linked again or refreshed. Series libraries are left out: comics and manga carry their cover inside the file, and
/// one series folder holds many books.
/// </summary>
public sealed class CoverWriter(
    FetchshelfDbContext dbContext,
    IMamCoverSource coverSource,
    IReadOnlyList<Library> libraries,
    LibraryWriteGate? writeGate = null,
    ILogger<CoverWriter>? logger = null)
{
    private readonly ILogger<CoverWriter> _logger = logger ?? NullLogger<CoverWriter>.Instance;
    private readonly LibraryWriteGate _writeGate = writeGate ?? new LibraryWriteGate();

    /// <summary>
    /// The most covers one run fetches from MAM. A first run over a large library would otherwise send one request per
    /// book; the rest are fetched in later runs.
    /// </summary>
    public const int MaxFetchesPerRun = 20;

    /// <summary>Books read from the database at a time while looking for those that need a cover.</summary>
    private const int PageSize = 500;

    public async Task<CoverRunSummary> RunAsync(CancellationToken cancellationToken = default)
    {
        var (fetches, written) = (0, 0);
        while (true)
        {
            var page = await dbContext.Torrents
                .Where(t => t.LibraryPath != null && t.CoverCheckedAt == null)
                .OrderBy(t => t.MamId).ThenBy(t => t.Id) // a stable order, so each run goes on where the last stopped
                .Take(PageSize)
                .ToListAsync(cancellationToken);
            var settled = 0;
            foreach (var torrent in page)
            {
                if (!NeedsCover(torrent))
                {
                    torrent.CoverCheckedAt = DateTimeOffset.UtcNow;
                    settled++;
                    continue;
                }

                if (fetches == MaxFetchesPerRun)
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                    var waiting = await CountWaitingAsync(cancellationToken);
                    _logger.LogInformation(
                        "Fetched {Max} covers from MAM, the most for one run; {Count} more books will get theirs in the next runs",
                        MaxFetchesPerRun, waiting);
                    return new CoverRunSummary(written, waiting);
                }

                fetches++;
                switch (await FetchAndWriteAsync(torrent, cancellationToken))
                {
                    case true:
                        written++;
                        break;
                    case null:
                        // MAM refused the account: every later request would fail too, so the rest wait for the next run.
                        await dbContext.SaveChangesAsync(cancellationToken);
                        return new CoverRunSummary(written, await CountWaitingAsync(cancellationToken));
                }

                torrent.CoverCheckedAt = DateTimeOffset.UtcNow;
                settled++;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (page.Count < PageSize || settled == 0)
            {
                return new CoverRunSummary(written, 0);
            }
        }
    }

    /// <summary>Books that would be looked at: some of them have an image already, so this is at most how many need one.</summary>
    private Task<int> CountWaitingAsync(CancellationToken cancellationToken) =>
        dbContext.Torrents.CountAsync(t => t.LibraryPath != null && t.CoverCheckedAt == null, cancellationToken);

    /// <summary>
    /// Whether the book's folder needs a cover: it is a book folder of a configured library that places files, it exists,
    /// and it holds no image. A torrent MAM doesn't know (id 0) has no cover there.
    /// </summary>
    private bool NeedsCover(TorrentEntity torrent)
    {
        var folder = torrent.LibraryPath!;
        return torrent.MamId > 0
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
        MamImage? image;
        try
        {
            image = await coverSource.FetchAsync(torrent.MamId, cancellationToken);
        }
        catch (Exception ex) when (ex is MamRateLimitedException or MamAccountBlockedException or MamForbiddenException)
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
            _logger.LogInformation("No cover for {Title}: MAM has none it serves as a JPEG, PNG or WebP image", torrent.Title);
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
