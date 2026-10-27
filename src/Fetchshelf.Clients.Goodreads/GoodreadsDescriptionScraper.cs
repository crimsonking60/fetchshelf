using AngleSharp.Html.Parser;

namespace Fetchshelf.Clients.Goodreads;

/// <summary>
/// Scrapes a Goodreads feed item's embedded HTML <c>description</c> field for details not present as
/// their own feed fields, Fetchshelf-spec.md §7.8 step 1 (cover, authors, series are extracted from the book
/// page's markup the feed embeds).
/// </summary>
public static class GoodreadsDescriptionScraper
{
    private static readonly HtmlParser Parser = new();

    /// <summary>The book's cover image URL from <c>.cover img[src]</c>, or empty if absent.</summary>
    public static string ExtractCoverUrl(string descriptionHtml)
    {
        var document = Parser.ParseDocument(descriptionHtml);
        var image = document.QuerySelector(".cover img");
        return image?.GetAttribute("src") ?? string.Empty;
    }

    /// <summary>
    /// The book's Goodreads page URL, from a user-shelf-style absolute link
    /// (<c>a[href^="https://www.goodreads.com/book/show/"]</c>) or a group-shelf-style relative link
    /// (<c>a[href^="/book/show/"]</c>, resolved to an absolute URL), or null if neither is present.
    /// </summary>
    public static string? ExtractBookUrl(string descriptionHtml)
    {
        var document = Parser.ParseDocument(descriptionHtml);

        var absoluteLink = document.QuerySelectorAll("a").FirstOrDefault(
            a => a.GetAttribute("href")?.StartsWith("https://www.goodreads.com/book/show/", StringComparison.Ordinal) == true);
        if (absoluteLink is not null)
        {
            return absoluteLink.GetAttribute("href");
        }

        var relativeLink = document.QuerySelectorAll("a").FirstOrDefault(
            a => a.GetAttribute("href")?.StartsWith("/book/show/", StringComparison.Ordinal) == true);
        return relativeLink is null ? null : $"https://www.goodreads.com{relativeLink.GetAttribute("href")}";
    }

    /// <summary>Author name(s) from <c>[itemprop="author"] [itemprop="name"]</c>, periods replaced with spaces to match the reference's initials normalization.</summary>
    public static IReadOnlyList<string> ExtractAuthors(string descriptionHtml)
    {
        var document = Parser.ParseDocument(descriptionHtml);
        return document.QuerySelectorAll("[itemprop=\"author\"] [itemprop=\"name\"]")
            .Select(e => e.TextContent.Replace('.', ' '))
            .ToList();
    }
}
