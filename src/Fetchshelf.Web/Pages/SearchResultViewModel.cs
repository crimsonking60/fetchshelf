using Fetchshelf.Clients.Mam;

namespace Fetchshelf.Web.Pages;

/// <summary>Display model for one row on the Search page, Fetchshelf-spec.md §10.4.</summary>
public sealed record SearchResultViewModel(
    long MamId, string Title, string AuthorsDisplay, long SizeBytes, string SizeDisplay, bool IsFree, bool IsPersonalFreeleech, bool IsVip,
    bool WedgePreHighlighted, MamResultStatus Status, string MediaTypeSlug, string MediaTypeLabel)
{
    public static SearchResultViewModel From(MamTorrentRecord record, long sizeBytes, long? wedgeOverBytes, MamResultStatus status) => new(
        record.MamId, record.Title, AuthorsDisplayOf(record), sizeBytes, record.SizeRaw, record.IsFree, record.IsPersonalFreeleech, record.IsVip,
        WedgePreHighlighted: wedgeOverBytes is { } threshold && sizeBytes > threshold,
        Status: status,
        MediaTypeSlug: MediaTypeSlugOf(record),
        MediaTypeLabel: MediaTypeLabelOf(record));

    private static string AuthorsDisplayOf(MamTorrentRecord record) =>
        string.Join(", ", record.Authors.OrderBy(kv => kv.Key).Select(kv => kv.Value));

    /// <summary>
    /// "ebook"/"audiobook"/"comic"/"other", for the browser-side Ebooks/Audiobooks/Comics filter buttons on the
    /// Search page to match against (see the page's <c>applyMediaFilter</c> script). Mirrors
    /// <c>MamTorrentMapper</c>'s media-type fallback (<c>MediaTypeId</c>, else the old main category,
    /// Fetchshelf-spec.md §12.1) rather than the record's raw main category alone, so newer
    /// mediatype-only records still classify correctly.
    /// </summary>
    private static string MediaTypeSlugOf(MamTorrentRecord record) => EffectiveMediaTypeId(record) switch
    {
        1 or 8 => "audiobook", // Audiobook, Periodical Audiobook
        2 or 7 => "ebook", // Ebook, Periodical Ebook
        5 or 6 => "comic", // Manga, Comic Book / Graphic Novel
        _ => "other",
    };

    /// <summary>Display name for the Media type column, Fetchshelf-spec.md §12.1; "Unknown" for ids it does not list.</summary>
    private static string MediaTypeLabelOf(MamTorrentRecord record) => EffectiveMediaTypeId(record) switch
    {
        1 => "Audiobook",
        2 => "Ebook",
        3 => "Musicology",
        4 => "Radio",
        5 => "Manga",
        6 => "Comic Book / Graphic Novel",
        7 => "Periodical Ebook",
        8 => "Periodical Audiobook",
        _ => "Unknown",
    };

    private static int EffectiveMediaTypeId(MamTorrentRecord record) => record.MediaTypeId != 0 ? record.MediaTypeId : record.OldMainCategoryId switch
    {
        13 => 1, // Audiobook
        14 => 2, // Ebook
        15 => 3, // Musicology
        16 => 4, // Radio
        _ => 0,
    };
}
