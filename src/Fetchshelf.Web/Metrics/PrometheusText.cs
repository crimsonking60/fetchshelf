using System.Globalization;
using System.Text;

namespace Fetchshelf.Web.Metrics;

/// <summary>One value of a metric, with its labels.</summary>
public sealed record MetricSample(double Value, IReadOnlyList<(string Name, string Value)>? Labels = null);

/// <summary>A metric and its samples. A metric without samples, such as one whose value isn't known yet, is left out.</summary>
/// <param name="Name">The metric's name, such as <c>fetchshelf_mam_ratio</c>.</param>
/// <param name="Type"><c>gauge</c> or <c>counter</c>.</param>
/// <param name="Help">One line saying what it is.</param>
public sealed record Metric(string Name, string Type, string Help, IReadOnlyList<MetricSample> Samples)
{
    public static Metric Gauge(string name, string help, double value) => new(name, "gauge", help, [new MetricSample(value)]);
}

/// <summary>Prometheus's text exposition format, version 0.0.4.</summary>
public static class PrometheusText
{
    public const string ContentType = "text/plain; version=0.0.4; charset=utf-8";

    public static string Format(IEnumerable<Metric> metrics)
    {
        var text = new StringBuilder();
        foreach (var metric in metrics.Where(m => m.Samples.Count > 0))
        {
            text.Append("# HELP ").Append(metric.Name).Append(' ').Append(EscapeHelp(metric.Help)).Append('\n');
            text.Append("# TYPE ").Append(metric.Name).Append(' ').Append(metric.Type).Append('\n');
            foreach (var sample in metric.Samples)
            {
                text.Append(metric.Name);
                if (sample.Labels is { Count: > 0 } labels)
                {
                    text.Append('{')
                        .AppendJoin(',', labels.Select(l => $"{l.Name}=\"{EscapeLabel(l.Value)}\""))
                        .Append('}');
                }

                text.Append(' ').Append(FormatValue(sample.Value)).Append('\n');
            }
        }

        return text.ToString();
    }

    private static string FormatValue(double value) => value switch
    {
        double.PositiveInfinity => "+Inf",
        double.NegativeInfinity => "-Inf",
        double.NaN => "NaN",
        _ => value.ToString("R", CultureInfo.InvariantCulture),
    };

    private static string EscapeLabel(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private static string EscapeHelp(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
}
