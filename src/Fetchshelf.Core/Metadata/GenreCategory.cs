namespace Fetchshelf.Core.Metadata;

/// <summary>
/// Tracker genre categories (newer taxonomy). Numeric ids match the tracker's
/// real category ids (confirmed against the original Rust implementation's reference data,
/// `mlm_db::Category`), which include two entries beyond's list: "Sheet
/// Collection MP3" (48, between Sheet Collection and Sports) and "Occult / Metaphysical
/// Practices"/"Slice of Life" (61/62).
/// </summary>
public enum GenreCategory
{
    Action = 1,
    Art = 2,
    Biographical = 3,
    Business = 4,
    Comedy = 5,
    CompleteEditionsMusic = 6,
    Computer = 7,
    Crafts = 8,
    Crime = 9,
    Dramatization = 10,
    Education = 11,
    FactualNews = 12,
    Fantasy = 13,
    Food = 14,
    Guitar = 15,
    Health = 16,
    Historical = 17,
    Home = 18,
    Horror = 19,
    Humor = 20,
    IndividualSheet = 21,
    Instructional = 22,
    Juvenile = 23,
    Language = 24,
    Lgbt = 25,
    LickLibraryLtp = 26,
    LickLibraryTechniques = 27,
    LiteraryClassics = 28,
    LitRpg = 29,
    Math = 30,
    Medicine = 31,
    Music = 32,
    MusicBook = 33,
    Mystery = 34,
    Nature = 35,
    Paranormal = 36,
    Philosophy = 37,
    Poetry = 38,
    Politics = 39,
    Reference = 40,
    Religion = 41,
    Romance = 42,
    Rpg = 43,
    Science = 44,
    ScienceFiction = 45,
    SelfHelp = 46,
    SheetCollection = 47,
    SheetCollectionMp3 = 48,
    Sports = 49,
    Technology = 50,
    Thriller = 51,
    Travel = 52,
    UrbanFantasy = 53,
    Western = 54,
    YoungAdult = 55,
    Superheroes = 56,
    LiteraryFiction = 57,
    ProgressionFantasy = 58,
    ContemporaryFiction = 59,
    DramaPlays = 60,
    Occult = 61,
    SliceOfLife = 62,
}

/// <summary>Display-name exceptions for genre categories. Ids the tracker added after our list was written display as "Unknown Category (id: N)".</summary>
public static class GenreCategoryDisplay
{
    public static string DisplayName(GenreCategory category) => category switch
    {
        GenreCategory.LiteraryFiction => "General Fiction",
        GenreCategory.ContemporaryFiction => "Contemporary",
        _ when !Enum.IsDefined(category) => $"Unknown Category (id: {(int)category})",
        _ => SplitPascalCase(category.ToString()),
    };

    private static string SplitPascalCase(string value) =>
        string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
}
