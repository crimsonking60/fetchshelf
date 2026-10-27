using System.Text.RegularExpressions;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// Builds a MaM title+author search query text from a list item, Fetchshelf-spec.md §7.8 step 3: "search MaM
/// with <c>@title &lt;title&gt; @author ("A"|"B")</c>. Quotes, slashes and bracketed suffixes are
/// stripped from the title. "and"/"&amp;" is expanded to <c>(and|&amp;)</c>. The part before a colon
/// is also tried alone."
/// </summary>
public static partial class ListMamQueryBuilder
{
    [GeneratedRegex(@"['`/?]|\s+[\(\[][^\)\]]+[\)\]]")]
    private static partial Regex BadCharactersPattern();

    [GeneratedRegex(@"&|\band\b", RegexOptions.IgnoreCase)]
    private static partial Regex AndPattern();

    public static string Build(string title, IReadOnlyList<string> authors)
    {
        var titleQuery = BuildTitleQuery(title);
        var authorQuery = string.Join('|', authors.Select(a => $"\"{a}\""));
        return $"@title {titleQuery} @author ({authorQuery})";
    }

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
