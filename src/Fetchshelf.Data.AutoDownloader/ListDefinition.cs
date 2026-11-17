using Fetchshelf.Clients.Hardcover;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.Lists;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>Reads the books on one configured list. One instance per list, so it carries that list's own settings.</summary>
public interface IListBookSource
{
    Task<IReadOnlyList<ListBook>> GetBooksAsync(CancellationToken cancellationToken = default);

    /// <summary>How many lines of the last read couldn't be read as a book; only a file has any.</summary>
    int UnreadableLines => 0;

    /// <summary>
    /// The MAM torrent a book of the last read is, for a source whose books are torrents already (MAM bookmarks); null
    /// for a book that has to be searched for. Such a book isn't searched for: its torrent is the only candidate.
    /// </summary>
    MamTorrentRecord? KnownTorrent(string externalId) => null;
}

/// <summary>
/// What <see cref="ListImporter"/> needs from a list's configuration, whatever its source.
/// </summary>
/// <param name="ListId">The list row's id, which its items are stored under.</param>
/// <param name="DisplayName">The name shown on the Lists page and in the log.</param>
/// <param name="ListType">The source as the Lists page names it, such as "Hardcover".</param>
/// <param name="JobName">The list's job name, which its selections carry and its own buffers are found by.</param>
/// <param name="ErrorStage">What the Errors page records a book the list couldn't search for under.</param>
public sealed record ListDefinition(string ListId, string DisplayName, string ListType, string JobName, ErrorStage ErrorStage)
{
    /// <summary>Restricted to <see cref="FilterMainType.Audio"/>/<see cref="FilterMainType.Ebook"/>.</summary>
    public FilterMainType? PreferFormat { get; init; }

    /// <summary>qBittorrent category for everything this list downloads.</summary>
    public string? Category { get; init; }

    public IReadOnlyList<GrabEntry> Grab { get; init; } = [];

    /// <summary>Overrides the top-level `unsat_buffer` for what this list selects.</summary>
    public int? UnsatBuffer { get; init; }

    /// <summary>Overrides the top-level `wedge_buffer` for what this list selects.</summary>
    public int? WedgeBuffer { get; init; }

    /// <summary>The most selections this list may have waiting to be downloaded; unset means no cap.</summary>
    public int? MaxActiveDownloads { get; init; }

    public int? SearchInterval { get; init; }

    public bool DryRun { get; init; }

    /// <summary>Where a file list's books are read from: its `path` as configured. Null for other lists.</summary>
    public string? FilePath { get; init; }

    /// <summary>Every configured list, Hardcover ones first, then files, each in the order the config file has them.</summary>
    public static IReadOnlyList<ListDefinition> AllOf(FetchshelfSettings settings) =>
        settings.HardcoverList.Select((entry, i) => ForHardcover(entry, i))
            .Concat(settings.FileList.Select((entry, i) => ForFile(entry, i)))
            .Concat(settings.BookmarksList.Select(ForBookmarks))
            .ToList();

    public static ListDefinition ForHardcover(HardcoverListEntry entry, int index) =>
        new(HardcoverListId(entry), HardcoverDisplayName(entry), "Hardcover", AutoDownloaderJobNames.HardcoverList(entry, index), ErrorStage.HardcoverList)
        {
            PreferFormat = entry.PreferFormat,
            Category = entry.Category,
            Grab = entry.Grab,
            UnsatBuffer = entry.UnsatBuffer,
            WedgeBuffer = entry.WedgeBuffer,
            MaxActiveDownloads = entry.MaxActiveDownloads,
            SearchInterval = entry.SearchInterval,
            DryRun = entry.DryRun,
        };

    /// <summary>
    /// A file list, under the id <c>file:</c> and its job name: a new file at the same name keeps the list's items, and
    /// renaming the list starts a new one.
    /// </summary>
    public static ListDefinition ForFile(FileListEntry entry, int index)
    {
        var jobName = AutoDownloaderJobNames.FileList(entry, index);
        return new ListDefinition($"file:{jobName}", jobName, "File", jobName, ErrorStage.FileList)
        {
            PreferFormat = entry.PreferFormat,
            Category = entry.Category,
            Grab = entry.Grab,
            UnsatBuffer = entry.UnsatBuffer,
            WedgeBuffer = entry.WedgeBuffer,
            MaxActiveDownloads = entry.MaxActiveDownloads,
            SearchInterval = entry.SearchInterval,
            DryRun = entry.DryRun,
            FilePath = entry.Path,
        };
    }

    /// <summary>The bookmarks list, under the id <c>mam:bookmarks</c>: there is one.</summary>
    public static ListDefinition ForBookmarks(BookmarksListEntry entry)
    {
        var jobName = AutoDownloaderJobNames.BookmarksList(entry);
        return new ListDefinition(BookmarksListId, jobName, BookmarksListType, jobName, ErrorStage.BookmarksList)
        {
            Category = entry.Category,
            Grab = entry.Grab,
            UnsatBuffer = entry.UnsatBuffer,
            WedgeBuffer = entry.WedgeBuffer,
            MaxActiveDownloads = entry.MaxActiveDownloads,
            SearchInterval = entry.SearchInterval,
            DryRun = entry.DryRun,
        };
    }

    public const string BookmarksListId = "mam:bookmarks";

    /// <summary>The bookmarks list's source as the Lists page names it.</summary>
    public const string BookmarksListType = "MAM bookmarks";

    /// <summary>A Hardcover list row's id: one per reading status or Hardcover list.</summary>
    public static string HardcoverListId(HardcoverListEntry entry) =>
        entry.List is { } slug ? $"hardcover:list:{slug}" : $"hardcover:status:{StatusName(entry.Status!.Value)}";

    /// <summary>A Hardcover list's name: the entry's own name, else the status or list.</summary>
    public static string HardcoverDisplayName(HardcoverListEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Name) ? entry.Name
        : entry.List is { } slug ? slug
        : HardcoverClient.Describe(HardcoverBookSource.ToReadingStatus(entry.Status!.Value));

    private static string StatusName(HardcoverListStatus status) => status switch
    {
        HardcoverListStatus.WantToRead => "want_to_read",
        HardcoverListStatus.CurrentlyReading => "currently_reading",
        HardcoverListStatus.Read => "read",
        HardcoverListStatus.Paused => "paused",
        HardcoverListStatus.DidNotFinish => "did_not_finish",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
