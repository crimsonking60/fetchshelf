using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Web.Pages;

/// <summary>Display model for one row on the Search page.</summary>
public sealed record SearchResultViewModel(
    long MamId, string Title, string AuthorsDisplay, long SizeBytes, string SizeDisplay, bool IsFree, bool IsPersonalFreeleech, bool IsVip,
    bool WedgePreHighlighted, MamResultStatus Status, string MediaTypeSlug, string MediaTypeLabel)
{
    /// <summary>True when qBittorrent has the torrent, false when it doesn't, null when qBittorrent couldn't be asked.</summary>
    public bool? InQBittorrent { get; init; }

    /// <summary>The torrent's category in qBittorrent, "" for none; only set when <see cref="InQBittorrent"/> is true.</summary>
    public string? QBittorrentCategory { get; init; }

    /// <summary>
    /// Why Fetchshelf can't select the torrent, so the row offers no download buttons; null when it can. Set when its
    /// file types are unknown, or none of them is in the format list for its media type: selecting it would fail.
    /// </summary>
    public string? CannotDownloadReason { get; init; }

    /// <summary>Whether the account has snatched the torrent before, as MAM says.</summary>
    public bool IsSnatchedByMe { get; init; }

    /// <summary>
    /// Whether the row says *Snatched*: only when nothing else on it says the torrent was downloaded. A torrent that is
    /// downloading, downloaded or linked, or that qBittorrent has, was snatched whatever MAM says; one with no status, or
    /// only selected, may be a snatch from outside Fetchshelf or from before, which is worth knowing before downloading.
    /// </summary>
    public bool ShowSnatched => IsSnatchedByMe && Status is MamResultStatus.None or MamResultStatus.Selected && InQBittorrent != true;

    /// <summary>How complete the torrent's metadata on MAM is; null for rows built without the MAM record.</summary>
    public MetadataQualityRating? MetadataQuality { get; init; }

    /// <summary>
    /// How well the torrent's formats fit the format list for its media type; null when no list applies (no format lists
    /// given, or a media type Fetchshelf doesn't know).
    /// </summary>
    public FormatQualityRating? FormatQuality { get; init; }

    /// <summary>The Quality pill's grade: the worse of the metadata's and the formats'.</summary>
    public MetadataQualityGrade? QualityGrade => MetadataQuality is not { } metadata ? null
        : FormatQuality is { } format ? Core.Metadata.FormatQuality.Worse(metadata.Grade, format.Grade)
        : metadata.Grade;

    /// <summary>The pill's word for <see cref="QualityGrade"/>.</summary>
    public string? QualityLabel => QualityGrade switch
    {
        MetadataQualityGrade.Good => "Good",
        MetadataQualityGrade.Lacking => "Lacking",
        MetadataQualityGrade.Poor => "Poor",
        _ => null,
    };

    /// <summary>The tooltip's first line, what is wrong with the formats; null when nothing is.</summary>
    public string? FormatQualityDetail => FormatQuality?.Issue;

    /// <summary>The tooltip's metadata line: what is missing, or that nothing is.</summary>
    public string? MetadataQualityDetail => MetadataQuality is not { } rating ? null
        : rating.Missing.Count == 0 ? "Author, file types, description, cover, genres and ISBN or ASIN are all there."
        : $"Missing: {string.Join(", ", rating.Missing)}.";

    public static SearchResultViewModel From(
        MamTorrentRecord record, long sizeBytes, long? wedgeOverBytes, MamResultStatus status, FormatPreferenceLists? preferenceLists = null) => new(
        record.MamId, record.Title, AuthorsDisplayOf(record), sizeBytes, record.SizeRaw, record.IsFree, record.IsPersonalFreeleech, record.IsVip,
        WedgePreHighlighted: wedgeOverBytes is { } threshold && sizeBytes > threshold,
        Status: status,
        MediaTypeSlug: MediaTypeSlugOf(record),
        MediaTypeLabel: MediaTypeLabelOf(record))
        {
            MetadataQuality = RateMetadata(record),
            FormatQuality = preferenceLists is null ? null : RateFormats(record, preferenceLists),
            IsSnatchedByMe = record.IsSnatchedByMe,
            CannotDownloadReason = preferenceLists is null ? null : CannotDownloadReasonFor(record, preferenceLists),
        };

    /// <summary>
    /// Why selecting the torrent would fail on its formats, in the words the Download button's refusal would use; null
    /// when one of its file types is wanted. This is the check selection makes, so the buttons only show when it passes.
    /// </summary>
    public static string? CannotDownloadReasonFor(MamTorrentRecord record, FormatPreferenceLists preferenceLists)
    {
        var fileTypes = record.FileTypesRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fileTypes.Length == 0)
        {
            return "MAM lists no file types for this torrent, so Fetchshelf can't tell its format and won't download it.";
        }

        var mediaType = (MediaType)MamTorrentMapper.EffectiveMediaTypeId(record);
        if (!Enum.IsDefined(mediaType))
        {
            return null;
        }

        var wanted = preferenceLists.For(mediaType);
        return FormatRanker.Rank(wanted, fileTypes) == FormatRanker.NotPresent
            ? $"None of its file types ({string.Join(", ", fileTypes)}) is in your {FormatPreferenceLists.SettingNameFor(mediaType)} "
                + $"setting ({string.Join(", ", wanted)}), so Fetchshelf won't download it."
            : null;
    }

    /// <summary>
    /// Rates the torrent's file types against the format list for its media type, the list selection checks them
    /// against; null for a media type Fetchshelf doesn't know. See <see cref="Core.Metadata.FormatQuality"/>.
    /// </summary>
    public static FormatQualityRating? RateFormats(MamTorrentRecord record, FormatPreferenceLists preferenceLists)
    {
        var mediaType = (MediaType)MamTorrentMapper.EffectiveMediaTypeId(record);
        return Enum.IsDefined(mediaType)
            ? Core.Metadata.FormatQuality.Rate(
                preferenceLists.For(mediaType), record.FileTypesRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                mediaType, FormatPreferenceLists.SettingNameFor(mediaType))
            : null;
    }

    /// <summary>Rates the metadata MAM returned for the torrent; see <see cref="Core.Metadata.MetadataQuality"/>.</summary>
    public static MetadataQualityRating RateMetadata(MamTorrentRecord record) => Core.Metadata.MetadataQuality.Rate(new MetadataQualityFacts(
        HasAuthor: record.Authors.Values.Any(author => !string.IsNullOrWhiteSpace(author)),
        Description: record.Description,
        HasCover: !string.IsNullOrWhiteSpace(record.PosterType),
        HasGenres: record.GenreCategoryIds.Count > 0,
        IsAudio: MamTorrentMapper.EffectiveMediaTypeId(record) is 1 or 8,
        HasNarrator: record.Narrators.Values.Any(narrator => !string.IsNullOrWhiteSpace(narrator)),
        HasIdentifier: !string.IsNullOrWhiteSpace(record.Isbn),
        HasFileTypes: !string.IsNullOrWhiteSpace(record.FileTypesRaw)));

    /// <summary>Authors longer than this are cut for the Search table (anthologies can list dozens); the full list stays in <see cref="AuthorsDisplay"/>.</summary>
    public const int MaxAuthorsLength = 40;

    /// <summary><see cref="AuthorsDisplay"/> cut to <see cref="MaxAuthorsLength"/> characters and ending in "... more", or unchanged if it fits.</summary>
    public string AuthorsShortDisplay => ShortenAuthors(AuthorsDisplay);

    public bool AuthorsShortened => AuthorsDisplay.Length > MaxAuthorsLength;

    /// <summary>
    /// Keeps whole authors while they fit in <paramref name="maxLength"/> characters (not counting the ending);
    /// a first author that is itself longer than that is cut mid-name instead.
    /// </summary>
    public static string ShortenAuthors(string authors, int maxLength = MaxAuthorsLength)
    {
        if (authors.Length <= maxLength)
        {
            return authors;
        }

        var lastBoundary = -1;
        for (var comma = authors.IndexOf(", ", StringComparison.Ordinal); comma >= 0 && comma <= maxLength; comma = authors.IndexOf(", ", comma + 1, StringComparison.Ordinal))
        {
            lastBoundary = comma;
        }

        return lastBoundary > 0
            ? $"{authors[..lastBoundary]}, ... more"
            : $"{authors[..maxLength].TrimEnd()}... more";
    }

    private static string AuthorsDisplayOf(MamTorrentRecord record) =>
        string.Join(", ", record.Authors.OrderBy(kv => kv.Key).Select(kv => kv.Value));

    /// <summary>
    /// "ebook"/"audiobook"/"comic"/"other", for the browser-side Ebooks/Audiobooks/Comics filter buttons on the
    /// Search page to match against (see the page's <c>applyMediaFilter</c> script).
    /// </summary>
    private static string MediaTypeSlugOf(MamTorrentRecord record) => MamTorrentMapper.EffectiveMediaTypeId(record) switch
    {
        1 or 8 => "audiobook", // Audiobook, Periodical Audiobook
        2 or 7 => "ebook", // Ebook, Periodical Ebook
        5 or 6 => "comic", // Manga, Comic Book / Graphic Novel
        _ => "other",
    };

    /// <summary>Display name for the Media type column; "Unknown" for ids it does not list.</summary>
    private static string MediaTypeLabelOf(MamTorrentRecord record) => MamTorrentMapper.EffectiveMediaTypeId(record) switch
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
}
