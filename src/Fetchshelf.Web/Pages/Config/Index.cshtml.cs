using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fetchshelf.Web.Pages.Config;

/// <summary>
/// The Config page: a read-only view of the active configuration (secrets
/// hidden), a trigger link per `[[autograb]]` entry, an "apply to existing torrents" action per `[[tag]]` rule, and
/// "Send a test message" per `[[notify]]` block.
/// </summary>
public class IndexModel(
    FetchshelfSettings settings, RetroactiveTagApplier retroactiveTagApplier, Notifier notifier, NotificationDelivery notificationDelivery,
    ILogger<IndexModel> logger) : PageModel
{
    public const string HiddenPlaceholder = "(hidden)";

    /// <summary>What the last "apply to existing torrents" did, shown once after the redirect back to this page.</summary>
    [TempData]
    public string? ActionMessage { get; set; }

    [TempData]
    public bool ActionFailed { get; set; }

    public FetchshelfSettings Settings => settings;

    /// <summary>The `[[notify]]` blocks, as they are used.</summary>
    public IReadOnlyList<NotifyTarget> NotifyTargets => notifier.Targets;

    /// <summary>"mam_refused, job_failing", or "all" when the block wants every event.</summary>
    public static string DescribeEvents(NotifyTarget target) =>
        target.Events.Count == NotificationEvents.All.Count
            ? NotificationEvents.AllName
            : string.Join(", ", NotificationEvents.All.Where(target.Events.Contains).Select(NotificationEvents.Name));

    /// <summary>
    /// Sends the test message to one `[[notify]]` block now, whatever events it wants, and shows the answer: the HTTP
    /// status, or what went wrong. The URL is never part of it.
    /// </summary>
    public async Task<IActionResult> OnPostTestNotifyAsync(int index, CancellationToken cancellationToken)
    {
        if (index < 0 || index >= notifier.Targets.Count)
        {
            return NotFound();
        }

        var target = notifier.Targets[index];
        var result = await notificationDelivery.SendTestAsync(target, cancellationToken);
        ActionFailed = !result.Succeeded;
        ActionMessage = result.Succeeded
            ? $"Sent a test message to {target.Name}: {result.Describe()}."
            : $"Could not send a test message to {target.Name}: {result.Describe()}.";
        return RedirectToPage();
    }

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
