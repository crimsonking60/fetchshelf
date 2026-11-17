using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web.Pages.Config;

/// <summary>
/// A shared torrent filter as the Config page shows it: one line per part that is set, so a `[[tag]]` rule shows every
/// condition a torrent must meet, not only its categories. Parts that aren't set restrict nothing and are left out.
/// </summary>
public static class FilterDescription
{
    public static IReadOnlyList<string> Describe(SharedFilterConfig filter)
    {
        var lines = new List<string> { Categories(filter.Categories) };
        if (filter.MediaType.Count > 0)
        {
            lines.Add($"Media types: {string.Join(", ", filter.MediaType)}");
        }

        if (filter.Languages is { } languages)
        {
            lines.Add(languages.Count == 0 ? "Languages: any" : $"Languages: {string.Join(", ", languages)}");
        }

        if (filter.Flags.Count > 0)
        {
            lines.Add("Flags: " + string.Join(", ", filter.Flags.OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => $"{f.Key} {(f.Value ? "required" : "forbidden")}")));
        }

        AddRange(lines, "Size", filter.MinSize, filter.MaxSize);
        AddRange(lines, "Uploaded", filter.UploadedAfter, filter.UploadedBefore, from: "after", to: "before");
        AddRange(lines, "Seeders", filter.MinSeeders?.ToString(System.Globalization.CultureInfo.InvariantCulture), filter.MaxSeeders?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddRange(lines, "Leechers", filter.MinLeechers?.ToString(System.Globalization.CultureInfo.InvariantCulture), filter.MaxLeechers?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddRange(lines, "Snatched", filter.MinSnatched?.ToString(System.Globalization.CultureInfo.InvariantCulture), filter.MaxSnatched?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (filter.MaxAuthors is { } maxAuthors)
        {
            lines.Add($"Authors: at most {maxAuthors}");
        }

        if (filter.ExcludeUploader.Count > 0)
        {
            lines.Add($"Not uploaded by: {string.Join(", ", filter.ExcludeUploader)}");
        }

        return lines;
    }

    /// <summary>
    /// The main types a torrent may be, with their categories: "Categories: audio (all), ebook (Fantasy, Science
    /// Fiction)". Musicology and radio are left out unless allowed, as they are by default.
    /// </summary>
    private static string Categories(CategoriesConfig categories)
    {
        var allowed = new[] { ("audio", categories.Audio), ("ebook", categories.Ebook), ("musicology", categories.Musicology), ("radio", categories.Radio) }
            .Where(c => c.Item2.Names is not { Count: 0 })
            .Select(c => $"{c.Item1} ({(c.Item2.Names is { } names ? string.Join(", ", names) : "all")})")
            .ToList();
        return allowed.Count == 0 ? "Categories: none, so it matches nothing" : $"Categories: {string.Join(", ", allowed)}";
    }

    private static void AddRange(List<string> lines, string label, string? min, string? max, string from = "at least", string to = "at most")
    {
        var parts = new[] { min is null ? null : $"{from} {min}", max is null ? null : $"{to} {max}" }.OfType<string>().ToList();
        if (parts.Count > 0)
        {
            lines.Add($"{label}: {string.Join(", ", parts)}");
        }
    }
}
