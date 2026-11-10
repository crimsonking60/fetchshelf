namespace Fetchshelf.Core.Organizer;

/// <summary>Translates a path through a `path_mapping` table: qBittorrent's paths to Fetchshelf's, or Fetchshelf's
/// paths to Audiobookshelf's.</summary>
public static class PathMapper
{
    /// <summary>
    /// The longest key that <paramref name="path"/> is in, by whole folder names (<see cref="PathPrefix"/>), is replaced
    /// by its value. A trailing slash on a key or value makes no difference. Returns the path unchanged if no key holds it.
    /// </summary>
    public static string Translate(IReadOnlyDictionary<string, string> pathMapping, string path)
    {
        string? bestPrefix = null;
        foreach (var prefix in pathMapping.Keys)
        {
            if (PathPrefix.IsSameOrUnder(path, prefix)
                && (bestPrefix is null || prefix.Length > bestPrefix.Length))
            {
                bestPrefix = prefix;
            }
        }

        if (bestPrefix is null)
        {
            return path;
        }

        // Joined at the folder boundary, so a trailing slash on the key or the value, or on neither, gives one separator.
        var rest = path[bestPrefix.TrimEnd('/', '\\').Length..];
        var target = pathMapping[bestPrefix];
        return rest.Length == 0 ? target : target.TrimEnd('/', '\\') + rest;
    }
}
