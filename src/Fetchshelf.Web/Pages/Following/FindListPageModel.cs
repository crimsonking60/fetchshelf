using System.Globalization;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Web.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fetchshelf.Web.Pages.Following;

/// <summary>
/// What a page with a list of finds binds from its address (<see cref="FindListQuery"/>) and shows after a button: a
/// follow's page and the To look at page. Each row's buttons post to the Following page, which comes back here; the
/// buttons for the ticked finds queue a bulk action here (<see cref="OnPostBulk"/>), whose progress the page shows.
/// </summary>
public abstract class FindListPageModel : PageModel
{
    /// <summary>The page-size cookie the lists of finds share.</summary>
    public const string PageSizeList = "follow-finds";

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public bool MessageIsError { get; set; }

    /// <summary>The finds the address asks for; unset (or unknown) for the page's default.</summary>
    [BindProperty(SupportsGet = true)]
    public FindShow? Show { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Format { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Language { get; set; }

    /// <summary>The column sorted by; unset for the page's default.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    /// <summary>"asc" or "desc"; unset for the column's natural direction.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = Paging.DefaultPageSize;

    public FindListViewModel? List { get; protected set; }

    /// <summary>The query from the address, with the remembered page size; a format other than the two is none.</summary>
    /// <param name="defaultShow">What the page shows when the address names nothing.</param>
    /// <param name="defaultSort">The column the page sorts by when the address names none.</param>
    protected FindListQuery ReadQuery(FindShow defaultShow, string defaultSort = FindListQuery.DefaultSort)
    {
        PageSize = PageSizeCookie.Apply(HttpContext, PageSizeList, PageSize);
        var format = FindListQuery.FormatOptions.Any(o => o.Value == Format) ? Format : null;
        var language = Enum.TryParse<Core.Metadata.Language>(Language, out var parsed) && Enum.IsDefined(parsed) ? parsed.ToString() : null;
        var sort = string.IsNullOrEmpty(Sort) ? defaultSort : Sort;
        var dir = string.IsNullOrEmpty(Dir) ? FindListQuery.DefaultDir(sort) : Dir;
        return new FindListQuery(Show is { } show && Enum.IsDefined(show) ? show : defaultShow, format, sort, dir, PageSize, language, defaultShow);
    }

    /// <summary>Where a button on the page comes back to: this page, with its filters, sort and page.</summary>
    protected string ReturnUrl => Request.Path + Request.QueryString;

    private BulkOperationQueue BulkQueue => HttpContext.RequestServices.GetRequiredService<BulkOperationQueue>();

    /// <summary>The page's own bulk operations: a follow's page shows those of its finds, the To look at page its own.</summary>
    private string? BulkOrigin => RouteData.Values.TryGetValue("id", out var id) ? Convert.ToString(id, CultureInfo.InvariantCulture) : null;

    /// <summary>The bulk operations this page queued, shown above the list while they run and until dismissed.</summary>
    public BulkOperationsViewModel BulkOperations => new(BulkQueue.ShownForFinds(BulkOrigin), PageContext.ActionDescriptor.ViewEnginePath, BulkOrigin);

    /// <summary>
    /// <b>Download</b>, <b>Wedge download</b>, <b>Dismiss</b> or <b>Undo dismiss</b> for the ticked finds, queued for
    /// <see cref="BulkOperationWorker"/>; the page comes back at once and shows how it goes.
    /// </summary>
    public IActionResult OnPostBulk(string bulkAction, List<int> selectedIds, string? returnUrl)
    {
        if (HttpContext.RequestServices.GetService<FollowService>() is null)
        {
            return NotFound();
        }

        BulkAction? action = bulkAction switch
        {
            "download" => BulkAction.DownloadFinds,
            "wedge" => BulkAction.WedgeDownloadFinds,
            "dismiss" => BulkAction.DismissFinds,
            "undo-dismiss" => BulkAction.UndoDismissFinds,
            _ => null,
        };
        if (action is not { } queued)
        {
            return BadRequest();
        }

        if (selectedIds.Count == 0)
        {
            (Message, MessageIsError) = ("Tick one or more books first.", true);
        }
        else
        {
            BulkQueue.Enqueue(queued, selectedIds.Distinct().Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList(), origin: BulkOrigin);
        }

        return Back(returnUrl);
    }

    /// <summary>The panel of bulk operations, which polls itself here while one runs or waits.</summary>
    public PartialViewResult OnGetBulkOperations() => Partial("_BulkOperationsPartial", BulkOperations);

    /// <summary>Cancels a bulk operation: it stops after the find it is on.</summary>
    public IActionResult OnPostCancelBulk(int operationId)
    {
        BulkQueue.Cancel(operationId);
        return BulkPanelOrPage();
    }

    /// <summary>Hides an ended bulk operation.</summary>
    public IActionResult OnPostDismissBulk(int operationId)
    {
        BulkQueue.Dismiss(operationId);
        return BulkPanelOrPage();
    }

    private IActionResult BulkPanelOrPage() => Request.Headers.ContainsKey("HX-Request") ? OnGetBulkOperations() : Back(null);

    /// <summary>Back to the page the button was on, with its filters, sort and page, else to this page.</summary>
    private IActionResult Back(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToPage();
}
