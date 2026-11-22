using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Pages;

/// <summary>
/// The Genre and Fiction filters of the Torrents and Books pages, with `genres = true`: a genre by MAM's id or
/// <see cref="NoGenre"/>, and the main category or <see cref="Neither"/>. On the Torrents page they run in SQL, on
/// <c>TorrentGenres</c> and the <c>FictionCategory</c> column (<see cref="Apply"/>); on the Books page on the catalog
/// (<see cref="Matches"/>).
/// </summary>
public static class GenreFilters
{
    /// <summary>The Genre filter's value for torrents without any genre.</summary>
    public const string NoGenre = "none";

    /// <summary>The Fiction filter's value for torrents MAM gave neither main category.</summary>
    public const string Neither = "Neither";

    /// <summary>The Fiction filter's choices, <see cref="Neither"/> included; see <see cref="FictionChoices"/>.</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> FictionOptions =
    [
        (nameof(FictionCategory.Fiction), "Fiction"),
        (nameof(FictionCategory.Nonfiction), "Nonfiction"),
        (Neither, "Neither"),
    ];

    /// <summary>
    /// The Fiction filter's choices shown: <see cref="Neither"/> only when some book has neither main category, or it is
    /// the value in use. MAM gives nearly every torrent one, so the choice would usually find nothing.
    /// </summary>
    public static IReadOnlyList<(string Value, string Label)> FictionChoices(bool anyWithNeither, string? inUse) =>
        anyWithNeither || inUse == Neither ? FictionOptions : FictionOptions.Where(option => option.Value != Neither).ToList();

    /// <summary>A genre's filter value: its MAM id.</summary>
    public static string ValueOf(GenreCategory genre) => ((int)genre).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The genre a filter value names, if it names one.</summary>
    public static GenreCategory? Parse(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) ? (GenreCategory)id : null;

    /// <summary>The Genre filter's choices: <paramref name="present"/> by MAM's names, A-Z, then No genre; the value in use stays a choice.</summary>
    public static IReadOnlyList<(string Value, string Label)> Options(IEnumerable<GenreCategory> present, string? inUse)
    {
        var genres = present.ToHashSet();
        if (Parse(inUse) is { } current)
        {
            genres.Add(current);
        }

        return
        [
            .. genres.Select(genre => (ValueOf(genre), GenreLabels.Label(genre))).OrderBy(option => option.Item2, StringComparer.OrdinalIgnoreCase),
            (NoGenre, "No genre"),
        ];
    }

    /// <summary>What a Genre value is shown as: MAM's name, "No genre", or the value itself when it is neither.</summary>
    public static string? LabelOf(string? value) =>
        string.IsNullOrEmpty(value) ? null : value == NoGenre ? "No genre" : Parse(value) is { } genre ? GenreLabels.Label(genre) : value;

    /// <summary>What a Fiction value is shown as.</summary>
    public static string? FictionLabelOf(string? value) =>
        string.IsNullOrEmpty(value) ? null : FictionOptions.FirstOrDefault(option => option.Value == value).Label ?? value;

    /// <summary>Whether a book with these genres and main category passes the two filters; an empty value passes every book.</summary>
    public static bool Matches(IReadOnlySet<GenreCategory> bookGenres, FictionCategory? bookFiction, string? genre, string? fiction)
    {
        var genreMatches = string.IsNullOrEmpty(genre)
            || (genre == NoGenre ? bookGenres.Count == 0 : Parse(genre) is { } wanted && bookGenres.Contains(wanted));
        var fictionMatches = string.IsNullOrEmpty(fiction)
            || (fiction == Neither ? bookFiction is null : bookFiction?.ToString() == fiction);
        return genreMatches && fictionMatches;
    }

    /// <summary>The torrents the two filters let through; an empty value lets every torrent through, and an unknown one none.</summary>
    public static IQueryable<TorrentEntity> Apply(
        IQueryable<TorrentEntity> torrents, IQueryable<TorrentGenreEntity> genres, string? genre, string? fiction)
    {
        // As "id IN (SELECT ...)", which SQLite answers from the genre index; a correlated EXISTS per torrent was a
        // hundred times slower at 10,000 torrents.
        if (genre == NoGenre)
        {
            var withAny = genres.Select(g => g.TorrentId);
            torrents = torrents.Where(t => !withAny.Contains(t.Id));
        }
        else if (!string.IsNullOrEmpty(genre))
        {
            var id = Parse(genre) ?? (GenreCategory)(-1);
            var withGenre = genres.Where(g => g.Genre == id).Select(g => g.TorrentId);
            torrents = torrents.Where(t => withGenre.Contains(t.Id));
        }

        if (fiction == Neither)
        {
            torrents = torrents.Where(t => t.FictionCategory == null);
        }
        else if (!string.IsNullOrEmpty(fiction))
        {
            torrents = torrents.Where(t => t.FictionCategory == fiction);
        }

        return torrents;
    }
}
