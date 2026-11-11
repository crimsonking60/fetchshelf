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
public sealed class AbsMatcher(
    FetchshelfDbContext dbContext,
    AbsClient absClient,
    ILogger<AbsMatcher>? logger = null,
    IReadOnlyDictionary<string, string>? fetchshelfToAbsPaths = null)
{
    private readonly IReadOnlyDictionary<string, string> _fetchshelfToAbsPaths = fetchshelfToAbsPaths ?? new Dictionary<string, string>();
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

        try
        {
            foreach (var torrent in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
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

                var absPath = PathMapper.Translate(_fetchshelfToAbsPaths, torrent.LibraryPath!);
                var candidateLibraries = libraries.Where(library => library.Folders.Any(folder => PathPrefix.IsSameOrUnder(absPath, folder.FullPath))).ToList();
                if (candidateLibraries.Count == 0)
                {
                    _logger.LogError(
                        "Cannot match {Title} in Audiobookshelf: no Audiobookshelf library has a folder containing {AbsPath}. "
                        + "Audiobookshelf must see the library at the same path as Fetchshelf, or [audiobookshelf.path_mapping] must map "
                        + "Audiobookshelf's path to Fetchshelf's",
                        torrent.Title, absPath);
                    failed++;
                    continue;
                }

                string? matchedId;
                try
                {
                    matchedId = await FindMatchAsync(candidateLibraries, absPath, firstAuthor, cancellationToken);
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
                        "{Title} is not in Audiobookshelf yet at {AbsPath}; trying again next run. "
                        + "If this persists, check that Audiobookshelf has scanned the library",
                        torrent.Title, absPath);
                    notFoundYet++;
                    continue;
                }

                torrent.AbsId = matchedId;
                matched++;
                _logger.LogInformation("Matched {Title} to Audiobookshelf item {AbsId}", torrent.Title, matchedId);
            }

            // Every book was gone through: what was matched is saved even if the run was cancelled meanwhile.
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The matches found before the run was cancelled, or stopped by an error, are kept if they can be. If even
            // that fails, the error that stopped the run is the one reported.
            try
            {
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception saveException)
            {
                _logger.LogWarning(
                    "Could not keep the Audiobookshelf matches found before the run stopped ({Reason}): {Error}",
                    ex.Message, saveException.Message);
            }

            throw;
        }

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
}
