namespace Fetchshelf.Data.Entities;

/// <summary>A shelf or list of wanted books from another service, such as a Hardcover reading status or list.</summary>
public sealed class ListEntity
{
    /// <summary>E.g. "hardcover:status:want_to_read" or "hardcover:list:books-to-fetch".</summary>
    public required string Id { get; set; }

    public required string Name { get; set; }

    /// <summary>The service the list comes from, e.g. "Hardcover".</summary>
    public required string ListType { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the list was last read from its service.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>What Fetchshelf found for one format (audiobook or ebook) of a book on a list.</summary>
public static class ListItemSideStatus
{
    /// <summary>A MAM torrent was selected for download.</summary>
    public const string Selected = "Selected";

    /// <summary>A MAM torrent matches, but costs more than the list's grab blocks allow.</summary>
    public const string Wanted = "Wanted";

    /// <summary>Found, but left out because the list prefers the other format.</summary>
    public const string NotWanted = "NotWanted";

    /// <summary>The library already has the book in this format.</summary>
    public const string Existing = "Existing";

    /// <summary>MAM had nothing that matched when last searched.</summary>
    public const string NotFound = "NotFound";

    /// <summary>Statuses after which the format is never searched for again.</summary>
    public static bool IsResolved(string? status) => status is Selected or Existing or NotWanted;
}

/// <summary>
/// One book on a list, with what was found for each format. Keyed by the list plus the book's id at its service, so
/// reading the list again updates the same row. Each format has its own MAM id, status and time columns.
/// </summary>
public sealed class ListItemEntity
{
    public required string ListId { get; set; }

    /// <summary>The book's id at the list's service, e.g. the Hardcover book id.</summary>
    public required string ExternalId { get; set; }

    /// <summary>The book's place in the list as last read, for showing the books in the list's own order.</summary>
    public int Position { get; set; }

    public required string BookTitle { get; set; }

    /// <summary>JSON array of author names.</summary>
    public required string AuthorsJson { get; set; }

    public string? SeriesName { get; set; }

    public double? SeriesNumber { get; set; }

    public string? CoverUrl { get; set; }

    /// <summary>The book's page at the list's service.</summary>
    public string? BookUrl { get; set; }

    /// <summary>"Audio" or "Ebook" when the list configures a preferred format, else null.</summary>
    public string? PreferFormat { get; set; }

    public bool AllowAudio { get; set; }

    public bool AllowEbook { get; set; }

    public long? AudioMamId { get; set; }

    /// <summary>One of the <see cref="ListItemSideStatus"/> values; null when the audiobook has not been looked for yet.</summary>
    public string? AudioStatus { get; set; }

    public DateTimeOffset? AudioAt { get; set; }

    public long? EbookMamId { get; set; }

    public string? EbookStatus { get; set; }

    public DateTimeOffset? EbookAt { get; set; }

    /// <summary>When MAM was last searched for this book, so a book that wasn't found is not searched again too soon.</summary>
    public DateTimeOffset? LastSearchedAt { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set by the user on the Lists page: the book is no longer searched for.</summary>
    public DateTimeOffset? MarkedDoneAt { get; set; }

    /// <summary>When the book was no longer on the list at its service; cleared if it comes back.</summary>
    public DateTimeOffset? RemovedAt { get; set; }
}
