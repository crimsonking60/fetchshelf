namespace Fetchshelf.Web.Pages.Torrents;

/// <summary>
/// The Torrents list's search, filters and sort, so a link that changes one of them (a sort heading, a filter value)
/// keeps the others. Paging is left out: any such change starts again at page 1. <see cref="Author"/>,
/// <see cref="Narrator"/> and <see cref="Series"/> are name keys (<c>PersonNameKey</c>, <c>SeriesNameKey</c>), not names.
/// </summary>
public sealed record TorrentListQuery(
    string? Q, string? Status, string? Library, string? QbCategory, string? Problem, string? Language, string? MediaType, string Sort, string Dir, int PageSize,
    string? Author = null, string? Narrator = null, string? Series = null, string? Seeding = null, string? CollectionBooks = null,
    string? Genre = null, string? Fiction = null)
{
    /// <summary>The query as route values for a link, leaving out empty values.</summary>
    public Dictionary<string, string> ToRouteValues()
    {
        var values = new Dictionary<string, string>
        {
            [nameof(Q)] = Q ?? string.Empty,
            [nameof(Status)] = Status ?? string.Empty,
            [nameof(Library)] = Library ?? string.Empty,
            [nameof(QbCategory)] = QbCategory ?? string.Empty,
            [nameof(Problem)] = Problem ?? string.Empty,
            [nameof(Language)] = Language ?? string.Empty,
            [nameof(MediaType)] = MediaType ?? string.Empty,
            [nameof(Author)] = Author ?? string.Empty,
            [nameof(Narrator)] = Narrator ?? string.Empty,
            [nameof(Series)] = Series ?? string.Empty,
            [nameof(Seeding)] = Seeding ?? string.Empty,
            [nameof(CollectionBooks)] = CollectionBooks ?? string.Empty,
            [nameof(Genre)] = Genre ?? string.Empty,
            [nameof(Fiction)] = Fiction ?? string.Empty,
            [nameof(Sort)] = Sort,
            [nameof(Dir)] = Dir,
            [nameof(PageSize)] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return values.Where(pair => !string.IsNullOrEmpty(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>Whether any filter is set, not counting the search text.</summary>
    public bool HasFilters =>
        !string.IsNullOrEmpty(Status) || !string.IsNullOrEmpty(Library) || !string.IsNullOrEmpty(QbCategory) || !string.IsNullOrEmpty(Problem)
        || !string.IsNullOrEmpty(Language) || !string.IsNullOrEmpty(MediaType)
        || !string.IsNullOrEmpty(Author) || !string.IsNullOrEmpty(Narrator) || !string.IsNullOrEmpty(Series)
        || !string.IsNullOrEmpty(Seeding) || !string.IsNullOrEmpty(CollectionBooks)
        || !string.IsNullOrEmpty(Genre) || !string.IsNullOrEmpty(Fiction);

    /// <summary>The query with every filter cleared, keeping the search text, sort and page size.</summary>
    public TorrentListQuery WithoutFilters() =>
        this with
        {
            Status = null,
            Library = null,
            QbCategory = null,
            Problem = null,
            Language = null,
            MediaType = null,
            Author = null,
            Narrator = null,
            Series = null,
            Seeding = null,
            CollectionBooks = null,
            Genre = null,
            Fiction = null,
        };

    /// <summary>The sort as a state, for the column headings.</summary>
    public SortState SortState => SortState.From(Sort, Dir);
}
