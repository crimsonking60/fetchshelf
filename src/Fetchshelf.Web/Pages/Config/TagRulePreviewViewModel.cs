using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web.Pages.Config;

/// <summary>
/// A tag rule's preview as the Config page shows it: the rule, which the dialog covers on the page, and what applying it
/// would change, or why that couldn't be worked out.
/// </summary>
/// <param name="RuleIndex">The rule's place among the `[[tag]]` rules, from 0, which the Apply button posts.</param>
/// <param name="Rule">The rule as configured.</param>
/// <param name="Preview">What applying it would change; null when <paramref name="Error"/> says why it isn't known.</param>
/// <param name="Error">Why the preview couldn't be worked out, such as qBittorrent not answering.</param>
public sealed record TagRulePreviewViewModel(int RuleIndex, TagRuleEntry Rule, TagRulePreview? Preview, string? Error)
{
    /// <summary>The rule's number in the Config page's table, from 1.</summary>
    public int Number => RuleIndex + 1;

    /// <summary>"tag rule 4", with the rule's `name` after it when it has one: "tag rule 4 (Comics)".</summary>
    public string Title => string.IsNullOrWhiteSpace(Rule.Name) ? $"tag rule {Number}" : $"tag rule {Number} ({Rule.Name})";
}
