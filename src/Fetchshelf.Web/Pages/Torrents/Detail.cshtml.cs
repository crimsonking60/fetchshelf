using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Torrents;

public class DetailModel(
    FetchshelfDbContext dbContext, LinkRunner linkRunner, Cleaner cleaner,
    FetchshelfSettings settings, QBittorrentClientFactory qbittorrentFactory, MamClientOptions mamOptions,
    ILogger<DetailModel> logger) : PageModel
{
    public TorrentDetailViewModel? Torrent { get; private set; }

    /// <summary>What the last action did, shown once after the redirect back to this page.</summary>
    [TempData]
    public string? ActionMessage { get; set; }

    [TempData]
    public bool ActionFailed { get; set; }

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var loaded = await LoadAsync(id);
        if (loaded is null)
        {
            return NotFound();
        }

        Torrent = loaded;
        return Page();
    }

    public async Task<IActionResult> OnPostCleanAsync(string id)
    {
        await cleaner.CleanOneAsync(id, QBittorrentConnections.Build(settings, qbittorrentFactory));
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRefreshAsync(string id)
    {
        var editedByHand = await dbContext.Torrents.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => t.MetadataJson)
            .FirstOrDefaultAsync() is { } json && HandEdits.AreIn(TorrentMetadataSerializer.Deserialize(json));
        try
        {
            var outcome = await linkRunner.RefreshAsync(id);
            ActionFailed = outcome == RefreshOutcome.NotFoundOnMam;
            ActionMessage = (outcome, editedByHand) switch
            {
                (RefreshOutcome.NotFoundOnMam, _) => "MAM did not find this torrent, so nothing was refreshed.",
                (_, true) => "Replaced the details you edited by hand with MAM's.",
                (RefreshOutcome.Changed, false) => "Refreshed from MAM; the changes are listed in the history below.",
                _ => "Refreshed from MAM; nothing had changed.",
            };
        }
        catch (Exception ex) when (!IsRequestAborted(ex))
        {
            logger.LogWarning(ex, "Refresh of {TorrentId} failed", id);
            ActionFailed = true;
            ActionMessage = $"Refresh failed: {MamErrorDescriber.DescribeAmong(ex, "MAM or Audiobookshelf")}";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRefreshAndRelinkAsync(string id)
    {
        try
        {
            var result = await linkRunner.RefreshAndRelinkAsync(id, QBittorrentConnections.Build(settings, qbittorrentFactory));
            ActionMessage = result.Message;
            ActionFailed = !result.Succeeded;
        }
        catch (Exception ex) when (!IsRequestAborted(ex))
        {
            logger.LogWarning(ex, "Refresh and relink of {TorrentId} failed", id);
            ActionFailed = true;
            ActionMessage = $"Refresh and relink failed: {MamErrorDescriber.DescribeAmong(ex, "MAM, qBittorrent or Audiobookshelf")}";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUnlinkAsync(string id)
    {
        try
        {
            var result = await cleaner.UnlinkAsync(id, QBittorrentConnections.Build(settings, qbittorrentFactory));
            ActionMessage = result.Message;
            ActionFailed = !result.Succeeded;
        }
        catch (Exception ex) when (!IsRequestAborted(ex))
        {
            logger.LogWarning(ex, "Unlink of {TorrentId} failed", id);
            ActionFailed = true;
            ActionMessage = $"Unlinking failed: {MamErrorDescriber.DescribeAmong(ex, "qBittorrent or Audiobookshelf")}";
        }

        return RedirectToPage(new { id });
    }

    /// <summary>True when the browser gave up on the request: nobody is left to show an error to, so let it propagate.</summary>
    private bool IsRequestAborted(Exception exception) =>
        exception is OperationCanceledException && HttpContext.RequestAborted.IsCancellationRequested;

    public async Task<IActionResult> OnPostRemoveRecordAsync(string id)
    {
        var entity = await dbContext.Torrents.FindAsync(id);
        if (entity is not null)
        {
            dbContext.Torrents.Remove(entity);
            await dbContext.SaveChangesAsync();
        }

        return RedirectToPage("/Torrents/Index");
    }

    public async Task<IActionResult> OnPostClearReplacedAsync(string id)
    {
        var entity = await dbContext.Torrents.FindAsync(id);
        if (entity is not null)
        {
            entity.ReplacedWithTorrentId = null;
            entity.ReplacedAt = null;
            await dbContext.SaveChangesAsync();
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostStartAsync(string id)
    {
        await WithOwningClientAsync(id, client => client.StartAsync(id));
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostStopAsync(string id)
    {
        await WithOwningClientAsync(id, client => client.StopAsync(id));
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveWithDataAsync(string id)
    {
        await WithOwningClientAsync(id, client => client.DeleteWithDataAsync(id));
        return RedirectToPage("/Torrents/Index");
    }

    /// <summary>
    /// Sets the torrent's qBittorrent category and tags: <paramref name="tags"/> are the current tags left ticked, and
    /// <paramref name="newTags"/> is a comma-separated list of tags to add.
    /// </summary>
    public async Task<IActionResult> OnPostSetCategoryAndTagsAsync(string id, string? category, List<string> tags, string? newTags)
    {
        category ??= string.Empty;
        // Only a library's category, or the one the torrent already has, which the dropdown also offers.
        var currentCategory = await dbContext.Torrents.Where(t => t.Id == id).Select(t => t.QBittorrentCategory).FirstOrDefaultAsync();
        if (!LibraryCategories.IsLibraryCategory(settings, category) && category != (currentCategory ?? string.Empty)
            && !await HasCategoryInClientAsync(id, category))
        {
            return BadRequest();
        }

        tags = tags
            .Concat((newTags ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        await WithOwningClientAsync(id, async client =>
        {
            await client.SetCategoryAsync(id, category);
            var current = await client.GetTorrentAsync(id);
            if (current is not null)
            {
                var toRemove = current.TagList.Except(tags, StringComparer.OrdinalIgnoreCase).ToList();
                var toAdd = tags.Except(current.TagList, StringComparer.OrdinalIgnoreCase).ToList();
                if (toRemove.Count > 0)
                {
                    await client.RemoveTagsAsync(id, toRemove);
                }

                if (toAdd.Count > 0)
                {
                    await client.AddTagsAsync(id, toAdd);
                }
            }
        });
        return RedirectToPage(new { id });
    }

    /// <summary>The library categories the category dropdown offers.</summary>
    public IReadOnlyList<LibraryCategory> CategoryOptions => LibraryCategories.For(settings);

    private async Task<bool> HasCategoryInClientAsync(string hash, string category)
    {
        var found = false;
        await WithOwningClientAsync(hash, async client =>
            found = (await client.GetTorrentAsync(hash))?.Category == category);
        return found;
    }

    private async Task WithOwningClientAsync(string hash, Func<QBittorrentClient, Task> action)
    {
        var connections = QBittorrentConnections.Build(settings, qbittorrentFactory);
        var client = await QBittorrentConnectionResolver.ResolveAsync(connections.Select(c => c.Client).ToList(), hash);
        if (client is not null)
        {
            await action(client);
        }
    }

    private async Task<TorrentDetailViewModel?> LoadAsync(string id)
    {
        var entity = await dbContext.Torrents.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (entity is null)
        {
            return null;
        }

        // Sorted in memory: the SQLite EF Core provider cannot translate ORDER BY over a
        // DateTimeOffset column (see the same note in Pages/Torrents/Index.cshtml.cs).
        var matchedEvents = await dbContext.Events.AsNoTracking().Where(e => e.TorrentId == id).ToListAsync();
        var events = matchedEvents.OrderByDescending(e => e.OccurredAt).ToList();

        QBittorrentTorrent? clientState = null;
        try
        {
            var connections = QBittorrentConnections.Build(settings, qbittorrentFactory);
            var client = await QBittorrentConnectionResolver.ResolveAsync(connections.Select(c => c.Client).ToList(), id);
            if (client is not null)
            {
                clientState = await client.GetTorrentAsync(id);
            }
        }
        catch (HttpRequestException)
        {
            // qBittorrent unreachable: the detail page still shows the stored state.
        }

        return TorrentDetailViewModel.From(entity, events, clientState, settings.Audiobookshelf?.Url, mamOptions.BaseUrl);
    }
}
