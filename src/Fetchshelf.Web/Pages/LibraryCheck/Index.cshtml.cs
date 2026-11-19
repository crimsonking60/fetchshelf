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
    EventStore eventStore, LibraryWriteGate writeGate, BulkOperationQueue bulkOperations, ILogger<IndexModel> logger) : PageModel
{
    public DateTimeOffset? CheckedAt => results.CheckedAt;

    public bool Running => jobRegistry.Get(JobNames.LibraryCheck)?.IsRunning == true;

    [TempData]
    public string? ActionMessage { get; set; }

    [TempData]
    public bool ActionFailed { get; set; }

    /// <summary>The findings of one kind, in the order the check found them.</summary>
    public IReadOnlyList<NumberedFinding> Of(LibraryFindingKind kind) => results.Findings.Where(f => f.Finding.Kind == kind).ToList();

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
        return RedirectToPage();
    }

    /// <summary><b>Run the check</b>: starts the job, which goes on after the page has come back.</summary>
    public IActionResult OnPostRun()
    {
        _ = jobRegistry.TriggerAsync(JobNames.LibraryCheck, JobTrigger.Manual, CancellationToken.None);
        ActionMessage = "The Library check is running; the page shows what it found when it ends.";
        return RedirectToPage();
    }

    /// <summary><b>Delete selected</b>: each folder or file is checked again first, so one a book uses now is left alone.</summary>
    public async Task<IActionResult> OnPostDeleteAsync(List<int> selectedIds)
    {
        if (Refusal(selectedIds) is { } notNow)
        {
            return Refuse(notNow);
        }

        var (deleted, refusals) = (new List<int>(), new List<string>());
        using (await writeGate.AcquireAsync(HttpContext.RequestAborted))
        {
            // A check that started after the page was shown held the gate until it ended, and replaced the findings.
            if (selectedIds.Any(id => results.Get(id) is null))
            {
                return Refuse(Refusal(selectedIds)!);
            }

            foreach (var numbered in selectedIds.Select(results.Get).OfType<NumberedFinding>())
            {
                try
                {
                    if (await LibraryCheckDeleter.DeleteAsync(numbered.Finding, libraries, dbContext, eventStore, HttpContext.RequestAborted) is { } refusal)
                    {
                        refusals.Add(refusal);
                        continue;
                    }

                    logger.LogInformation("Library check: deleted {Path}", numbered.Finding.Path);
                    deleted.Add(numbered.Id);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning("Library check: could not delete {Path}: {Error}", numbered.Finding.Path, ex.Message);
                    refusals.Add($"{numbered.Finding.Path} could not be deleted: {ex.Message}");
                }
            }
        }

        results.Remove(deleted);
        ActionMessage = string.Join(" ", new[] { $"Deleted {deleted.Count}." }.Concat(refusals));
        ActionFailed = refusals.Count > 0;
        return RedirectToPage();
    }

    /// <summary><b>Relink selected</b>: queued as the Torrents page's bulk Relink, whose progress that page shows.</summary>
    public IActionResult OnPostRelink(List<int> selectedIds)
    {
        if (Refusal(selectedIds) is { } refusal)
        {
            return Refuse(refusal);
        }

        var books = BookFindings(selectedIds);
        if (books.Count > 0)
        {
            bulkOperations.Enqueue(BulkAction.Relink, books.Select(f => f.Finding.TorrentId!).Distinct(StringComparer.Ordinal).ToList(), null);
            results.Remove(books.Select(f => f.Id));
        }

        ActionMessage = $"Relink of {books.Count} {(books.Count == 1 ? "book" : "books")} queued; the Torrents page shows how it goes.";
        return RedirectToPage();
    }

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
        return RedirectToPage();
    }

    private List<NumberedFinding> BookFindings(List<int> selectedIds) =>
        selectedIds.Select(results.Get).OfType<NumberedFinding>().Where(f => f.Finding.TorrentId is not null).ToList();
}
