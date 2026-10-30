using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Torrents;

/// <summary>The Torrent detail page, Fetchshelf-spec.md §10.3.</summary>
public class DetailModel(
    FetchshelfDbContext dbContext, LinkRunner linkRunner, Cleaner cleaner,
    FetchshelfSettings settings, QBittorrentClientFactory qbittorrentFactory, MamClientOptions mamOptions) : PageModel
{
    public TorrentDetailViewModel? Torrent { get; private set; }

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
        await linkRunner.RefreshAsync(id, relink: false);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRefreshAndRelinkAsync(string id)
    {
        await linkRunner.RefreshAsync(id, relink: true);
        return RedirectToPage(new { id });
    }

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
        tags = tags
            .Concat((newTags ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        await WithOwningClientAsync(id, async client =>
        {
            await client.SetCategoryAsync(id, category);
            var current = (await client.ListTorrentsAsync()).FirstOrDefault(t => t.Hash == id);
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
                clientState = (await client.ListTorrentsAsync()).FirstOrDefault(t => t.Hash == id);
            }
        }
        catch (HttpRequestException)
        {
            // qBittorrent unreachable: detail page still shows stored state (§10.3: "when available").
        }

        return TorrentDetailViewModel.From(entity, events, clientState, settings.Audiobookshelf?.Url, mamOptions.BaseUrl);
    }
}
