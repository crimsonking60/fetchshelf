using Fetchshelf.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.MamApi;

/// <summary>
/// The calls Fetchshelf made to MAM in a window of 1, 6 or 14 hours: a chart of calls per group interval with the
/// errors marked, statistics, and every call, newest first. Without <see cref="End"/> the window ends now and the
/// page refreshes itself.
/// </summary>
public class IndexModel(FetchshelfDbContext dbContext, TimeProvider timeProvider) : PageModel
{
    /// <summary>How many calls the table lists; the chart and statistics count them all.</summary>
    public const int TableRows = 500;

    /// <summary>The group interval, in minutes.</summary>
    [BindProperty(SupportsGet = true)]
    public int Group { get; set; } = 10;

    /// <summary>The window, in hours.</summary>
    [BindProperty(SupportsGet = true)]
    public int Span { get; set; } = 6;

    /// <summary>When the window ends, as Unix seconds; null for now.</summary>
    [BindProperty(SupportsGet = true)]
    public long? End { get; set; }

    public MamApiView View { get; private set; } = null!;

    /// <summary>The window ends now, so the page refreshes itself.</summary>
    public bool Live => End is null;

    /// <summary>The window before this one.</summary>
    public long PreviousEnd => View.Start.ToUnixTimeSeconds();

    /// <summary>The window after this one; null when that would reach now, which is the live window.</summary>
    public long? NextEnd { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    /// <summary>The chart, statistics and table alone, for the live window's refresh.</summary>
    public async Task<IActionResult> OnGetPartialAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return Partial("_MamApiPartial", this);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Group = MamApiView.GroupMinutes.Contains(Group) ? Group : 10;
        Span = MamApiView.SpanHours.Contains(Span) ? Span : 6;
        var now = timeProvider.GetUtcNow();
        var group = TimeSpan.FromMinutes(Group);
        var span = TimeSpan.FromHours(Span);
        DateTimeOffset? end = End is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        if (end >= now)
        {
            End = null;
            end = null;
        }

        var (start, windowEnd) = MamApiView.Window(now, end, span, group);
        var (from, to) = (start.ToUnixTimeMilliseconds(), windowEnd.ToUnixTimeMilliseconds());
        var calls = await dbContext.MamCalls.AsNoTracking()
            .Where(c => c.StartedAtMs >= from && c.StartedAtMs < to)
            .Select(c => new { c.StartedAtMs, c.DurationMs, c.Method, c.Host, c.Path, c.StatusCode, c.Failure, c.Caller })
            .ToListAsync(cancellationToken);
        View = MamApiView.Build(
            calls.Select(c => new MamApiCall(
                DateTimeOffset.FromUnixTimeMilliseconds(c.StartedAtMs), c.DurationMs, c.Method, c.Host, c.Path, c.StatusCode, c.Failure, c.Caller)).ToList(),
            start, windowEnd, group);
        NextEnd = End is null || windowEnd + span >= now ? null : (windowEnd + span).ToUnixTimeSeconds();
    }
}
