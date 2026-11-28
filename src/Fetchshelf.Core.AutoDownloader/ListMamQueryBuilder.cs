using System.Text.RegularExpressions;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// Builds a MAM title and author search text from a list item: <c>@title &lt;title&gt; @author ("A"|"B")</c>.
/// Bracketed suffixes and every character MAM's search could read as an operator are removed from the title,
/// "and"/"&amp;" matches either, and for a title with a colon the part before it is tried as well. An initial in an
/// author's name matches with or without its full stop: lists and MAM don't agree on "Sarah J. Maas" or "Sarah J Maas",
/// and MAM's search takes "J." and "J" as different words. Initials written together, as in "A.E. van Vogt", are also
/// searched for apart ("A E van Vogt"), and a suffix such as "Jr." is left out, so the name matches with it or without.
/// </summary>
public static partial class ListMamQueryBuilder
{
    [GeneratedRegex(@"\s+[\(\[][^\)\]]+[\)\]]")]
    private static partial Regex BracketedSuffixPattern();

    /// <summary>
    /// What MAM's search could read as an operator, besides <c>*</c> (matched literally instead) and <c>-</c> (only at
    /// the start of a word, see <see cref="LeadingHyphenPattern"/>): quotes, <c>~</c> and <c>!</c> (not), <c>|</c>
    /// (or), <c>@</c> (a field), brackets, and the rest. A title of nothing but a "not", such as
    /// <c>~The World of Final Fantasy XIV~</c>, makes MAM refuse the search.
    /// </summary>
    [GeneratedRegex(@"['`/?""~!^$=<>|@\\{}()\[\]]")]
    private static partial Regex OperatorPattern();

    /// <summary>A hyphen starting a word, which MAM reads as "not": <c>-ish</c>. One inside a word, as in "Spider-Man", stays.</summary>
    [GeneratedRegex(@"(?<=^|\s)-+")]
    private static partial Regex LeadingHyphenPattern();

    [GeneratedRegex(@"&|\band\b", RegexOptions.IgnoreCase)]
    private static partial Regex AndPattern();

    [GeneratedRegex(@"(?<=^|\s)(\p{Lu})\.?(?=\s|$)")]
    private static partial Regex InitialPattern();

    /// <summary>Initials written together: <c>A.E.</c>, <c>J.R.R.</c>, <c>A.E</c>.</summary>
    [GeneratedRegex(@"(?<=^|\s)(?:\p{Lu}\.)+\p{Lu}\.?(?=\s|$)")]
    private static partial Regex InitialsTogetherPattern();

    /// <summary>
    /// A suffix at the end of a name: Jr, Sr (with or without a full stop or a comma before it) or II to IV. A phrase
    /// without it still matches the name with it, so it is left out.
    /// </summary>
    [GeneratedRegex(@",?\s+(?:(?i:Jr|Jnr|Sr|Snr)\.?|II|III|IV)$")]
    private static partial Regex SuffixPattern();

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

    /// <summary>The search text; without authors, the title alone, since an empty <c>@author ()</c> matches nothing.</summary>
    public static string Build(string title, IReadOnlyList<string> authors)
    {
        var titleQuery = BuildTitleQuery(title);
        if (authors.Count == 0)
        {
            return $"@title {titleQuery}";
        }

        // Each name is a quoted phrase, so only a quote or a backslash in it could break the search.
        var authorQuery = string.Join('|', authors
            .SelectMany(a => BuildAuthorQueries(a.Replace('"', ' ').Replace('\\', ' ')))
            .Distinct(StringComparer.Ordinal)
            .Select(phrase => $"\"{phrase}\""));
        return $"@title {titleQuery} @author ({authorQuery})";
    }

    /// <summary>
    /// The phrases one author is searched for by: the name without a suffix such as "Jr.", each single-letter initial
    /// written as <c>(J|J.)</c>, and, when it has initials written together, the same with them apart.
    /// </summary>
    public static IReadOnlyList<string> BuildAuthorQueries(string author)
    {
        var name = SuffixPattern().Replace(author.Trim(), string.Empty) is { Length: > 0 } withoutSuffix ? withoutSuffix : author.Trim();
        var phrases = new List<string> { BuildAuthorQuery(name) };
        if (InitialsTogetherPattern().IsMatch(name))
        {
            var apart = InitialsTogetherPattern().Replace(name, match => string.Join(' ', match.Value.Where(char.IsUpper)));
            phrases.Add(BuildAuthorQuery(apart));
        }

        return phrases;
    }

    /// <summary>One author's name with each single-letter initial written as <c>(J|J.)</c>.</summary>
    public static string BuildAuthorQuery(string author) =>
        InitialPattern().Replace(author, match => $"({match.Groups[1].Value}|{match.Groups[1].Value}.)");

    /// <summary>The title portion alone, exposed for testing the title-specific transformations.</summary>
    public static string BuildTitleQuery(string title)
    {
        var cleaned = OperatorPattern().Replace(BracketedSuffixPattern().Replace(title, " "), " ");
        cleaned = LeadingHyphenPattern().Replace(cleaned, " ");
        // After the clean-up, which takes quotes out: the quotes added here make the star match literally.
        var escaped = cleaned.Replace("*", "\"*\"");
        var withAndExpanded = AndPattern().Replace(escaped, "(and|&)");

        var colonIndex = withAndExpanded.IndexOf(':');
        if (colonIndex < 0)
        {
            return withAndExpanded;
        }

        var primaryTitle = withAndExpanded[..colonIndex];
        return $"(\"{primaryTitle}\"|\"{withAndExpanded}\")";
    }
}
