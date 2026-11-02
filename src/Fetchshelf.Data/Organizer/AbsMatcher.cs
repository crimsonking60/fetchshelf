using Fetchshelf.Clients.Abs;
using Fetchshelf.Core.Organizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Organizer;

/// <summary>
/// What one matcher run did: books matched to an Audiobookshelf item, books Audiobookshelf does not show
/// yet (it may not have scanned them), and books that cannot be matched or whose lookup failed.
/// </summary>
public sealed record AbsMatchSummary(int Matched, int NotFoundYet, int Failed);

/// <summary>
/// Matches linked torrents to their Audiobookshelf library item. Distinct from the
/// metadata-push hooks in <see cref="LinkRunner"/>, which act only once a torrent already has a
/// stored id.
/// </summary>
public sealed class AbsMatcher(FetchshelfDbContext dbContext, AbsClient absClient, ILogger<AbsMatcher>? logger = null)
{
    private readonly ILogger<AbsMatcher> _logger = logger ?? NullLogger<AbsMatcher>.Instance;

    public async Task<AbsMatchSummary> RunAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await dbContext.Torrents
            .Where(t => t.LibraryPath != null && t.AbsId == null)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            _logger.LogInformation("No linked books are waiting to be matched in Audiobookshelf");
            return new AbsMatchSummary(0, 0, 0);
        }

        IReadOnlyList<AbsLibrary>? libraries = null;
        var (matched, notFoundYet, failed) = (0, 0, 0);

        foreach (var torrent in candidates)
        {
            var metadata = TorrentMetadataSerializer.Deserialize(torrent.MetadataJson);
            var firstAuthor = metadata.Authors.FirstOrDefault();
            if (firstAuthor is null)
            {
                _logger.LogError("Cannot match {Title} in Audiobookshelf: it has no author to search for", torrent.Title);
                failed++;
                continue;
            }

            // Fetched once, and only when some book needs it; failing to reach Audiobookshelf ends the run.
            libraries ??= await ListLibrariesAsync(cancellationToken);

            var candidateLibraries = libraries.Where(library => library.Folders.Any(folder => IsPathPrefix(folder.FullPath, torrent.LibraryPath!))).ToList();
            if (candidateLibraries.Count == 0)
            {
                _logger.LogError(
                    "Cannot match {Title} in Audiobookshelf: no Audiobookshelf library has a folder containing {LibraryPath}. "
                    + "Audiobookshelf must see the library at the same path as Fetchshelf",
                    torrent.Title, torrent.LibraryPath);
                failed++;
                continue;
            }

            string? matchedId;
            try
            {
                matchedId = await FindMatchAsync(candidateLibraries, torrent.LibraryPath!, firstAuthor, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("Could not match {Title} in Audiobookshelf: {Error}", torrent.Title, ex.Message);
                failed++;
                continue;
            }

            if (matchedId is null)
            {
                _logger.LogWarning(
                    "{Title} is not in Audiobookshelf yet at {LibraryPath}; trying again next run. "
                    + "If this persists, check that Audiobookshelf has scanned the library",
                    torrent.Title, torrent.LibraryPath);
                notFoundYet++;
                continue;
            }

            torrent.AbsId = matchedId;
            matched++;
            _logger.LogInformation("Matched {Title} to Audiobookshelf item {AbsId}", torrent.Title, matchedId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new AbsMatchSummary(matched, notFoundYet, failed);
    }

    private async Task<IReadOnlyList<AbsLibrary>> ListLibrariesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await absClient.ListLibrariesAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Could not list the Audiobookshelf libraries: {Error}", ex.Message);
            throw;
        }
    }

    private async Task<string?> FindMatchAsync(
        IReadOnlyList<AbsLibrary> candidateLibraries, string libraryPath, string firstAuthor, CancellationToken cancellationToken)
    {
        foreach (var library in candidateLibraries)
        {
            var authors = await absClient.SearchLibraryAsync(library.Id, firstAuthor, cancellationToken);
            foreach (var author in authors)
            {
                var items = await absClient.GetAuthorItemsAsync(author.Id, cancellationToken);
                var match = items.FirstOrDefault(item => item.Path == libraryPath);
                if (match is not null)
                {
                    return match.Id;
                }
            }
        }

        return null;
    }

    /// <summary>Path-component-aware prefix check (mirrors Rust `Path::starts_with`): a raw string
    /// prefix would wrongly match e.g. "/data/audio" against "/data/audiobooks2".</summary>
    private static bool IsPathPrefix(string folderPath, string torrentLibraryPath)
    {
        var folder = folderPath.TrimEnd('/');
        var torrent = torrentLibraryPath.TrimEnd('/');
        return torrent == folder || torrent.StartsWith(folder + "/", StringComparison.Ordinal);
    }
}
