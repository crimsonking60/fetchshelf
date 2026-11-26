using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Web.Pages;

/// <summary>A media type as the Search page's and the Following pages' Format columns name it.</summary>
public static class MediaTypeLabels
{
    public static string Of(MediaType mediaType) => mediaType switch
    {
        MediaType.Audiobook => "Audiobook",
        MediaType.Ebook => "Ebook",
        MediaType.Musicology => "Musicology",
        MediaType.Radio => "Radio",
        MediaType.Manga => "Manga",
        MediaType.ComicBook => "Comic Book / Graphic Novel",
        MediaType.PeriodicalEbook => "Periodical Ebook",
        MediaType.PeriodicalAudiobook => "Periodical Audiobook",
        _ => "Unknown",
    };

    /// <summary>A follow find's: its media type when recorded, else its side, as finds recorded before media types were kept have only that.</summary>
    public static string OfFind(string? mediaType, string mediaSide) =>
        Enum.TryParse<MediaType>(mediaType, out var known) && Enum.IsDefined(known) ? Of(known)
        : mediaSide == "Audio" ? "Audiobook" : "Ebook";
}
