using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fetchshelf.Web.Pages.Following;

/// <summary>
/// The Following page: every followed series and author, one row each with how many of its finds are in each state, and
/// the buttons to change its mode, search now or stop following. Also where every follow button posts to: those on a
/// torrent's page and the Torrents page, on a follow's own page (<see cref="DetailModel"/>) and on the To look at page
/// (<see cref="ToLookAtModel"/>); each comes back to the page it was on.
/// </summary>
public class IndexModel(
    FetchshelfSettings settings, IJobRegistry jobRegistry, FormatPreferenceLists formatPreferenceLists, MamClientOptions mamOptions,
    FetchshelfDbContext dbContext, IServiceProvider services, ILogger<IndexModel> logger) : PageModel
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

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public FollowKind? Kind { get; set; }

    [BindProperty(SupportsGet = true)]
    public FollowMode? Mode { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Waiting { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = FollowListQuery.DefaultSort;

    [BindProperty(SupportsGet = true)]
    public string Dir { get; set; } = FollowListQuery.DefaultDir(FollowListQuery.DefaultSort);

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = Paging.DefaultPageSize;

    public FollowListQuery Query => new(Q, Kind, Mode, Waiting, Sort, Dir, PageSize);

    /// <summary>The follows on this page.</summary>
    public IReadOnlyList<FollowSummary> Follows { get; private set; } = [];

    /// <summary>Whether anything is followed at all, matched by the search and filters or not.</summary>
    public bool AnyFollows { get; private set; }

    /// <summary>The finds to look at, of every follow.</summary>
    public int ToLookAt { get; private set; }

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public PagerViewModel Pager =>
        new("/Following/Index", Query.ToRouteValues(), PageNumber, TotalPages, Follows.Count, TotalCount, PageSize, "follow-pager", "Pages of follows");

    /// <summary>Where a button on the page comes back to: this page, with its search, filters, sort and page.</summary>
    public string ReturnUrl => Request.Path + Request.QueryString;

    private FollowService? FollowService => services.GetService<FollowService>();

    public async Task OnGetAsync()
    {
        PageSize = PageSizeCookie.Apply(HttpContext, "following", PageSize);
        if (settings.Follow is not { } follow)
        {
            return;
        }

        var summaries = await FollowQueries.SummariesAsync(dbContext, FollowQueries.WantedSides(follow.Grab), HttpContext.RequestAborted);
        AnyFollows = summaries.Count > 0;
        ToLookAt = summaries.Sum(s => s.ToLookAt);
        var matched = Query.Apply(summaries).ToList();
        TotalCount = matched.Count;
        (var page, PageNumber, TotalPages) = Paging.Slice(matched, TotalCount, PageNumber, PageSize);
        Follows = page;
    }

    /// <summary>Follows a series or author in the default mode (<see cref="FollowService.DefaultMode"/>), from a torrent's page or the Torrents page, and goes back there.</summary>
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
        return RedirectToPage("/Following/Detail", new { id = follow.Id });
    }

    public async Task<IActionResult> OnPostModeAsync(FollowKind kind, string key, string name, FollowMode mode, string? returnUrl)
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
        return Back(returnUrl);
    }

    public async Task<IActionResult> OnPostStopAsync(int followId, string name, string? returnUrl)
    {
        if (FollowService is not { } followService)
        {
            return NotFound();
        }

        await followService.StopAsync(followId);
        Message = $"Stopped following {name}.";
        return Back(returnUrl);
    }

    /// <summary>How long Search now waits for the Follow job's run before showing the page again.</summary>
    public static readonly TimeSpan SearchNowWait = TimeSpan.FromSeconds(15);

    /// <summary>Makes the follow the first the Follow job searches, starts the job, and waits a while for it.</summary>
    public async Task<IActionResult> OnPostSearchNowAsync(int followId, string name, string? returnUrl)
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
            return Back(returnUrl);
        }

        // Waits for the run, so the page comes back with what it found and the new search time; a run that searches many
        // follows, or waits for another MAM job, carries on in the background.
        var run = jobRegistry.TriggerOrQueueAsync(JobNames.Follow, JobTrigger.Manual, CancellationToken.None);
        if (await Task.WhenAny(run, Task.Delay(SearchNowWait, HttpContext.RequestAborted)) != run)
        {
            Message = $"The Follow job is searching for {name}; the Jobs page shows when it is done.";
            return Back(returnUrl);
        }

        if (jobRegistry.Get(JobNames.Follow)?.LastRun is { Result: JobRunResult.Failure } failed)
        {
            (Message, MessageIsError) = ($"The search for {name} failed: {failed.Summary}", true);
            return Back(returnUrl);
        }

        Message = $"Searched MAM for {name}.";
        return Back(returnUrl);
    }

    public async Task<IActionResult> OnPostDismissAsync(int findId, string? returnUrl)
    {
        if (FollowService is not { } followService)
        {
            return NotFound();
        }

        await followService.DismissAsync(findId);
        return Back(returnUrl);
    }

    public async Task<IActionResult> OnPostDownloadAsync(int findId, bool wedge, string? returnUrl)
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

        return Back(returnUrl);
    }

    /// <summary>How many of the ticked books' outcomes a bulk download names, before saying how many more there are.</summary>
    private const int BulkProblemsNamed = 3;

    /// <summary>Downloads, wedge-downloads or dismisses the ticked finds of a list of finds.</summary>
    public async Task<IActionResult> OnPostBulkAsync(string bulkAction, int[] findIds, string? returnUrl)
    {
        if (FollowService is not { } followService)
        {
            return NotFound();
        }

        if (findIds.Length == 0)
        {
            (Message, MessageIsError) = ("Tick one or more books first.", true);
            return Back(returnUrl);
        }

        switch (bulkAction)
        {
            case "dismiss":
                foreach (var findId in findIds)
                {
                    await followService.DismissAsync(findId);
                }

                Message = findIds.Length == 1 ? "Dismissed 1 book." : $"Dismissed {findIds.Length} books.";
                return Back(returnUrl);
            case "download" or "wedge":
                await DownloadAllAsync(followService, findIds, bulkAction == "wedge");
                return Back(returnUrl);
            default:
                return BadRequest();
        }
    }

    /// <summary>
    /// Selects each find in turn, as its own Download button does, and says how many were selected and why others weren't.
    /// A MAM failure stops the rest: they would most likely fail the same way.
    /// </summary>
    private async Task DownloadAllAsync(FollowService followService, int[] findIds, bool wedge)
    {
        var selected = 0;
        var problems = new List<string>();
        foreach (var findId in findIds)
        {
            try
            {
                var result = await followService.DownloadAsync(findId, wedge, HttpContext.RequestAborted);
                if (result.Outcome is { } outcome && outcome.IsSelected())
                {
                    selected++;
                }
                else if (result.Outcome is { } other && result.Metadata is { } metadata)
                {
                    problems.Add(SelectionOutcomeMessages.Describe(other, metadata, formatPreferenceLists, downloadStarted: false));
                }
                else
                {
                    problems.Add(result.Problem ?? string.Empty);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && HttpContext.RequestAborted.IsCancellationRequested))
            {
                logger.LogWarning(ex, "Downloading follow find {FindId} failed", findId);
                problems.Add($"Download failed: {MamErrorDescriber.Describe(ex, mamOptions.Timeout)}");
                break;
            }
        }

        var started = selected > 0 && StartDownload();
        var summary = $"Selected {selected} of {findIds.Length}"
            + (selected == 0 ? "." : started ? "; the Download job is starting them now." : "; they start at the next Download run.");
        var named = problems.Take(BulkProblemsNamed).Append(
            problems.Count > BulkProblemsNamed ? $"{problems.Count - BulkProblemsNamed} more were not selected." : string.Empty);
        Message = string.Join(' ', named.Prepend(summary).Where(part => part.Length > 0));
        MessageIsError = selected == 0;
    }

    /// <summary>Back to the page the button was on, else to this one.</summary>
    private IActionResult Back(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToPage();

    public static string ModeLabel(FollowMode mode) => mode switch
    {
        FollowMode.Download => "Auto - downloaded automatically",
        FollowMode.Watch => "Followed - download yourself",
        _ => "Ignored",
    };

    /// <summary>
    /// A find's own label, for one not on its way into the library. A new find has none, as a Search result with nothing
    /// done to it has none: its Download and Dismiss buttons say what can be done.
    /// </summary>
    public static string StatusLabel(FollowFindStatus status) => status switch
    {
        FollowFindStatus.New => string.Empty,
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
