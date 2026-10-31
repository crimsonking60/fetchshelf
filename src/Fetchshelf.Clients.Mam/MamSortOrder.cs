namespace Fetchshelf.Clients.Mam;

/// <summary>Search result sort order. MaM sorts newest first by default.</summary>
public enum MamSortOrder
{
    NewestFirst,
    OldestFirst,
    Random,
    LowestSeeders,
    LowestSnatches,
}

internal static class MamSortOrderExtensions
{
    /// <summary>Empty string means "default" (newest first), which the real API treats as omitting `sortType` entirely.</summary>
    public static string ToWireValue(this MamSortOrder sortOrder) => sortOrder switch
    {
        MamSortOrder.NewestFirst => "",
        MamSortOrder.OldestFirst => "dateAsc",
        MamSortOrder.Random => "random",
        MamSortOrder.LowestSeeders => "seedersAsc",
        MamSortOrder.LowestSnatches => "snatchedAsc",
        _ => throw new ArgumentOutOfRangeException(nameof(sortOrder)),
    };
}
