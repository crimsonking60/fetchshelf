using System.Globalization;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Fetchshelf.Web.TagHelpers;

/// <summary>
/// <c>&lt;local-time value="x" /&gt;</c>: renders a timestamp as <c>&lt;time class="local-time"&gt;</c> so every page shows
/// it the same way. The server cannot know the visitor's time zone, so it writes the instant in UTC (the no-JS
/// fallback, labelled as such) and wwwroot/js/local-time.js rewrites the text to the browser's local time as
/// <c>YYYY-MM-DD HH:MM:SS</c>. The title keeps the UTC time for reference. With <c>short</c> the browser shows
/// "3 h ago" or "yesterday 21:14" instead, for the tables, and the title holds the exact local time. With <c>date</c>
/// it shows only the local date (<c>YYYY-MM-DD</c>), with the exact local time in the title, and with <c>minute</c> the
/// local time without seconds (<c>YYYY-MM-DD HH:MM</c>).
/// </summary>
[HtmlTargetElement("local-time", TagStructure = TagStructure.NormalOrSelfClosing)]
public sealed class LocalTimeTagHelper : TagHelper
{
    private const string DisplayFormat = "yyyy-MM-dd HH:mm:ss";

    public DateTimeOffset? Value { get; set; }

    /// <summary>What to show when <see cref="Value"/> is null, e.g. "never".</summary>
    public string Empty { get; set; } = "-";

    /// <summary>Shows the time in the short form for tables ("3 h ago", "yesterday 21:14"); the exact local time goes in the title.</summary>
    public bool Short { get; set; }

    /// <summary>Shows only the date, in the browser's time zone; the exact local time goes in the title.</summary>
    public bool Date { get; set; }

    /// <summary>Shows the time to the minute, in the browser's time zone, for times on the hour or a schedule.</summary>
    public bool Minute { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (Value is not { } value)
        {
            output.TagName = null;
            output.Content.SetContent(Empty);
            return;
        }

        var utc = value.UtcDateTime.ToString(DisplayFormat, CultureInfo.InvariantCulture);
        output.TagName = "time";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "local-time");
        output.Attributes.SetAttribute("datetime", value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        if (Short)
        {
            output.Attributes.SetAttribute("data-short", "");
        }

        output.Attributes.SetAttribute("title", $"{utc} UTC");
        if (Date)
        {
            // The fallback keeps the date's width, so the column doesn't shift when the script rewrites it.
            output.Attributes.SetAttribute("data-date", "");
            output.Content.SetContent(value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            return;
        }

        if (Minute)
        {
            output.Attributes.SetAttribute("data-minute", "");
            output.Content.SetContent($"{value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC");
            return;
        }

        output.Content.SetContent($"{utc} UTC");
    }
}
