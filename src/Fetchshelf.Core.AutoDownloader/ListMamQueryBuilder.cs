using System.Text.RegularExpressions;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// Builds a MAM title and author search text from a list item: <c>@title &lt;title&gt; @author ("A"|"B")</c>.
/// Quotes, slashes and bracketed suffixes are removed from the title, "and"/"&amp;" matches either, and for a
/// title with a colon the part before it is tried as well. An initial in an author's name matches with or without
/// its full stop: lists and MAM don't agree on "Sarah J. Maas" or "Sarah J Maas".
/// </summary>
public static partial class ListMamQueryBuilder
{
    [GeneratedRegex(@"['`/?]|\s+[\(\[][^\)\]]+[\)\]]")]
    private static partial Regex BadCharactersPattern();

    [GeneratedRegex(@"&|\band\b", RegexOptions.IgnoreCase)]
    private static partial Regex AndPattern();

    [GeneratedRegex(@"(?<=^|\s)(\p{Lu})\.?(?=\s|$)")]
    private static partial Regex InitialPattern();

    [GeneratedRegex(@"[^\p{L}\p{N}\s.,]+")]
    private static partial Regex NotPlainPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesPattern();

    /// <summary>
    /// A name as plain words for MAM's search, with everything MAM's search syntax could read as an operator taken out:
    /// quotes, slashes, <c>?</c>, <c>*</c>, <c>|</c>, <c>-</c>, <c>@</c>, brackets. "Ender's Saga" becomes "Ender s Saga",
    /// "Spider-Man" "Spider Man". Letters and digits of every script stay.
    /// </summary>
    public static string PlainWords(string name) =>
        SpacesPattern().Replace(NotPlainPattern().Replace(name, " "), " ").Trim();

    public static string Build(string title, IReadOnlyList<string> authors)
    {
        var titleQuery = BuildTitleQuery(title);
        var authorQuery = string.Join('|', authors.Select(a => $"\"{BuildAuthorQuery(a)}\""));
        return $"@title {titleQuery} @author ({authorQuery})";
    }

    /// <summary>One author's name with each single-letter initial written as <c>(J|J.)</c>.</summary>
    public static string BuildAuthorQuery(string author) =>
        InitialPattern().Replace(author, match => $"({match.Groups[1].Value}|{match.Groups[1].Value}.)");

    /// <summary>The title portion alone, exposed for testing the title-specific transformations.</summary>
    public static string BuildTitleQuery(string title)
    {
        var escaped = title.Replace("*", "\"*\"");
        var cleaned = BadCharactersPattern().Replace(escaped, " ");
        var withAndExpanded = AndPattern().Replace(cleaned, "(and|&)");

        var colonIndex = withAndExpanded.IndexOf(':');
        if (colonIndex < 0)
        {
            return withAndExpanded;
        }

        var primaryTitle = withAndExpanded[..colonIndex];
        return $"(\"{primaryTitle}\"|\"{withAndExpanded}\")";
    }
}
