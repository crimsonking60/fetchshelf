using System.Globalization;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Fetchshelf.Web.TagHelpers;

/// <summary>
/// <c>&lt;local-time value="x" /&gt;</c>: renders a timestamp as <c>&lt;time class="local-time"&gt;</c> so every page shows
/// it the same way. The server cannot know the visitor's time zone, so it writes the instant in UTC (the no-JS
/// fallback, labelled as such) and wwwroot/js/local-time.js rewrites the text to the browser's local time as
/// <c>YYYY-MM-DD HH:MM:SS</c>. The title keeps the UTC time for reference.
/// </summary>
[HtmlTargetElement("local-time", TagStructure = TagStructure.NormalOrSelfClosing)]
public sealed class LocalTimeTagHelper : TagHelper
{
    private const string DisplayFormat = "yyyy-MM-dd HH:mm:ss";

    public DateTimeOffset? Value { get; set; }

    /// <summary>What to show when <see cref="Value"/> is null, e.g. "never".</summary>
    public string Empty { get; set; } = "-";

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
        output.Attributes.SetAttribute("title", $"{utc} UTC");
        output.Content.SetContent($"{utc} UTC");
    }
}
