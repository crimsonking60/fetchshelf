namespace Fetchshelf.Core.Metadata;

/// <summary>
/// MAM's own name for each genre (<see cref="GenreCategory"/>), as its search form shows it (checked against the form
/// on 2026-11-22), and the key a name in the
/// configuration is matched on: lower case, letters and digits only, so <c>"Action/Adventure"</c>,
/// <c>"action / adventure"</c> and <c>"actionadventure"</c> are the same genre. The enum's member names stay as they
/// are, since stored metadata holds them.
/// </summary>
public static class GenreLabels
{
    private static readonly Dictionary<GenreCategory, string> Labels = new()
    {
        [GenreCategory.Action] = "Action/Adventure",
        [GenreCategory.Art] = "Art/Photography",
        [GenreCategory.Biographical] = "Biographical",
        [GenreCategory.Business] = "Business/Money",
        [GenreCategory.Comedy] = "Comedy",
        [GenreCategory.CompleteEditionsMusic] = "Complete Editions - Music",
        [GenreCategory.Computer] = "Computer/Internet",
        [GenreCategory.Crafts] = "Crafts",
        [GenreCategory.Crime] = "Crime",
        [GenreCategory.Dramatization] = "Dramatization/Full Cast",
        [GenreCategory.Education] = "Education/Textbook",
        [GenreCategory.FactualNews] = "Factual News/Current Events",
        [GenreCategory.Fantasy] = "Fantasy",
        [GenreCategory.Food] = "Food/Wine",
        [GenreCategory.Guitar] = "Guitar/Bass Tabs",
        [GenreCategory.Health] = "Health/Fitness/Diet",
        [GenreCategory.Historical] = "Historical",
        [GenreCategory.Home] = "Home/Garden",
        [GenreCategory.Horror] = "Horror",
        [GenreCategory.Humor] = "Humor",
        [GenreCategory.IndividualSheet] = "Individual Sheet",
        [GenreCategory.Instructional] = "Instructional",
        [GenreCategory.Juvenile] = "Juvenile",
        [GenreCategory.Language] = "Language",
        [GenreCategory.Lgbt] = "LGBTQIA+",
        [GenreCategory.LickLibraryLtp] = "Erotica/Sexual Content",
        [GenreCategory.LickLibraryTechniques] = "Lick Library - Techniques/QL", // no longer on MAM's search form
        [GenreCategory.LiteraryClassics] = "Literary Classics",
        [GenreCategory.LitRpg] = "LitRPG",
        [GenreCategory.Math] = "Math",
        [GenreCategory.Medicine] = "Medicine/Psychology",
        [GenreCategory.Music] = "Music",
        [GenreCategory.MusicBook] = "Music Book",
        [GenreCategory.Mystery] = "Mystery",
        [GenreCategory.Nature] = "Nature",
        [GenreCategory.Paranormal] = "Paranormal",
        [GenreCategory.Philosophy] = "Philosophy",
        [GenreCategory.Poetry] = "Poetry",
        [GenreCategory.Politics] = "Politics/Sociology",
        [GenreCategory.Reference] = "Reference",
        [GenreCategory.Religion] = "Religion/Spirituality",
        [GenreCategory.Romance] = "Romance",
        [GenreCategory.Rpg] = "RPG",
        [GenreCategory.Science] = "Science",
        [GenreCategory.ScienceFiction] = "Science Fiction",
        [GenreCategory.SelfHelp] = "Self-Help",
        [GenreCategory.SheetCollection] = "Sheet Collection",
        [GenreCategory.SheetCollectionMp3] = "Accompanying music",
        [GenreCategory.Sports] = "Sports/Hobbies",
        [GenreCategory.Technology] = "Technology",
        [GenreCategory.Thriller] = "Thriller/Suspense",
        [GenreCategory.Travel] = "Travel",
        [GenreCategory.UrbanFantasy] = "Urban Fantasy",
        [GenreCategory.Western] = "Western",
        [GenreCategory.YoungAdult] = "Young Adult",
        [GenreCategory.Superheroes] = "Superheroes",
        [GenreCategory.LiteraryFiction] = "Literary Fiction",
        [GenreCategory.ProgressionFantasy] = "Progression Fantasy",
        [GenreCategory.ContemporaryFiction] = "Contemporary Fiction",
        [GenreCategory.DramaPlays] = "Plays/Drama",
        [GenreCategory.Occult] = "Occult / Metaphysical Practices",
        [GenreCategory.SliceOfLife] = "Slice of Life",
        [GenreCategory.Military] = "Military/War",
    };

    private static readonly Dictionary<string, GenreCategory> ByKey =
        Labels.ToDictionary(pair => KeyOf(pair.Value), pair => pair.Key, StringComparer.Ordinal);

    /// <summary>Every genre Fetchshelf knows, by its MAM id.</summary>
    public static IReadOnlyList<GenreCategory> All { get; } = [.. Labels.Keys.OrderBy(genre => (int)genre)];

    /// <summary>MAM's name for the genre; an id Fetchshelf doesn't know is "Unknown Category (id: N)".</summary>
    public static string Label(GenreCategory genre) =>
        Labels.TryGetValue(genre, out var label) ? label : $"Unknown Category (id: {(int)genre})";

    /// <summary>The genre a name from the configuration or a page means, compared on <see cref="KeyOf"/>.</summary>
    public static bool TryParse(string? name, out GenreCategory genre) => ByKey.TryGetValue(KeyOf(name), out genre);

    /// <summary>A name's letters and digits, in lower case: what names are compared on.</summary>
    public static string KeyOf(string? name) =>
        string.Concat((name ?? string.Empty).Where(char.IsLetterOrDigit)).ToLowerInvariant();

    /// <summary>The fiction dimension from <c>"fiction"</c> or <c>"nonfiction"</c>, compared the same way (so "Non-fiction" too).</summary>
    public static bool TryParseFiction(string? name, out FictionCategory fiction)
    {
        switch (KeyOf(name))
        {
            case "fiction":
                fiction = FictionCategory.Fiction;
                return true;
            case "nonfiction":
                fiction = FictionCategory.Nonfiction;
                return true;
            default:
                fiction = default;
                return false;
        }
    }

    /// <summary>"Fiction" or "Nonfiction", as MAM names them.</summary>
    public static string FictionLabel(FictionCategory fiction) => fiction == FictionCategory.Nonfiction ? "Nonfiction" : "Fiction";
}
