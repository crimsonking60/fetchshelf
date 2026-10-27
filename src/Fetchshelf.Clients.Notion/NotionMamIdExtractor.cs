namespace Fetchshelf.Clients.Notion;

/// <summary>
/// Extracts MaM ids from a Notion row's configured URL properties, Fetchshelf-spec.md §7.8: "the trailing
/// number of each URL is taken as MaM id."
/// </summary>
public static class NotionMamIdExtractor
{
    /// <summary>
    /// Returns the MaM id parsed from each of <paramref name="mamFields"/> that names a URL property
    /// with a parseable trailing path segment, skipping fields that are absent, not URL-typed, have
    /// no URL value, or whose trailing segment is not an integer.
    /// </summary>
    public static IReadOnlyList<long> ExtractMamIds(NotionRow row, IReadOnlyList<string> mamFields)
    {
        var mamIds = new List<long>();
        foreach (var field in mamFields)
        {
            if (row.Properties.TryGetValue(field, out var property)
                && property is NotionUrlProperty { Url: { } url }
                && long.TryParse(url.Split('/')[^1], out var mamId))
            {
                mamIds.Add(mamId);
            }
        }

        return mamIds;
    }
}
