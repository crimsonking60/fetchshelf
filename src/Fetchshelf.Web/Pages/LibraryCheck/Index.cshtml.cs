using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.LibraryCheck;

/// <summary>
/// The Library check's findings (#25), with an action for each kind: delete an unknown folder or file, relink or remove
/// the record of a book whose folder or files are gone. A finding acted on leaves the list; the rest wait for the next
/// check. While a check runs, the findings shown are the previous check's, and the actions are refused: the check holds
/// the library write gate, and when it ends its findings replace the list the page showed.
/// </summary>
public class IndexModel(
    LibraryCheckResults results, IJobRegistry jobRegistry, IReadOnlyList<Library> libraries, FetchshelfDbContext dbContext,
    BulkOperationQueue bulkOperations) : PageModel
{
    public DateTimeOffset? CheckedAt => results.CheckedAt;

    public bool Running => jobRegistry.Get(JobNames.LibraryCheck)?.IsRunning == true;

    [TempData]
    public string? ActionMessage { get; set; }

    [TempData]
    public bool ActionFailed { get; set; }

    /// <summary>The library whose findings are shown, by the name a finding carries; empty for every library.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Library { get; set; }

    /// <summary>The libraries to choose from, named as their findings are: by name, else by folder.</summary>
    public IReadOnlyList<string> LibraryNames => libraries.Select(l => l.Name ?? l.LibraryDir).Distinct().ToList();

    /// <summary>Whether the check found anything at all, in any library.</summary>
    public bool AnyFindings => results.Findings.Count > 0;

    /// <summary>The findings of one kind in the chosen library, in the order the check found them.</summary>
    public IReadOnlyList<NumberedFinding> Of(LibraryFindingKind kind) => results.Findings
        .Where(f => f.Finding.Kind == kind && (string.IsNullOrEmpty(Library) || f.Finding.LibraryName == Library))
        .ToList();

    /// <summary>Back to the page, with the library that was chosen.</summary>
    private RedirectToPageResult BackToPage() => RedirectToPage(new { Library = string.IsNullOrEmpty(Library) ? null : Library });

    public void OnGet()
    {
    }

    /// <summary>Polled while a check runs: nothing until it has ended, then a reload of the page, with its findings.</summary>
    public IActionResult OnGetStatus()
    {
        if (Running)
        {
            return new NoContentResult();
        }

        Response.Headers["HX-Refresh"] = "true";
        return Content(string.Empty);
    }

    /// <summary>
    /// Why an action on the ticked findings can't be done now: a check is running, or some of them were replaced by a newer
    /// check since the page was shown. Null when it can.
    /// </summary>
    private string? Refusal(List<int> selectedIds)
    {
        if (Running)
        {
            return "The Library check is running, so these are the previous check's findings. Act on them once it has ended and the page shows its own.";
        }

        var replaced = selectedIds.Count(id => results.Get(id) is null);
        return replaced > 0
            ? $"{replaced} of the ticked findings {(replaced == 1 ? "is" : "are")} from an earlier check, which a newer one has replaced. Nothing was done; tick them again on the current list."
            : null;
    }

    private IActionResult Refuse(string refusal)
    {
        ActionMessage = refusal;
        ActionFailed = true;
        return BackToPage();
    }

    /// <summary>How long <b>Run the check</b> waits for the check to end before the page comes back with it still running.</summary>
    public static readonly TimeSpan RunWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// <b>Run the check</b>: starts the job and waits up to <see cref="RunWait"/> for it, so a small library's check comes
    /// back with its findings. A longer one goes on after the page has come back, which then says it is running.
    /// </summary>
    public async Task<IActionResult> OnPostRunAsync()
    {
        // A new check would replace the findings a Delete or Relink selected is still working through.
        if (bulkOperations.Active().Any(o => o.OnLibraryCheck))
        {
            return Refuse("A Delete or Relink selected is still running or waiting. Run the check once it has ended, or cancel it.");
        }

        var run = jobRegistry.TriggerAsync(JobNames.LibraryCheck, JobTrigger.Manual, CancellationToken.None);
        await Task.WhenAny(run, Task.Delay(RunWait, HttpContext.RequestAborted));
        return BackToPage();
    }

    /// <summary><b>Delete selected</b>: each folder or file is checked again first, so one a book uses now is left alone.</summary>
    public IActionResult OnPostDelete(List<int> selectedIds) => Queue(BulkAction.DeleteFindings, selectedIds);

    /// <summary>
    /// <b>Relink selected</b>, as on the torrent page, one book after the other. A book relinked leaves the list; one that
    /// couldn't be stays, with why noted on it, so <b>Remove record</b> or a fix by hand can follow.
    /// </summary>
    public IActionResult OnPostRelink(List<int> selectedIds) => Queue(BulkAction.RelinkFindings, BookFindings(selectedIds).Select(f => f.Id).ToList(), selectedIds);

    /// <summary>
    /// Queues <b>Delete selected</b> or <b>Relink selected</b> for <see cref="BulkOperationWorker"/>, whose progress the
    /// page shows at the top, and comes back at once: thousands of folders take a while, and the Link and Clean jobs run
    /// between them.
    /// </summary>
    /// <param name="ticked">What was ticked, checked for findings a newer check replaced; the items by default.</param>
    private IActionResult Queue(BulkAction action, List<int> items, List<int>? ticked = null)
    {
        if (Refusal(ticked ?? items) is { } refusal)
        {
            return Refuse(refusal);
        }

        if (items.Count > 0)
        {
            bulkOperations.Enqueue(action, items.Distinct().Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList());
        }

        return BackToPage();
    }

    /// <summary>This page's bulk operations, shown at the top while they run and until dismissed.</summary>
    public BulkOperationsViewModel BulkOperations => new(bulkOperations.Shown(libraryCheck: true), "/LibraryCheck/Index");

    /// <summary>The panel of bulk operations, which polls itself here while one runs or waits.</summary>
    public PartialViewResult OnGetBulkOperations() => Partial("_BulkOperationsPartial", BulkOperations);

    /// <summary>Cancels a bulk operation: it stops after the finding it is on.</summary>
    public IActionResult OnPostCancelBulk(int operationId)
    {
        bulkOperations.Cancel(operationId);
        return BulkPanelOrPage();
    }

    /// <summary>Hides an ended bulk operation.</summary>
    public IActionResult OnPostDismissBulk(int operationId)
    {
        bulkOperations.Dismiss(operationId);
        return BulkPanelOrPage();
    }

    private IActionResult BulkPanelOrPage() => Request.Headers.ContainsKey("HX-Request") ? OnGetBulkOperations() : BackToPage();

    /// <summary><b>Remove record</b>: forgets the selected books, as the torrent page's Remove record does.</summary>
    public async Task<IActionResult> OnPostRemoveRecordAsync(List<int> selectedIds)
    {
        if (Refusal(selectedIds) is { } refusal)
        {
            return Refuse(refusal);
        }

        var books = BookFindings(selectedIds);
        var ids = books.Select(f => f.Finding.TorrentId!).ToList();
        dbContext.Torrents.RemoveRange(await dbContext.Torrents.Where(t => ids.Contains(t.Id)).ToListAsync());
        await dbContext.SaveChangesAsync();
        results.Remove(books.Select(f => f.Id));
        ActionMessage = $"Removed the {(books.Count == 1 ? "record" : "records")} of {books.Count} {(books.Count == 1 ? "book" : "books")}.";
        return BackToPage();
    }

    private List<NumberedFinding> BookFindings(List<int> selectedIds) =>
        selectedIds.Select(results.Get).OfType<NumberedFinding>().Where(f => f.Finding.TorrentId is not null).ToList();
}
