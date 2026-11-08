namespace Fetchshelf.Core.Organizer;

/// <summary>Translates a path through a `path_mapping` table: qBittorrent's paths to Fetchshelf's, or Fetchshelf's
/// paths to Audiobookshelf's.</summary>
public static class PathMapper
{
    /// <summary>Longest-prefix match; replaces the matched prefix. Returns the path unchanged if no prefix matches.</summary>
    public static string Translate(IReadOnlyDictionary<string, string> pathMapping, string path)
    {
        string? bestPrefix = null;
        foreach (var prefix in pathMapping.Keys)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal)
                && (bestPrefix is null || prefix.Length > bestPrefix.Length))
            {
                bestPrefix = prefix;
            }
        }

        return bestPrefix is null ? path : pathMapping[bestPrefix] + path[bestPrefix.Length..];
    }
}
