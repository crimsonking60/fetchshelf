using System.Globalization;
using System.Net;
using System.Text;

namespace Fetchshelf.Web.Pages.MamApi;

/// <summary>
/// The MAM API page's chart as inline SVG, built on the server with no chart library: a bar per group interval, the
/// number of calls started in it, and a red vertical line at each call MAM answered with a 4xx or 5xx, or didn't answer.
/// Every bar and line carries its details in <c>data-tip</c> (and its time in <c>data-from</c>/<c>data-to</c> or
/// <c>data-at</c>), which wwwroot/js/mam-chart.js shows on hover in the browser's time zone; the axis times are
/// written in UTC and rewritten to local time by the same script.
/// </summary>
public static class MamApiChartSvg
{
    public const int Width = 1000;
    public const int Height = 240;
    private const double Left = 40;
    private const double Right = 8;
    private const double Top = 10;
    private const double Bottom = 22;

    private static double PlotWidth => Width - Left - Right;

    private static double PlotHeight => Height - Top - Bottom;

    public static string Build(MamApiView view)
    {
        var span = (view.End - view.Start).Ticks;
        double X(DateTimeOffset at) => Left + (PlotWidth * (at - view.Start).Ticks / span);
        var max = NiceMax(view.Buckets.Count == 0 ? 0 : view.Buckets.Max(b => b.Calls.Count));
        double Y(double count) => Top + (PlotHeight * (1 - (count / max)));

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"<svg id=\"mam-chart\" class=\"mam-chart\" viewBox=\"0 0 {Width} {Height}\" role=\"img\" aria-label=\"Calls to MAM per {MamApiView.GroupLabel((int)view.Group.TotalMinutes)}\">");

        // The scale: a line at the bottom, half way and the top, labelled with the number of calls.
        foreach (var count in new[] { 0, max / 2.0, max })
        {
            svg.Append(CultureInfo.InvariantCulture,
                $"<line class=\"mam-grid\" x1=\"{Left}\" x2=\"{Width - Right}\" y1=\"{Y(count):0.#}\" y2=\"{Y(count):0.#}\" />");
            svg.Append(CultureInfo.InvariantCulture,
                $"<text class=\"mam-axis\" x=\"{Left - 4}\" y=\"{Y(count) + 4:0.#}\" text-anchor=\"end\">{count:0.#}</text>");
        }

        var tick = TickEvery(view.End - view.Start);
        var firstTick = new DateTimeOffset((view.Start.UtcTicks + tick.Ticks - 1) / tick.Ticks * tick.Ticks, TimeSpan.Zero);
        for (var at = firstTick; at <= view.End; at += tick)
        {
            svg.Append(CultureInfo.InvariantCulture,
                $"<text class=\"mam-axis mam-tick\" x=\"{X(at):0.#}\" y=\"{Height - 6}\" text-anchor=\"middle\" data-at=\"{Iso(at)}\">{at.UtcDateTime:HH:mm}</text>");
        }

        var barWidth = PlotWidth / Math.Max(1, view.Buckets.Count);
        var gap = barWidth > 4 ? 1 : 0;
        foreach (var bucket in view.Buckets)
        {
            var x = X(bucket.Start);
            var tip = WebUtility.HtmlEncode(BucketTip(bucket));
            var times = $"data-from=\"{Iso(bucket.Start)}\" data-to=\"{Iso(bucket.End)}\" data-tip=\"{tip}\"";
            if (bucket.Calls.Count > 0)
            {
                var y = Y(bucket.Calls.Count);
                svg.Append(CultureInfo.InvariantCulture,
                    $"<rect class=\"mam-bar\" x=\"{x + (gap / 2.0):0.##}\" y=\"{y:0.#}\" width=\"{barWidth - gap:0.##}\" height=\"{Top + PlotHeight - y:0.#}\" />");
            }

            // The whole column answers the hover, so a short bar is as easy to point at as a tall one.
            svg.Append(CultureInfo.InvariantCulture,
                $"<rect class=\"mam-hit\" x=\"{x:0.##}\" y=\"{Top}\" width=\"{barWidth:0.##}\" height=\"{PlotHeight}\" {times} />");
        }

        foreach (var call in view.Errors)
        {
            var x = X(call.StartedAt);
            var tip = WebUtility.HtmlEncode(CallTip(call));
            var kind = call.StatusCode is null ? "mam-error mam-no-answer" : "mam-error";
            svg.Append(CultureInfo.InvariantCulture,
                $"<line class=\"{kind}\" x1=\"{x:0.#}\" x2=\"{x:0.#}\" y1=\"{Top}\" y2=\"{Top + PlotHeight}\" />");
            svg.Append(CultureInfo.InvariantCulture,
                $"<line class=\"mam-error-hit\" x1=\"{x:0.#}\" x2=\"{x:0.#}\" y1=\"{Top}\" y2=\"{Top + PlotHeight}\" data-at=\"{Iso(call.StartedAt)}\" data-tip=\"{tip}\" />");
        }

        return svg.Append("</svg>").ToString();
    }

    /// <summary>The bar's details: how many calls, how many failed, how long they took, and to what.</summary>
    public static string BucketTip(MamApiBucket bucket)
    {
        if (bucket.Calls.Count == 0)
        {
            return "No calls";
        }

        var lines = new List<string>
        {
            $"{bucket.Calls.Count} {(bucket.Calls.Count == 1 ? "call" : "calls")}"
                + (bucket.Errors > 0 ? $", {bucket.Errors} failed" : "")
                + $", {MamApiView.Duration((int)bucket.Calls.Average(c => c.DurationMs))} on average",
        };
        lines.AddRange(bucket.Calls.GroupBy(c => c.Endpoint).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} × {g.Key}"));
        lines.AddRange(bucket.Calls.GroupBy(c => c.Caller ?? "No job (a page)").OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} by {g.Key}"));
        return string.Join('\n', lines);
    }

    /// <summary>A failed call's details: the answer, what was called, by whom and how long it took.</summary>
    public static string CallTip(MamApiCall call) => string.Join('\n', new[]
    {
        call.StatusCode is { } status ? $"HTTP {status}" : call.Failure,
        $"{call.Method} {call.Path}",
        call.Caller is { } caller ? $"By {caller}" : "No job (a page)",
        MamApiView.Duration(call.DurationMs),
    }.OfType<string>());

    /// <summary>The top of the scale: at least 2, and even, so the middle line falls on a whole number.</summary>
    private static int NiceMax(int highest) => Math.Max(2, highest + (highest % 2));

    /// <summary>How far apart the times along the bottom are: about six to eight of them.</summary>
    private static TimeSpan TickEvery(TimeSpan span) => span.TotalHours switch
    {
        <= 1 => TimeSpan.FromMinutes(10),
        <= 3 => TimeSpan.FromMinutes(30),
        <= 8 => TimeSpan.FromHours(1),
        <= 14 => TimeSpan.FromHours(2),
        _ => TimeSpan.FromHours(3),
    };

    private static string Iso(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
