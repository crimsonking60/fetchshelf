using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fetchshelf.Web.Pages.Config;

/// <summary>
/// The Config page: a read-only view of the active configuration (secrets
/// hidden), a trigger link per `[[autograb]]` entry, and an "apply to existing torrents" action per
/// `[[tag]]` rule.
/// </summary>
public class IndexModel(FetchshelfSettings settings, RetroactiveTagApplier retroactiveTagApplier, ILogger<IndexModel> logger) : PageModel
{
    public const string HiddenPlaceholder = "(hidden)";

    /// <summary>What the last "apply to existing torrents" did, shown once after the redirect back to this page.</summary>
    [TempData]
    public string? ActionMessage { get; set; }

    [TempData]
    public bool ActionFailed { get; set; }

    public FetchshelfSettings Settings => settings;

    public void OnGet()
    {
    }

    /// <summary>The "apply to existing torrents" action for one `[[tag]]` rule.</summary>
    public async Task<IActionResult> OnPostApplyTagRuleAsync(int ruleIndex)
    {
        if (ruleIndex < 0 || ruleIndex >= settings.Tag.Count)
        {
            return NotFound();
        }

        var entry = settings.Tag[ruleIndex];
        var rule = new TagRule
        {
            Filter = SharedFilterConfigMapper.ToTorrentFilter(entry),
            Category = entry.Category,
            Tags = entry.Tags,
        };
        try
        {
            var applied = await retroactiveTagApplier.ApplyAsync(rule);
            ActionMessage = applied switch
            {
                0 => "No torrent in qBittorrent matched the tag rule.",
                1 => "Applied the tag rule to 1 torrent.",
                _ => $"Applied the tag rule to {applied} torrents.",
            };
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && HttpContext.RequestAborted.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Applying tag rule {RuleIndex} failed", ruleIndex);
            ActionFailed = true;
            ActionMessage = $"Applying the tag rule stopped part-way: {MamErrorDescriber.DescribeAmong(ex, "MAM or qBittorrent")}";
        }

        return RedirectToPage();
    }
}
