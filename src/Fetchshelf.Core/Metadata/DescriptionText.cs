using System.Net;
using System.Text.RegularExpressions;

namespace Fetchshelf.Core.Metadata;

/// <summary>
/// A MAM description as plain text. MAM sends HTML written by the uploader, which must not reach a page or a reader
/// as markup.
/// </summary>
public static partial class DescriptionText
{
    /// <summary>
    /// Line breaks and paragraphs become new lines, scripts and styles go with their content, other tags are dropped
    /// and entities decoded. Null when nothing is left.
    /// </summary>
    public static string? From(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = ScriptOrStylePattern().Replace(html, string.Empty);
        text = LineBreakPattern().Replace(text, "\n");
        text = TagPattern().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = BlankLinesPattern().Replace(text.Replace("\r\n", "\n", StringComparison.Ordinal), "\n\n");
        return text.Trim() is { Length: > 0 } trimmed ? trimmed : null;
    }

    [GeneratedRegex(@"<\s*br\s*/?\s*>|<\s*/\s*p\s*>|<\s*/\s*div\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakPattern();

    [GeneratedRegex(@"<\s*(script|style)\b[^>]*>.*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStylePattern();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\n[ \t]*\n(\s*\n)+")]
    private static partial Regex BlankLinesPattern();
}
