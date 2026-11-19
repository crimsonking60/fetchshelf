using Fetchshelf.Web.Pages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Fetchshelf.Web.TagHelpers;

/// <summary>
/// <c>&lt;th sort-by="Title" sort-state="@Model.Query.SortState" sort-page="/Torrents/Index" sort-route="@Model.Query.ToRouteValues()"&gt;Title&lt;/th&gt;</c>:
/// a sortable column heading. The heading's text becomes a link (the whole cell is its target, see site.css) to the
/// same list sorted the way a click leads to, with <c>sort-route</c>'s search, filters and page size kept and the page
/// number dropped. The heading carries <c>aria-sort</c>, and site.css draws the arrow from it.
/// </summary>
[HtmlTargetElement("th", Attributes = "sort-by")]
public sealed class SortHeaderTagHelper(IUrlHelperFactory urlHelperFactory) : TagHelper
{
    [HtmlAttributeName("sort-by")]
    public string Column { get; set; } = string.Empty;

    [HtmlAttributeName("sort-state")]
    public SortState State { get; set; } = new(string.Empty, false);

    [HtmlAttributeName("sort-page")]
    public string Page { get; set; } = string.Empty;

    [HtmlAttributeName("sort-route")]
    public IReadOnlyDictionary<string, string> RouteValues { get; set; } = new Dictionary<string, string>();

    /// <summary>For a number or date column: the first click sorts largest or newest first.</summary>
    [HtmlAttributeName("sort-first-descending")]
    public bool FirstDescending { get; set; }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var next = State.Toggle(Column, FirstDescending);
        var values = new Dictionary<string, object?>();
        foreach (var (name, value) in RouteValues)
        {
            values[name] = value;
        }

        values.Remove("PageNumber");
        values["Sort"] = next.Column;
        values["Dir"] = next.Direction;
        var href = urlHelperFactory.GetUrlHelper(ViewContext).Page(Page, values);

        var label = (await output.GetChildContentAsync()).GetContent();
        output.Attributes.SetAttribute("aria-sort", State.AriaSort(Column));
        output.Content.SetHtmlContent(
            $"<a class=\"sort\" href=\"{System.Net.WebUtility.HtmlEncode(href)}\">{label}<span class=\"sort-arrow\" aria-hidden=\"true\"></span></a>");
    }
}
