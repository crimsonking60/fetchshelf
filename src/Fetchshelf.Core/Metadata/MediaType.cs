namespace Fetchshelf.Core.Metadata;

/// <summary>Tracker media types, Fetchshelf-spec.md §12.1.</summary>
public enum MediaType
{
    Audiobook = 1,
    Ebook = 2,
    Musicology = 3,
    Radio = 4,
    Manga = 5,
    ComicBook = 6,
    PeriodicalEbook = 7,
    PeriodicalAudiobook = 8,
}

/// <summary>Fiction / non-fiction main category, Fetchshelf-spec.md §12.1.</summary>
public enum FictionCategory
{
    Fiction = 1,
    Nonfiction = 2,
}
