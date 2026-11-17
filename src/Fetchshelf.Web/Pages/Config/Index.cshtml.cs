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

    /// <summary>How many of a preview's changes the page lists; the rest are counted.</summary>
    public const int PreviewRowsShown = 200;

    /// <summary>The preview a page load without htmx asked for, shown on the page itself; null otherwise.</summary>
    public TagRulePreviewViewModel? PreviewViewModel { get; private set; }

    /// <summary>
    /// The "apply to existing torrents" action's first step for one `[[tag]]` rule: what applying it would change, with
    /// a button that applies it. Nothing is changed in qBittorrent. From the page's htmx form it answers with just the
    /// preview, which the page shows in a dialog over itself; without htmx, with the whole page.
    /// </summary>
    public async Task<IActionResult> OnPostPreviewTagRuleAsync(int ruleIndex)
    {
        if (RuleAt(ruleIndex) is not { } rule)
        {
            return NotFound();
        }

        var entry = settings.Tag[ruleIndex];
        TagRulePreviewViewModel preview;
        try
        {
            preview = new TagRulePreviewViewModel(ruleIndex, entry, await retroactiveTagApplier.PreviewAsync(rule, HttpContext.RequestAborted), Error: null);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && HttpContext.RequestAborted.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Previewing tag rule {RuleIndex} failed", ruleIndex);
            preview = new TagRulePreviewViewModel(
                ruleIndex, entry, Preview: null, $"Previewing the tag rule failed: {MamErrorDescriber.DescribeAmong(ex, "MAM or qBittorrent")}");
        }

        if (Request.Headers.ContainsKey("HX-Request"))
        {
            // htmx only swaps successful answers, so a failure is shown in the dialog as a message too.
            return Partial("_TagRulePreviewPartial", preview);
        }

        PreviewViewModel = preview;
        return Page();
    }

    /// <summary>The "apply to existing torrents" action for one `[[tag]]` rule, after its preview.</summary>
    public async Task<IActionResult> OnPostApplyTagRuleAsync(int ruleIndex)
    {
        if (RuleAt(ruleIndex) is not { } rule)
        {
            return NotFound();
        }

        try
        {
            var applied = await retroactiveTagApplier.ApplyAsync(rule);
            ActionMessage = applied switch
            {
                0 => "Nothing to change: the torrents the tag rule matches already have its category and tags, or aren't in qBittorrent.",
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

    private TagRule? RuleAt(int ruleIndex) =>
        ruleIndex < 0 || ruleIndex >= settings.Tag.Count
            ? null
            : new TagRule
            {
                Filter = SharedFilterConfigMapper.ToTorrentFilter(settings.Tag[ruleIndex]),
                Category = settings.Tag[ruleIndex].Category,
                Tags = settings.Tag[ruleIndex].Tags,
            };
}
