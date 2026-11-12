using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fetchshelf.Web.Pages.Following;

/// <summary>
/// The Following page: every followed or watched series and author, with what the Follow job found for it, and the
/// buttons to download or dismiss a find, change the mode, search now or stop following. Also where the follow buttons
/// on a torrent's page and the Torrents page post to.
/// </summary>
public class IndexModel(
    FetchshelfSettings settings, IJobRegistry jobRegistry, FormatPreferenceLists formatPreferenceLists, MamClientOptions mamOptions,
    IServiceProvider services, ILogger<IndexModel> logger) : PageModel
{
    [TempData]
    public string? Message { get; set; }

    [TempData]
    public bool MessageIsError { get; set; }

    public bool CanFollow => settings.Follow is not null;

    /// <summary>What the Follow job downloads by itself for a followed series or author, one line per grab block.</summary>
    public IReadOnlyList<string> GrabRules => settings.Follow is { } follow ? FollowGrabSummary.Describe(follow.Grab) : [];

    /// <summary>Whether `dry_run` is on in `[follow]`, so nothing is downloaded by itself.</summary>
    public bool DryRun => settings.Follow?.DryRun == true;

    /// <summary>
    /// When the Follow job searches a series or author again, at the earliest: `search_again_after` hours after its last
    /// search, as the job decides what is due. A later run, or Search now, does it.
    /// </summary>
    public DateTimeOffset SearchAgainAfter(DateTimeOffset lastSearched) =>
        lastSearched + TimeSpan.FromHours(settings.Follow?.SearchAgainAfter ?? FollowSettings.DefaultSearchAgainAfterHours);

    public IReadOnlyList<FollowWithFinds> Follows { get; private set; } = [];

    private FollowService? FollowService => services.GetService<FollowService>();

    public async Task OnGetAsync()
    {
        if (FollowService is { } followService)
        {
            Follows = await followService.AllAsync(HttpContext.RequestAborted);
        }
    }

    /// <summary>Follows a series or author in its default mode, from a torrent's page or the Torrents page, and goes back there.</summary>
    public async Task<IActionResult> OnPostFollowAsync(FollowKind kind, string key, string name, string? returnUrl)
    {
        if (FollowService is not { } followService)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name) || !Enum.IsDefined(kind))
        {
            return BadRequest();
        }

        var follow = await followService.SetAsync(kind, key, name);
        if (Url.IsLocalUrl(returnUrl))
        {
            // The page the button was on shows it followed; a message here would wait for the next visit to this page.
            return LocalRedirect(returnUrl);
        }

        Message = $"Following {Describe(follow)}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostModeAsync(FollowKind kind, string key, string name, FollowMode mode)
    {
        if (FollowService is not { } followService)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name) || !Enum.IsDefined(kind) || !Enum.IsDefined(mode)
            || (kind == FollowKind.Author && mode == FollowMode.Ignore))
        {
            return BadRequest();
        }

        var follow = await followService.SetAsync(kind, key, name, mode);
        Message = $"{Describe(follow)}: {ModeLabel(follow.Mode)}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostStopAsync(int followId, string name)
    {
        if (FollowService is not { } followService)
        {
            return NotFound();
        }

        await followService.StopAsync(followId);
        Message = $"Stopped following {name}.";
        return RedirectToPage();
    }

    /// <summary>Makes the follow the first the Follow job searches, and starts the job.</summary>
    public async Task<IActionResult> OnPostSearchNowAsync(int followId, string name)
    {
        if (FollowService is not { } followService)
        {
            return NotFound();
        }

        await followService.SearchSoonAsync(followId);
        if (jobRegistry.Get(JobNames.Follow) is null)
        {
            // Background jobs are off, such as without a mam_id: the search waits for them.
            Message = $"{name} is searched first when the Follow job next runs.";
            return RedirectToPage();
        }

        _ = jobRegistry.TriggerOrQueueAsync(JobNames.Follow, JobTrigger.Manual, CancellationToken.None);
        Message = $"The Follow job is searching for {name}; the Jobs page shows when it is done.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDismissAsync(int findId)
    {
        if (FollowService is not { } followService)
        {
            return NotFound();
        }

        await followService.DismissAsync(findId);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDownloadAsync(int findId, bool wedge)
    {
        if (FollowService is not { } followService)
        {
            return NotFound();
        }

        try
        {
            var result = await followService.DownloadAsync(findId, wedge, HttpContext.RequestAborted);
            if (result.Outcome is { } outcome && result.Metadata is { } metadata)
            {
                var started = outcome.IsSelected() && StartDownload();
                Message = SelectionOutcomeMessages.Describe(outcome, metadata, formatPreferenceLists, started);
                MessageIsError = !outcome.IsSelected();
            }
            else
            {
                (Message, MessageIsError) = (result.Problem, true);
            }
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && HttpContext.RequestAborted.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Downloading follow find {FindId} failed", findId);
            (Message, MessageIsError) = ($"Download failed: {MamErrorDescriber.Describe(ex, mamOptions.Timeout)}", true);
        }

        return RedirectToPage();
    }

    public static string ModeLabel(FollowMode mode) => mode switch
    {
        FollowMode.Download => "Followed - downloaded automatically",
        FollowMode.Watch => "Watched - download yourself",
        _ => "Ignored",
    };

    public static string StatusLabel(FollowFindStatus status) => status switch
    {
        FollowFindStatus.New => "New",
        FollowFindStatus.Selected => "Selected",
        FollowFindStatus.Wanted => "Not free",
        FollowFindStatus.Owned => "In the library",
        _ => "Dismissed",
    };

    private static string Describe(FollowEntity follow) =>
        $"{(follow.Kind == FollowKind.Series ? "the series" : "the author")} {follow.DisplayName}";

    private bool StartDownload()
    {
        if (!settings.DownloadAfterSelection || jobRegistry.Get(JobNames.Download) is null)
        {
            return false;
        }

        _ = jobRegistry.TriggerOrQueueAsync(JobNames.Download, JobTrigger.Manual, CancellationToken.None);
        return true;
    }
}
