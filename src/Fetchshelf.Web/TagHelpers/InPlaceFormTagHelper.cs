using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Fetchshelf.Web.TagHelpers;

/// <summary>
/// <c>&lt;form in-place&gt;</c>: a form that posts and comes back to the same page (a redirect to it) without the page
/// reloading. htmx sends it and swaps the page's <c>#in-place</c> element for the same element of the page that comes
/// back, so the message, the buttons and the lists all show the result while the scroll position stays put. The menu bar
/// is swapped too, for its counts. The button pressed is disabled until the answer is in. Without JavaScript it is a
/// plain form post.
/// </summary>
/// <remarks>
/// The page puts the part to refresh in an element with <see cref="RegionId"/>: the messages and everything a button
/// changes, and nothing with scripts that would run again (such as <c>_HitDialog</c>). A form whose answer is another
/// page, such as Stop following on a follow's own page, leaves the attribute off.
/// </remarks>
[HtmlTargetElement("form", Attributes = "in-place")]
public sealed class InPlaceFormTagHelper : TagHelper
{
    /// <summary>The id of the element a page refreshes in place.</summary>
    public const string RegionId = "in-place";

    /// <summary>The menu bar's id: it comes along, as its counts (such as To look at and Selected) may have changed.</summary>
    public const string NavId = "site-nav";

    /// <summary>Whether it posts in place; false leaves it a plain form.</summary>
    [HtmlAttributeName("in-place")]
    public bool InPlace { get; set; } = true;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!InPlace)
        {
            return;
        }

        output.Attributes.SetAttribute("hx-boost", "true");
        output.Attributes.SetAttribute("hx-target", "#" + RegionId);
        output.Attributes.SetAttribute("hx-select", "#" + RegionId);
        output.Attributes.SetAttribute("hx-select-oob", "#" + NavId);
        output.Attributes.SetAttribute("hx-swap", "outerHTML show:none");
        output.Attributes.SetAttribute("hx-push-url", "false");
        output.Attributes.SetAttribute("hx-disabled-elt", "find button");
    }
}
