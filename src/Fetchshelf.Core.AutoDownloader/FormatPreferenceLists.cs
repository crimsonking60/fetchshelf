using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>A library's own format lists, which replace the global ones for the copies linked into it; null keeps the global one.</summary>
public sealed record LibraryFormatLists(IReadOnlyList<string>? AudioTypes, IReadOnlyList<string>? EbookTypes);

/// <summary>The four per-media-type format preference lists, and the libraries' own lists where they set them.</summary>
public sealed record FormatPreferenceLists
{
    public required IReadOnlyList<string> AudioTypes { get; init; }

    public required IReadOnlyList<string> EbookTypes { get; init; }

    public required IReadOnlyList<string> MusicTypes { get; init; }

    public required IReadOnlyList<string> RadioTypes { get; init; }

    /// <summary>
    /// The `audio_types`/`ebook_types` of each `[[library]]` that sets them, by library name. The Link job chooses a
    /// copy's format with these, so a copy in the library is ranked with them too.
    /// </summary>
    public IReadOnlyDictionary<string, LibraryFormatLists> Libraries { get; init; } = new Dictionary<string, LibraryFormatLists>();

    public IReadOnlyList<string> For(MediaType mediaType) => mediaType switch
    {
        MediaType.Audiobook or MediaType.PeriodicalAudiobook => AudioTypes,
        MediaType.Ebook or MediaType.ComicBook or MediaType.Manga or MediaType.PeriodicalEbook => EbookTypes,
        MediaType.Musicology => MusicTypes,
        MediaType.Radio => RadioTypes,
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType)),
    };

    /// <summary>The configuration setting that holds <see cref="For"/>'s list, for messages that tell the user what to change.</summary>
    public static string SettingNameFor(MediaType mediaType) => mediaType switch
    {
        MediaType.Audiobook or MediaType.PeriodicalAudiobook => "audio_types",
        MediaType.Ebook or MediaType.ComicBook or MediaType.Manga or MediaType.PeriodicalEbook => "ebook_types",
        MediaType.Musicology => "music_types",
        MediaType.Radio => "radio_types",
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType)),
    };

    /// <summary>
    /// The list that picks the audio-side file type when linking: music and radio have their own lists, every other
    /// media type uses <see cref="AudioTypes"/>. The ebook side always uses <see cref="EbookTypes"/>.
    /// </summary>
    public IReadOnlyList<string> AudioSideFor(MediaType mediaType) => mediaType switch
    {
        MediaType.Musicology => MusicTypes,
        MediaType.Radio => RadioTypes,
        _ => AudioTypes,
    };
}
