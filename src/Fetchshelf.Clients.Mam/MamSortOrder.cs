namespace Fetchshelf.Clients.Mam;

/// <summary>Search result sort order.</summary>
public enum MamSortOrder
{
    /// <summary>
    /// MAM's own order, sent as no `sortType`: newest first without search text, by relevance with it, and by bookmark
    /// date for bookmarks.
    /// </summary>
    NewestFirst,
    OldestFirst,
    Random,
    LowestSeeders,
    LowestSnatches,

    /// <summary>Newest first even with search text (`dateDesc`), unlike <see cref="NewestFirst"/>.</summary>
    NewestByDate,
    MostSeeders,
    MostSnatches,
    LargestFirst,
    TitleAsc,
}

internal static class MamSortOrderExtensions
{
    /// <summary>Empty string means MAM's own order, which the real API treats as omitting `sortType` entirely.</summary>
    public static string ToWireValue(this MamSortOrder sortOrder) => sortOrder switch
    {
        MamSortOrder.NewestFirst => "",
        MamSortOrder.OldestFirst => "dateAsc",
        MamSortOrder.Random => "random",
        MamSortOrder.LowestSeeders => "seedersAsc",
        MamSortOrder.LowestSnatches => "snatchedAsc",
        MamSortOrder.NewestByDate => "dateDesc",
        MamSortOrder.MostSeeders => "seedersDesc",
        MamSortOrder.MostSnatches => "snatchedDesc",
        MamSortOrder.LargestFirst => "sizeDesc",
        MamSortOrder.TitleAsc => "titleAsc",
        _ => throw new ArgumentOutOfRangeException(nameof(sortOrder)),
    };
}
