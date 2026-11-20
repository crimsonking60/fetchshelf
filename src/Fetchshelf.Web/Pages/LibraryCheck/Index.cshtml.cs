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
    EventStore eventStore, LibraryWriteGate writeGate, LinkRunner linkRunner, FetchshelfSettings settings,
    QBittorrentClientFactory qbittorrentFactory, ILogger<IndexModel> logger) : PageModel
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
        var run = jobRegistry.TriggerAsync(JobNames.LibraryCheck, JobTrigger.Manual, CancellationToken.None);
        await Task.WhenAny(run, Task.Delay(RunWait, HttpContext.RequestAborted));
        return BackToPage();
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
        return BackToPage();
    }

    /// <summary>
    /// <b>Relink selected</b>, as on the torrent page, one book after the other. A book relinked leaves the list; one that
    /// couldn't be stays, with why noted on it, so <b>Remove record</b> or a fix by hand can follow.
    /// </summary>
    public async Task<IActionResult> OnPostRelinkAsync(List<int> selectedIds)
    {
        if (Refusal(selectedIds) is { } refusal)
        {
            return Refuse(refusal);
        }

        var books = BookFindings(selectedIds);
        var (relinked, notRelinked) = (new List<int>(), new Dictionary<int, (LibraryFinding, string?)>());
        var connections = QBittorrentConnections.Build(settings, qbittorrentFactory);
        using (await writeGate.AcquireAsync(HttpContext.RequestAborted))
        {
            foreach (var (id, finding, _) in books)
            {
                try
                {
                    var result = await linkRunner.RelinkAsync(finding.TorrentId!, connections, HttpContext.RequestAborted);
                    if (result.Succeeded)
                    {
                        relinked.Add(id);
                    }
                    else
                    {
                        // What stops it now, which may have changed since the check; the message only when that isn't it.
                        var (blocker, category) = await linkRunner.RelinkBlockerAsync(finding.TorrentId!, connections, HttpContext.RequestAborted);
                        notRelinked[id] = (finding with { Blocker = blocker, BlockerDetail = category }, blocker == RelinkBlocker.None ? result.Message : null);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Library check: relinking {Title} failed: {Error}", finding.Title, ex.Message);
                    notRelinked[id] = (finding, $"Not relinked: {MamErrorDescriber.DescribeAmong(ex, "qBittorrent")}");
                }
            }
        }

        results.Remove(relinked);
        results.Update(notRelinked);
        ActionMessage = $"Relinked {relinked.Count} of {books.Count}."
            + (notRelinked.Count > 0 ? $" {notRelinked.Count} could not be; the What to do column says why, and what to do about it." : "");
        ActionFailed = notRelinked.Count > 0;
        return BackToPage();
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
        return BackToPage();
    }

    private List<NumberedFinding> BookFindings(List<int> selectedIds) =>
        selectedIds.Select(results.Get).OfType<NumberedFinding>().Where(f => f.Finding.TorrentId is not null).ToList();
}
