using System.Globalization;

namespace Fetchshelf.Web.Pages.MamApi;

/// <summary>One recorded call to MAM, as the MAM API page reads it.</summary>
public sealed record MamApiCall(
    DateTimeOffset StartedAt, int DurationMs, string Method, string Host, string Path, int? StatusCode, string? Failure, string? Caller)
{
    /// <summary>MAM answered with an error (4xx or 5xx), or didn't answer. A call the caller cancelled is neither.</summary>
    public bool IsError => StatusCode >= 400 || (StatusCode is null && Failure != "Cancelled");

    /// <summary>The status, or why there was none, for a table cell.</summary>
    public string Result => StatusCode?.ToString(CultureInfo.InvariantCulture) ?? Failure ?? "?";

    /// <summary>What was called, for grouping: the API endpoint, or "Cover images" for the images fetched for covers.</summary>
    public string Endpoint => Path.EndsWith(".php", StringComparison.OrdinalIgnoreCase) ? Path
        : Path.StartsWith("/tor/download.php", StringComparison.OrdinalIgnoreCase) ? "/tor/download.php"
        : "Cover images";
}

/// <summary>One bar of the chart: the calls started in one group interval.</summary>
public sealed record MamApiBucket(DateTimeOffset Start, DateTimeOffset End, IReadOnlyList<MamApiCall> Calls)
{
    public int Errors => Calls.Count(c => c.IsError);
}

/// <summary>A row of the per-endpoint or per-caller table.</summary>
public sealed record MamApiBreakdown(string Label, int Calls, int Errors, int MedianMs);

/// <summary>The numbers in the statistics box, for the calls in the window shown.</summary>
public sealed record MamApiStats(
    int Calls,
    double CallsPerHour,
    int Ok,
    int ClientErrors,
    int ServerErrors,
    int NoAnswer,
    int RateLimited,
    DateTimeOffset? LastRateLimitedAt,
    (int Calls, DateTimeOffset At)? BusiestMinute,
    (int Calls, DateTimeOffset At)? BusiestTenMinutes,
    TimeSpan? ShortestGap,
    int? MedianMs,
    int? P95Ms,
    int? MaxMs,
    IReadOnlyList<MamApiBreakdown> ByEndpoint,
    IReadOnlyList<MamApiBreakdown> ByCaller);

/// <summary>
/// What the MAM API page shows for one window: the calls grouped into bars, the errors to mark, the statistics and the
/// calls themselves, newest first. Built from the calls started in the window, with no I/O.
/// </summary>
public sealed record MamApiView(
    DateTimeOffset Start,
    DateTimeOffset End,
    TimeSpan Group,
    IReadOnlyList<MamApiBucket> Buckets,
    IReadOnlyList<MamApiCall> Errors,
    MamApiStats Stats,
    IReadOnlyList<MamApiCall> NewestFirst)
{
    /// <summary>The group intervals the page offers, in minutes.</summary>
    public static readonly IReadOnlyList<int> GroupMinutes = [1, 10, 30];

    /// <summary>The windows the page offers, in hours.</summary>
    public static readonly IReadOnlyList<int> SpanHours = [1, 6, 14];

    /// <summary>
    /// The window of <paramref name="span"/> that ends at <paramref name="end"/>, or, without one, at the end of the
    /// group interval <paramref name="now"/> falls in, so the newest bar is the current one. The end is moved up to a
    /// group boundary, so bars always start on whole minutes of the interval.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Window(DateTimeOffset now, DateTimeOffset? end, TimeSpan span, TimeSpan group)
    {
        var target = end ?? now;
        var ticks = target.UtcTicks;
        var aligned = ticks % group.Ticks == 0 ? ticks : (ticks / group.Ticks + 1) * group.Ticks;
        var windowEnd = new DateTimeOffset(aligned, TimeSpan.Zero);
        return (windowEnd - span, windowEnd);
    }

    public static MamApiView Build(IReadOnlyList<MamApiCall> calls, DateTimeOffset start, DateTimeOffset end, TimeSpan group)
    {
        var inWindow = calls.Where(c => c.StartedAt >= start && c.StartedAt < end).OrderBy(c => c.StartedAt).ToList();
        var byBucket = inWindow.ToLookup(c => (c.StartedAt - start).Ticks / group.Ticks);
        var buckets = new List<MamApiBucket>();
        for (var (from, index) = (start, 0L); from < end; from += group, index++)
        {
            buckets.Add(new MamApiBucket(from, from + group, byBucket[index].ToList()));
        }

        return new MamApiView(
            start, end, group, buckets, inWindow.Where(c => c.IsError).ToList(), Statistics(inWindow, end - start),
            Enumerable.Reverse(inWindow).ToList());
    }

    private static MamApiStats Statistics(IReadOnlyList<MamApiCall> calls, TimeSpan span)
    {
        var durations = calls.Where(c => c.StatusCode is not null).Select(c => c.DurationMs).Order().ToList();
        var rateLimited = calls.Where(c => c.StatusCode == 429).ToList();
        TimeSpan? shortestGap = calls.Count < 2 ? null : calls.Zip(calls.Skip(1), (a, b) => b.StartedAt - a.StartedAt).Min();
        return new MamApiStats(
            calls.Count,
            calls.Count / span.TotalHours,
            calls.Count(c => c.StatusCode is >= 200 and < 400),
            calls.Count(c => c.StatusCode is >= 400 and < 500),
            calls.Count(c => c.StatusCode >= 500),
            calls.Count(c => c.StatusCode is null && c.Failure != "Cancelled"),
            rateLimited.Count,
            rateLimited.Count == 0 ? null : rateLimited[^1].StartedAt,
            Busiest(calls, TimeSpan.FromMinutes(1)),
            Busiest(calls, TimeSpan.FromMinutes(10)),
            shortestGap,
            Percentile(durations, 0.5),
            Percentile(durations, 0.95),
            durations.Count == 0 ? null : durations[^1],
            Breakdown(calls, c => c.Endpoint),
            Breakdown(calls, c => c.Caller ?? "No job (a page)"));
    }

    /// <summary>The most calls started within any <paramref name="width"/>, sliding over the calls, and when that stretch began.</summary>
    private static (int Calls, DateTimeOffset At)? Busiest(IReadOnlyList<MamApiCall> calls, TimeSpan width)
    {
        (int Calls, DateTimeOffset At)? best = null;
        var first = 0;
        for (var last = 0; last < calls.Count; last++)
        {
            while (calls[last].StartedAt - calls[first].StartedAt >= width)
            {
                first++;
            }

            var count = last - first + 1;
            if (best is null || count > best.Value.Calls)
            {
                best = (count, calls[first].StartedAt);
            }
        }

        return best;
    }

    private static int? Percentile(IReadOnlyList<int> sorted, double share) =>
        sorted.Count == 0 ? null : sorted[(int)Math.Min(sorted.Count - 1, Math.Ceiling(share * sorted.Count) - 1)];

    private static List<MamApiBreakdown> Breakdown(IEnumerable<MamApiCall> calls, Func<MamApiCall, string> key) =>
        calls.GroupBy(key)
            .Select(g => new MamApiBreakdown(
                g.Key, g.Count(), g.Count(c => c.IsError), Percentile(g.Select(c => c.DurationMs).Order().ToList(), 0.5) ?? 0))
            .OrderByDescending(b => b.Calls).ThenBy(b => b.Label, StringComparer.Ordinal)
            .ToList();

    /// <summary>"350 ms" or "2.4 s".</summary>
    public static string Duration(int ms) => ms < 1000
        ? $"{ms} ms"
        : string.Create(CultureInfo.InvariantCulture, $"{ms / 1000.0:0.0} s");

    /// <summary>"1 min", "10 min".</summary>
    public static string GroupLabel(int minutes) => $"{minutes} min";

    /// <summary>"1 hour", "6 hours".</summary>
    public static string SpanLabel(int hours) => hours == 1 ? "1 hour" : $"{hours} hours";
}
