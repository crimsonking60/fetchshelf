using Fetchshelf.Clients.Abs;
using Fetchshelf.Core.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Organizer;

/// <summary>
/// Matches linked torrents to their Audiobookshelf library item, Fetchshelf-spec.md §9. Distinct from the
/// metadata-push hooks in <see cref="LinkRunner"/>, which act only once a torrent already has a
/// stored id.
/// </summary>
public sealed class AbsMatcher(FetchshelfDbContext dbContext, AbsClient absClient)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await dbContext.Torrents
            .Where(t => t.LibraryPath != null && t.AbsId == null)
            .ToListAsync(cancellationToken);

        foreach (var torrent in candidates)
        {
            var metadata = TorrentMetadataSerializer.Deserialize(torrent.MetadataJson);
            var firstAuthor = metadata.Authors.FirstOrDefault();
            if (firstAuthor is null)
            {
                continue;
            }

            var matchedId = await FindMatchAsync(torrent.LibraryPath!, firstAuthor, cancellationToken);
            if (matchedId is not null)
            {
                torrent.AbsId = matchedId;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<string?> FindMatchAsync(string libraryPath, string firstAuthor, CancellationToken cancellationToken)
    {
        var libraries = await absClient.ListLibrariesAsync(cancellationToken);
        var candidateLibraries = libraries.Where(library => library.Folders.Any(folder => IsPathPrefix(folder.FullPath, libraryPath)));

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
