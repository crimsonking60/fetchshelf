using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages;

/// <summary>
/// The Search page: manual MAM search with the shared filter, cost badges, and
/// download / download-with-wedge actions that start a selection via tag rules. Also serves as the
/// application's home page ("/").
/// </summary>
public class IndexModel(
    MamClient mamClient, MamClientOptions mamOptions, SelectionRunner selectionRunner, FetchshelfSettings settings,
    FormatPreferenceLists formatPreferenceLists, FetchshelfDbContext dbContext, ILogger<IndexModel> logger) : PageModel
{
    /// <summary>What happened to the last Download click, shown once after the redirect back to this page.</summary>
    [TempData]
    public string? DownloadMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public IReadOnlyList<SearchResultViewModel> Results { get; private set; } = [];

    /// <summary>False while the page shell is rendered; the results arrive afterwards via <see cref="OnGetResultsAsync"/>.</summary>
    public bool ResultsLoaded { get; private set; }

    /// <summary>Set when the MAM search failed, so the results panel can say why instead of staying empty.</summary>
    public string? SearchError { get; private set; }

    /// <summary>
    /// Renders the page shell without waiting on MAM; the browser then fetches the table through
    /// <see cref="OnGetResultsAsync"/> (htmx, on load) while a spinner covers it.
    /// </summary>
    public void OnGet()
    {
    }

    /// <summary>The results table for <see cref="Q"/>, swapped into the Search page by htmx.</summary>
    public async Task<PartialViewResult> OnGetResultsAsync()
    {
        // Neither the Ebooks/Audiobooks "Show" filter nor "Hide downloaded torrents" narrows this
        // query or the Results list - both are applied browser-side (see the page's script)
        // against each row's data-media-type/data-status attribute, so every result is always
        // fetched and rendered regardless of the current filter state.
        var query = new MamSearchQuery
        {
            Text = string.IsNullOrWhiteSpace(Q) ? null : Q,
            SearchFields = string.IsNullOrWhiteSpace(Q) ? [] : [MamSearchField.Title, MamSearchField.Author],
            Target = MamSearchTarget.New,
            ReturnDownloadLink = true,
        };

        try
        {
            var result = await mamClient.SearchAsync(query);
            var wedgeOverBytes = settings.Search.WedgeOver is { } wedgeOver ? MamSizeParser.ParseBytes(wedgeOver) : (long?)null;
            var statusByMamId = await LoadStatusesAsync(result.Data.Select(record => record.MamId).ToList());

            Results = result.Data
                .Select(record => SearchResultViewModel.From(
                    record, MamSizeParser.ParseBytes(record.SizeRaw), wedgeOverBytes,
                    statusByMamId.GetValueOrDefault(record.MamId, MamResultStatus.None)))
                .ToList();
        }
        catch (Exception ex) when (!IsRequestAborted(ex))
        {
            // htmx does not swap error responses, so a thrown exception would leave the panel blank: say what went wrong instead.
            logger.LogWarning(ex, "Search on MAM failed");
            SearchError = MamErrorDescriber.Describe(ex, mamOptions.Timeout);
        }

        ResultsLoaded = true;
        return Partial("_SearchResultsPartial", this);
    }

    /// <summary>
    /// Resolves each given MAM id to where it stands in Fetchshelf's pipeline: linked into the
    /// library, downloaded but not yet linked, or still queued as a selection.
    /// </summary>
    private async Task<Dictionary<long, MamResultStatus>> LoadStatusesAsync(List<long> mamIds)
    {
        var statuses = new Dictionary<long, MamResultStatus>();
        if (mamIds.Count == 0)
        {
            return statuses;
        }

        var selectedMamIds = await dbContext.SelectedTorrents
            .Where(s => s.RemovedAt == null && s.StartedAt == null && mamIds.Contains(s.MamId))
            .Select(s => s.MamId)
            .ToListAsync();
        foreach (var mamId in selectedMamIds)
        {
            statuses[mamId] = MamResultStatus.Selected;
        }

        var torrents = await dbContext.Torrents
            .Where(t => mamIds.Contains(t.MamId))
            .Select(t => new { t.MamId, t.LibraryPath })
            .ToListAsync();
        foreach (var torrent in torrents)
        {
            statuses[torrent.MamId] = torrent.LibraryPath is null ? MamResultStatus.Downloaded : MamResultStatus.Linked;
        }

        return statuses;
    }

    /// <summary>
    /// Starts the selection for <paramref name="mamId"/>. From the Search page's htmx forms it answers with just the
    /// outcome message and the refreshed row, so the page is not reloaded; without htmx it redirects back to the page.
    /// </summary>
    public async Task<IActionResult> OnPostDownloadAsync(long mamId, bool wedge)
    {
        MamTorrentRecord? record = null;
        var failed = false;
        string message;
        try
        {
            var lookup = await mamClient.SearchAsync(new MamSearchQuery { Id = mamId, ReturnDownloadLink = true });
            record = lookup.Data.FirstOrDefault();
            if (record is not null)
            {
                var metadata = MamTorrentMapper.Map(record);
                var cost = wedge ? ConfiguredCost.Wedge : ConfiguredCost.Ratio;
                var outcome = await selectionRunner.ProcessCandidateAsync(
                    record, metadata, [], cost, autograbCategory: null, grabberName: "Manual search");
                logger.LogInformation("Manual download of {Torrent}: {Outcome}", metadata.DisplayName(), outcome.Describe(dryRun: false));
                message = DescribeOutcome(outcome, metadata, formatPreferenceLists);
            }
            else
            {
                message = $"MAM returned no torrent with id {mamId}.";
            }
        }
        catch (Exception ex) when (!IsRequestAborted(ex))
        {
            logger.LogError(
                "Manual download of {Torrent} failed: {Error}",
                record is null ? $"MAM id {mamId}" : MamTorrentMapper.Map(record).DisplayName(), ex.Message);
            failed = true;
            record = null;
            message = $"Download failed: {MamErrorDescriber.Describe(ex, mamOptions.Timeout)}";
        }

        if (!Request.Headers.ContainsKey("HX-Request"))
        {
            DownloadMessage = message;
            return RedirectToPage(new { Q });
        }

        // Set only on the redirect path above: TempData would otherwise show this message again on the next page load.
        SearchResultViewModel? row = null;
        if (record is not null)
        {
            var wedgeOverBytes = settings.Search.WedgeOver is { } wedgeOver ? MamSizeParser.ParseBytes(wedgeOver) : (long?)null;
            var statuses = await LoadStatusesAsync([record.MamId]);
            row = SearchResultViewModel.From(
                record, MamSizeParser.ParseBytes(record.SizeRaw), wedgeOverBytes,
                statuses.GetValueOrDefault(record.MamId, MamResultStatus.None));
        }

        return Partial("_DownloadFeedbackPartial", new DownloadFeedbackViewModel(message, row, IsError: failed));
    }

    /// <summary>True when the browser gave up on the request: nobody is left to show an error to, so let it propagate.</summary>
    private bool IsRequestAborted(Exception exception) =>
        exception is OperationCanceledException && HttpContext.RequestAborted.IsCancellationRequested;

    private static string DescribeOutcome(SelectionOutcome outcome, TorrentMetadata metadata, FormatPreferenceLists preferenceLists)
    {
        var title = $"\"{metadata.Title}\"";
        return outcome switch
        {
            SelectionOutcome.Selected => $"Selected {title}.",
            SelectionOutcome.RefreshedSelected => $"{title} is already selected.",
            SelectionOutcome.RefreshedLibrary => $"{title} is already downloaded.",
            SelectionOutcome.NoPreferredFormat =>
                $"{title} was not selected: none of its file types ({string.Join(", ", metadata.FileTypes)}) is in your "
                + $"{FormatPreferenceLists.SettingNameFor(metadata.MediaType)} setting ({string.Join(", ", preferenceLists.For(metadata.MediaType))}).",
            SelectionOutcome.DuplicateOfSelected => $"{title} was not selected: a copy is already selected.",
            SelectionOutcome.DuplicateOfLibrary => $"{title} was not selected: a copy is already in your library.",
            SelectionOutcome.CostNotFree => $"{title} was not selected: it is not free and the chosen cost does not allow it.",
            _ => $"{title} was not selected ({outcome}).",
        };
    }
}
