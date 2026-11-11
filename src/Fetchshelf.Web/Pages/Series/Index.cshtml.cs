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
        ("followed", "Followed or watched"),
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
        var all = await LoadRowsAsync();
        var matched = all
            .Where(r => Show switch
            {
                "missing" => r.HasMissing && r.Follow?.Mode != FollowMode.Ignore,
                "followed" => r.Follow?.Mode is FollowMode.Download or FollowMode.Watch,
                "ignored" => r.Follow?.Mode == FollowMode.Ignore,
                _ => true,
            })
            .Where(r => string.IsNullOrWhiteSpace(Q)
                || r.Name.Contains(Q.Trim(), StringComparison.CurrentCultureIgnoreCase)
                || r.Authors.Any(a => a.Contains(Q.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            .ToList();
        TotalCount = matched.Count;
        (var page, PageNumber, TotalPages) = Paging.Slice(matched, TotalCount, PageNumber, PageSize);
        Rows = page;
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

        var row = (await LoadRowsAsync(key)).SingleOrDefault();
        return row is null ? Content(string.Empty) : Partial("_SeriesRowPartial", new SeriesRowPartialModel(row, CanFollow));
    }

    /// <summary>The rows, by series name; only the one series when <paramref name="key"/> is given.</summary>
    private async Task<List<SeriesRowViewModel>> LoadRowsAsync(string? key = null)
    {
        IReadOnlyList<SeriesSummary> series = key is null
            ? await libraryQueries.SeriesAsync()
            : await libraryQueries.SeriesAsync(key) is { } one ? [one] : [];
        var authors = await libraryQueries.SeriesAuthorsAsync();
        var follows = await dbContext.Follows.AsNoTracking().Where(f => f.Kind == FollowKind.Series).ToDictionaryAsync(f => f.NameKey, StringComparer.Ordinal);
        return series
            .Select(s => SeriesRowViewModel.From(s, authors.GetValueOrDefault(s.NameKey) ?? [], follows.GetValueOrDefault(s.NameKey)))
            .ToList();
    }
}

/// <summary>One row of the Series page, and whether it gets follow buttons.</summary>
public sealed record SeriesRowPartialModel(SeriesRowViewModel Row, bool CanFollow);
