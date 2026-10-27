namespace Fetchshelf.Clients.Goodreads;

/// <summary>Derives a Goodreads list's stable id from its shelf RSS feed URL, Fetchshelf-spec.md §7.8: "List id is <c>&lt;goodreads-user-id&gt;:&lt;shelf&gt;</c>."</summary>
public static class GoodreadsListId
{
    public static string Derive(string feedUrl)
    {
        var uri = new Uri(feedUrl);
        var userId = uri.Segments.Length == 0 ? string.Empty : uri.Segments[^1].TrimEnd('/');
        var shelf = ParseQueryParameter(uri.Query, "shelf")
            ?? throw new FormatException($"Goodreads feed URL is missing the 'shelf' query parameter: {feedUrl}");
        return $"{userId}:{shelf}";
    }

    private static string? ParseQueryParameter(string query, string key)
    {
        var trimmed = query.TrimStart('?');
        if (trimmed.Length == 0)
        {
            return null;
        }

        foreach (var pair in trimmed.Split('&'))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(pair[..separatorIndex]);
            if (string.Equals(name, key, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(pair[(separatorIndex + 1)..]);
            }
        }

        return null;
    }
}
