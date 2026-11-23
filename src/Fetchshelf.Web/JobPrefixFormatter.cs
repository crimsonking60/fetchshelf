using Fetchshelf.Core.Jobs;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace Fetchshelf.Web;

/// <summary>
/// Renders a log line with <see cref="OutputTemplate"/>, which puts the job the line was written in first:
/// <c>[Link] Wrote cover.jpg for …</c>. The job comes from the scope each job run opens
/// (<see cref="JobRegistry.JobLogProperty"/>); a line written outside a job has no prefix.
/// </summary>
public sealed class JobPrefixFormatter(string outputTemplate) : ITextFormatter
{
    /// <summary>The property the templates render the prefix from.</summary>
    public const string PrefixProperty = "JobPrefix";

    /// <summary>The console's lines: Serilog's default, with the job in front of the message.</summary>
    public const string ConsoleTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {" + PrefixProperty + ":l}{Message:lj}{NewLine}{Exception}";

    /// <summary>The log file's lines: Serilog's default, with the job in front of the message.</summary>
    public const string FileTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {" + PrefixProperty + ":l}{Message:lj}{NewLine}{Exception}";

    private readonly MessageTemplateTextFormatter _inner = new(outputTemplate, System.Globalization.CultureInfo.InvariantCulture);

    public string OutputTemplate => outputTemplate;

    public void Format(LogEvent logEvent, TextWriter output) => _inner.Format(WithPrefix(logEvent), output);

    /// <summary>
    /// The event with the prefix property added when it names a job. A copy, not the event changed in place: the console
    /// writes from its own thread while the file sink may be reading the same event.
    /// </summary>
    private static LogEvent WithPrefix(LogEvent logEvent)
    {
        if (!logEvent.Properties.TryGetValue(JobRegistry.JobLogProperty, out var job) || job is not ScalarValue { Value: string name }
            || string.IsNullOrWhiteSpace(name) || logEvent.Properties.ContainsKey(PrefixProperty))
        {
            return logEvent;
        }

        var properties = logEvent.Properties
            .Select(p => new LogEventProperty(p.Key, p.Value))
            .Append(new LogEventProperty(PrefixProperty, new ScalarValue($"[{name}] ")));
        return new LogEvent(logEvent.Timestamp, logEvent.Level, logEvent.Exception, logEvent.MessageTemplate, properties, logEvent.TraceId ?? default, logEvent.SpanId ?? default);
    }
}
