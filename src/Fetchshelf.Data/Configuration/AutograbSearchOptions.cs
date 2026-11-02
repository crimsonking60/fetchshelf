using Fetchshelf.Clients.Mam;

namespace Fetchshelf.Data.Configuration;

/// <summary>
/// The accepted values of an `[[autograb]]` entry's `sort_by` and `search_in`, shared by
/// `ConfigLoader` (which rejects unknown values at startup) and the search translator.
/// </summary>
public static class AutograbSearchOptions
{
    public const string SortByNames = "newest_first, oldest_first, random, low_seeders, low_snatches";

    public const string SearchInNames = "author, description, filenames, file_types, narrator, series, tags, title";

    /// <summary>Unset means newest first. Names are case-insensitive.</summary>
    public static bool TryParseSortBy(string? sortBy, out MamSortOrder sortOrder)
    {
        MamSortOrder? parsed = sortBy?.ToLowerInvariant() switch
        {
            null or "newest_first" => MamSortOrder.NewestFirst,
            "oldest_first" => MamSortOrder.OldestFirst,
            "random" => MamSortOrder.Random,
            "low_seeders" => MamSortOrder.LowestSeeders,
            "low_snatches" => MamSortOrder.LowestSnatches,
            _ => null,
        };
        sortOrder = parsed ?? default;
        return parsed is not null;
    }

    /// <summary>Names are case-insensitive, and underscores are ignored (`file_types` or `filetypes`).</summary>
    public static bool TryParseSearchField(string name, out MamSearchField field)
    {
        var normalized = name.Replace("_", "", StringComparison.Ordinal);

        // Enum.TryParse alone would also accept numbers such as "3".
        if (Enum.GetNames<MamSearchField>().Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            field = Enum.Parse<MamSearchField>(normalized, ignoreCase: true);
            return true;
        }

        field = default;
        return false;
    }
}
