using Fetchshelf.Data.AutoDownloader;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fetchshelf.Web.Pages.Following;

/// <summary>
/// What a page with a list of finds binds from its address (<see cref="FindListQuery"/>) and shows after a button: a
/// follow's page and the To look at page. The buttons post to the Following page, which comes back here.
/// </summary>
public abstract class FindListPageModel : PageModel
{
    /// <summary>The page-size cookie the lists of finds share.</summary>
    public const string PageSizeList = "follow-finds";

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public bool MessageIsError { get; set; }

    [BindProperty(SupportsGet = true)]
    public FindShow Show { get; set; } = FindShow.ToLookAt;

    [BindProperty(SupportsGet = true)]
    public string? Format { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = FindListQuery.DefaultSort;

    [BindProperty(SupportsGet = true)]
    public string Dir { get; set; } = FindListQuery.DefaultDir(FindListQuery.DefaultSort);

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = Paging.DefaultPageSize;

    public FindListViewModel? List { get; protected set; }

    /// <summary>The query from the address, with the remembered page size; a format other than the two is none.</summary>
    protected FindListQuery ReadQuery()
    {
        PageSize = PageSizeCookie.Apply(HttpContext, PageSizeList, PageSize);
        var format = FindListQuery.FormatOptions.Any(o => o.Value == Format) ? Format : null;
        return new FindListQuery(Enum.IsDefined(Show) ? Show : FindShow.ToLookAt, format, Sort, Dir, PageSize);
    }

    /// <summary>Where a button on the page comes back to: this page, with its filters, sort and page.</summary>
    protected string ReturnUrl => Request.Path + Request.QueryString;
}
