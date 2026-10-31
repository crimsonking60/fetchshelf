using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>The four per-media-type format preference lists.</summary>
public sealed record FormatPreferenceLists
{
    public required IReadOnlyList<string> AudioTypes { get; init; }

    public required IReadOnlyList<string> EbookTypes { get; init; }

    public required IReadOnlyList<string> MusicTypes { get; init; }

    public required IReadOnlyList<string> RadioTypes { get; init; }

    public IReadOnlyList<string> For(MediaType mediaType) => mediaType switch
    {
        MediaType.Audiobook or MediaType.PeriodicalAudiobook => AudioTypes,
        MediaType.Ebook or MediaType.ComicBook or MediaType.Manga or MediaType.PeriodicalEbook => EbookTypes,
        MediaType.Musicology => MusicTypes,
        MediaType.Radio => RadioTypes,
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
