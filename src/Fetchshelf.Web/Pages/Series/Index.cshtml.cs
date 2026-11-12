using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Series;

/// <summary>
/// The Series page: every series in the library, with the books owned and those missing, and, with a `[follow]`
/// section, buttons to follow, watch or ignore it.
/// </summary>
public class IndexModel(FetchshelfDbContext dbContext, LibraryQueries libraryQueries, FetchshelfSettings settings, IServiceProvider services)
    : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>"missing", "followed", "ignored", or empty for every series.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Show { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = Paging.DefaultPageSize;

    public static IReadOnlyList<(string Value, string Label)> ShowOptions { get; } =
    [
        ("missing", "With missing books"),
        ("followed", "Auto or followed"),
        ("ignored", "Ignored"),
    ];

    /// <summary>Whether series can be followed: only with a `[follow]` section.</summary>
    public bool CanFollow => settings.Follow is not null;

    public IReadOnlyList<SeriesRowViewModel> Rows { get; private set; } = [];

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public Dictionary<string, string> RouteValues =>
        new Dictionary<string, string> { [nameof(Q)] = Q ?? string.Empty, [nameof(Show)] = Show ?? string.Empty, [nameof(PageSize)] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            .Where(pair => pair.Value.Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

    public PagerViewModel Pager => new("/Series/Index", RouteValues, PageNumber, TotalPages, Rows.Count, TotalCount, PageSize, "series-pager", "Series pages");

    public async Task OnGetAsync()
    {
        PageSize = PageSizeCookie.Apply(HttpContext, "series", PageSize);
        var follows = await FollowQueries.SeriesFollowsAsync(dbContext);
        var text = Q?.Trim() ?? string.Empty;
        var byAuthor = text.Length == 0 ? [] : await libraryQueries.SeriesKeysWithAuthorLikeAsync(text);

        // Every series' entries are needed to know which have missing books; the authors only for the page shown.
        var matched = (await libraryQueries.SeriesAsync())
            .Where(s => Show switch
            {
                "missing" => follows.GetValueOrDefault(s.NameKey)?.Mode != FollowMode.Ignore && SeriesRowViewModel.From(s, [], null).HasMissing,
                "followed" => follows.GetValueOrDefault(s.NameKey)?.Mode is FollowMode.Download or FollowMode.Watch,
                "ignored" => follows.GetValueOrDefault(s.NameKey)?.Mode == FollowMode.Ignore,
                _ => true,
            })
            .Where(s => text.Length == 0 || s.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase) || byAuthor.Contains(s.NameKey))
            .ToList();
        TotalCount = matched.Count;
        (var page, PageNumber, TotalPages) = Paging.Slice(matched, TotalCount, PageNumber, PageSize);
        Rows = await ToRowsAsync(page, follows);
    }

    /// <summary>Follows, watches, ignores or stops following one series; from htmx it answers with the row alone.</summary>
    /// <param name="mode">"Download", "Watch", "Ignore", or "Stop" to stop following.</param>
    public async Task<IActionResult> OnPostModeAsync(string key, string name, string mode)
    {
        if (services.GetService<FollowService>() is not { } followService)
        {
            return NotFound();
        }

        if (mode == "Stop")
        {
            if (await followService.FindAsync(FollowKind.Series, key) is { } follow)
            {
                await followService.StopAsync(follow.Id);
            }
        }
        else if (Enum.TryParse<FollowMode>(mode, out var followMode) && Enum.IsDefined(followMode))
        {
            await followService.SetAsync(FollowKind.Series, key, name, followMode);
        }
        else
        {
            return BadRequest();
        }

        if (!Request.Headers.ContainsKey("HX-Request"))
        {
            return RedirectToPage(RouteValues.ToDictionary(pair => pair.Key, pair => (object?)pair.Value));
        }

        var row = await LoadRowAsync(key);
        return row is null ? Content(string.Empty) : Partial("_SeriesRowPartial", new SeriesRowPartialModel(row, CanFollow));
    }

    /// <summary>One series' row, after a button changed it.</summary>
    private async Task<SeriesRowViewModel?> LoadRowAsync(string key) =>
        await libraryQueries.SeriesAsync(key) is { } series
            ? (await ToRowsAsync([series], await FollowQueries.SeriesFollowsAsync(dbContext))).Single()
            : null;

    private async Task<List<SeriesRowViewModel>> ToRowsAsync(IReadOnlyList<SeriesSummary> series, Dictionary<string, FollowEntity> follows)
    {
        var authors = await libraryQueries.SeriesAuthorsAsync(series.Select(s => s.NameKey).ToList());
        return series
            .Select(s => SeriesRowViewModel.From(s, authors.GetValueOrDefault(s.NameKey) ?? [], follows.GetValueOrDefault(s.NameKey)))
            .ToList();
    }
}

/// <summary>One row of the Series page, and whether it gets follow buttons.</summary>
public sealed record SeriesRowPartialModel(SeriesRowViewModel Row, bool CanFollow);
