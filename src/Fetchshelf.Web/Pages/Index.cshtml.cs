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
/// The Search page, Fetchshelf-spec.md §10.4: manual MaM search with the shared filter, cost badges, and
/// download / download-with-wedge actions that start a selection via tag rules. Also serves as the
/// application's home page ("/").
/// </summary>
public class IndexModel(
    MamClient mamClient, SelectionRunner selectionRunner, FetchshelfSettings settings, FetchshelfDbContext dbContext,
    ILogger<IndexModel> logger) : PageModel
{
    /// <summary>What happened to the last Download click, shown once after the redirect back to this page.</summary>
    [TempData]
    public string? DownloadMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public IReadOnlyList<SearchResultViewModel> Results { get; private set; } = [];

    public async Task OnGetAsync()
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
        var result = await mamClient.SearchAsync(query);
        var wedgeOverBytes = settings.Search.WedgeOver is { } wedgeOver ? MamSizeParser.ParseBytes(wedgeOver) : (long?)null;
        var statusByMamId = await LoadStatusesAsync(result.Data.Select(record => record.MamId).ToList());

        Results = result.Data
            .Select(record => SearchResultViewModel.From(
                record, MamSizeParser.ParseBytes(record.SizeRaw), wedgeOverBytes,
                statusByMamId.GetValueOrDefault(record.MamId, MamResultStatus.None)))
            .ToList();
    }

    /// <summary>
    /// Resolves each given MaM id to where it stands in Fetchshelf's pipeline: linked into the
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

    public async Task<IActionResult> OnPostDownloadAsync(long mamId, bool wedge)
    {
        var lookup = await mamClient.SearchAsync(new MamSearchQuery { Id = mamId, ReturnDownloadLink = true });
        var record = lookup.Data.FirstOrDefault();
        if (record is not null)
        {
            var metadata = MamTorrentMapper.Map(record);
            var cost = wedge ? ConfiguredCost.Wedge : ConfiguredCost.Ratio;
            var outcome = await selectionRunner.ProcessCandidateAsync(
                record, metadata, [], cost, autogrebCategory: null, grabberName: "Manual search",
                unsatBuffer: settings.UnsatBuffer, wedgeBuffer: settings.WedgeBuffer);
            logger.LogInformation("Manual download of MaM id {MamId} ended with outcome {Outcome}", mamId, outcome);
            DownloadMessage = DescribeOutcome(outcome, metadata, settings);
        }
        else
        {
            DownloadMessage = $"MaM returned no torrent with id {mamId}.";
        }

        return RedirectToPage(new { Q });
    }

    private static string DescribeOutcome(SelectionOutcome outcome, TorrentMetadata metadata, FetchshelfSettings settings)
    {
        var title = $"\"{metadata.Title}\"";
        return outcome switch
        {
            SelectionOutcome.Selected => $"Selected {title}.",
            SelectionOutcome.RefreshedSelected => $"{title} is already selected.",
            SelectionOutcome.RefreshedLibrary => $"{title} is already downloaded.",
            SelectionOutcome.NoPreferredFormat =>
                $"{title} was not selected: none of its file types ({string.Join(", ", metadata.FileTypes)}) is in your "
                + $"{PreferenceSettingFor(metadata.MediaType, settings, out var preferred)} setting ({string.Join(", ", preferred)}).",
            SelectionOutcome.DuplicateOfSelected => $"{title} was not selected: a copy is already selected.",
            SelectionOutcome.DuplicateOfLibrary => $"{title} was not selected: a copy is already in your library.",
            SelectionOutcome.CostNotFree => $"{title} was not selected: it is not free and the chosen cost does not allow it.",
            _ => $"{title} was not selected ({outcome}).",
        };
    }

    private static string PreferenceSettingFor(MediaType mediaType, FetchshelfSettings settings, out IReadOnlyList<string> preferred)
    {
        switch (mediaType)
        {
            case MediaType.Audiobook or MediaType.PeriodicalAudiobook:
                preferred = settings.AudioTypes;
                return "audio_types";
            case MediaType.Musicology:
                preferred = settings.MusicTypes;
                return "music_types";
            case MediaType.Radio:
                preferred = settings.RadioTypes;
                return "radio_types";
            default:
                preferred = settings.EbookTypes;
                return "ebook_types";
        }
    }
}
