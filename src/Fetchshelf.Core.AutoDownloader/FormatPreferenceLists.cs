using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>A library's own format lists, which replace the global ones for the copies linked into it; null keeps the global one.</summary>
public sealed record LibraryFormatLists(IReadOnlyList<string>? AudioTypes, IReadOnlyList<string>? EbookTypes, IReadOnlyList<string>? ComicTypes = null);

/// <summary>The per-media-type format preference lists, and the libraries' own lists where they set them.</summary>
public sealed record FormatPreferenceLists
{
    public required IReadOnlyList<string> AudioTypes { get; init; }

    public required IReadOnlyList<string> EbookTypes { get; init; }

    public required IReadOnlyList<string> MusicTypes { get; init; }

    public required IReadOnlyList<string> RadioTypes { get; init; }

    /// <summary>`comic_types`, for comic books and manga; null when unset, and then they use <see cref="EbookTypes"/>.</summary>
    public IReadOnlyList<string>? ComicTypes { get; init; }

    /// <summary>
    /// The `audio_types`/`ebook_types` of each `[[library]]` that sets them, by library name. The Link job chooses a
    /// copy's format with these, so a copy in the library is ranked with them too.
    /// </summary>
    public IReadOnlyDictionary<string, LibraryFormatLists> Libraries { get; init; } = new Dictionary<string, LibraryFormatLists>();

    /// <summary>Comic books and manga, which <c>comic_types</c> is for.</summary>
    public static bool IsComic(MediaType mediaType) => mediaType is MediaType.ComicBook or MediaType.Manga;

    public IReadOnlyList<string> For(MediaType mediaType) => mediaType switch
    {
        MediaType.Audiobook or MediaType.PeriodicalAudiobook => AudioTypes,
        MediaType.ComicBook or MediaType.Manga => ComicTypes ?? EbookTypes,
        MediaType.Ebook or MediaType.PeriodicalEbook => EbookTypes,
        MediaType.Musicology => MusicTypes,
        MediaType.Radio => RadioTypes,
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType)),
    };

    /// <summary>The configuration setting that holds <see cref="For"/>'s list, for messages that tell the user what to change.</summary>
    public string SettingNameFor(MediaType mediaType) => mediaType switch
    {
        MediaType.Audiobook or MediaType.PeriodicalAudiobook => "audio_types",
        MediaType.ComicBook or MediaType.Manga => ComicTypes is null ? "ebook_types" : "comic_types",
        MediaType.Ebook or MediaType.PeriodicalEbook => "ebook_types",
        MediaType.Musicology => "music_types",
        MediaType.Radio => "radio_types",
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType)),
    };

    /// <summary>
    /// The list that picks the ebook-side file type when linking, given a library's own lists: for a comic or manga the
    /// library's `comic_types`, else the global one, else the library's `ebook_types`, else the global one; for any
    /// other media type the library's `ebook_types`, else the global one.
    /// </summary>
    public IReadOnlyList<string> EbookSideFor(
        MediaType mediaType, IReadOnlyList<string>? libraryEbookTypes = null, IReadOnlyList<string>? libraryComicTypes = null) =>
        IsComic(mediaType)
            ? libraryComicTypes ?? ComicTypes ?? libraryEbookTypes ?? EbookTypes
            : libraryEbookTypes ?? EbookTypes;

    /// <summary>
    /// The list that picks the audio-side file type when linking: music and radio have their own lists, every other
    /// media type uses <see cref="AudioTypes"/>. The ebook side is <see cref="EbookSideFor"/>.
    /// </summary>
    public IReadOnlyList<string> AudioSideFor(MediaType mediaType) => mediaType switch
    {
        MediaType.Musicology => MusicTypes,
        MediaType.Radio => RadioTypes,
        _ => AudioTypes,
    };
}
