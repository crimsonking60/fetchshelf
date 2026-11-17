using Fetchshelf.Data.AutoDownloader;

namespace Fetchshelf.Web.Pages.Config;

/// <summary>A tag rule's preview as the Config page shows it: what applying it would change, or why that couldn't be worked out.</summary>
/// <param name="RuleIndex">The rule's place among the `[[tag]]` rules, which the Apply button posts.</param>
/// <param name="RuleName">The rule's `name`, or "tag rule N".</param>
/// <param name="Preview">What applying it would change; null when <paramref name="Error"/> says why it isn't known.</param>
/// <param name="Error">Why the preview couldn't be worked out, such as qBittorrent not answering.</param>
public sealed record TagRulePreviewViewModel(int RuleIndex, string RuleName, TagRulePreview? Preview, string? Error);
