using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>A configured library as selection sees it: what it takes, and its own format lists.</summary>
public sealed record SelectionLibrary(LibraryMatchRule Match, LibraryFormatLists Formats);

/// <summary>
/// The library a torrent being selected will be linked into, worked out as the Link job will: by the category, save path
/// and tags it is added to qBittorrent with, then by its genres. Selection takes that library's format lists, so a
/// torrent with no format the library links, such as a PDF novel for a library of epubs, isn't downloaded.
/// </summary>
/// <param name="mapSavePath">
/// A save path as qBittorrent sees it, for the named client (null for the first), as Fetchshelf sees it: what a
/// library's `download_dir` is compared with.
/// </param>
public sealed class SelectionLibraries(IReadOnlyList<SelectionLibrary> libraries, Func<string?, string, string> mapSavePath)
{
    public static readonly SelectionLibraries None = new([], (_, path) => path);

    private readonly IReadOnlyList<LibraryMatchRule> _rules = libraries.Select(l => l.Match).ToList();

    /// <summary>
    /// The format lists of the library the torrent will go to; null when none takes it, or it is to be saved where
    /// qBittorrent chooses and no library takes its category, so that it is selected by the global lists as before.
    /// </summary>
    public LibraryFormatLists? FormatsFor(DownloadPlacement placement, IReadOnlyCollection<string> tags, TorrentMetadata metadata)
    {
        if (libraries.Count == 0)
        {
            return null;
        }

        var savePath = placement.SavePath is { } path ? mapSavePath(placement.Client, path) : string.Empty;
        var candidates = LibraryMatcher.CandidateIndexes(_rules, placement.Category, savePath, [.. tags]);
        return LibraryMatcher.ChooseForBook(_rules, candidates, metadata) is { } index ? libraries[index].Formats : null;
    }
}
