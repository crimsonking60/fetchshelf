using System.Globalization;
using System.Net;
using System.Text;

namespace Fetchshelf.Web.Pages.Account;

/// <summary>
/// A small line chart as inline SVG, built on the server, with no chart library: one value per day, a day without one
/// leaving a gap in the line. The chart is drawn in the text color's accent through CSS (<c>.sparkline</c>).
/// </summary>
public static class SparklineSvg
{
    public const int Width = 300;
    public const int Height = 60;

    /// <summary>The smallest range a chart spans, as a share of its values' size.</summary>
    public const double MinimumSpan = 0.02;

    /// <summary>Room left at the edges, so a line at the top or bottom isn't cut off.</summary>
    private const double Margin = 3;

    /// <summary>
    /// The chart of <paramref name="values"/>, oldest first and evenly spaced, scaled so the lowest value is at the bottom
    /// and the highest at the top, unless they lie closer together than <see cref="MinimumSpan"/>. Null values, and values
    /// that aren't finite, are gaps; a value with gaps on both sides is a dot. <paramref name="description"/> is what a
    /// screen reader says instead.
    /// </summary>
    public static string Build(IReadOnlyList<double?> values, string description)
    {
        var known = values.Where(v => v is { } x && double.IsFinite(x)).Select(v => v!.Value).ToList();
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"<svg class=\"sparkline\" viewBox=\"0 0 {Width} {Height}\" width=\"{Width}\" height=\"{Height}\" role=\"img\" aria-label=\"{WebUtility.HtmlEncode(description)}\">");
        if (known.Count > 0)
        {
            var (low, high) = Range(known.Min(), known.Max());
            foreach (var run in Runs(values))
            {
                var points = run.Select(i => Point(i, values.Count, values[i]!.Value, low, high)).ToList();
                if (points.Count == 1)
                {
                    svg.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{points[0].X:0.#}\" cy=\"{points[0].Y:0.#}\" r=\"1.5\" />");
                }
                else
                {
                    svg.Append("<polyline points=\"")
                        .AppendJoin(' ', points.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.X:0.#},{p.Y:0.#}")))
                        .Append("\" />");
                }
            }
        }

        return svg.Append("</svg>").ToString();
    }

    /// <summary>
    /// The range the chart spans: the values' own, but at least <see cref="MinimumSpan"/> of their size, around their
    /// middle, so a ratio that moves from 2.38 to 2.39 stays a nearly flat line instead of filling the chart.
    /// </summary>
    private static (double Low, double High) Range(double low, double high)
    {
        var least = Math.Max(Math.Abs(low), Math.Abs(high)) * MinimumSpan;
        if (high - low >= least)
        {
            return (low, high);
        }

        var middle = (low + high) / 2;
        return (middle - (least / 2), middle + (least / 2));
    }

    /// <summary>The indexes of each unbroken run of known values.</summary>
    private static IEnumerable<List<int>> Runs(IReadOnlyList<double?> values)
    {
        var run = new List<int>();
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is { } value && double.IsFinite(value))
            {
                run.Add(i);
            }
            else if (run.Count > 0)
            {
                yield return run;
                run = [];
            }
        }

        if (run.Count > 0)
        {
            yield return run;
        }
    }

    private static (double X, double Y) Point(int index, int count, double value, double low, double high)
    {
        var x = count == 1 ? Width / 2.0 : Margin + (Width - (2 * Margin)) * index / (count - 1);
        // All values the same: a flat line across the middle.
        var y = high == low ? Height / 2.0 : Margin + (Height - (2 * Margin)) * (high - value) / (high - low);
        return (x, y);
    }
}
