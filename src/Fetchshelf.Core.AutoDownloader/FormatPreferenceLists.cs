using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>The four per-media-type format preference lists, Fetchshelf-spec.md §4.1, §7.6.</summary>
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
}
