using System.Globalization;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Duplicates;

/// <summary>The Duplicates page: torrents skipped because an equal or better copy exists.</summary>
public class IndexModel(FetchshelfDbContext dbContext, MamClientOptions mamOptions) : PageModel
{
    public IReadOnlyList<DuplicateViewModel> Duplicates { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var entities = await dbContext.DuplicateTorrents.AsNoTracking().ToListAsync();

        // A duplicate of a library torrent stores that torrent's hash; a duplicate of a selection stores its MaM id.
        var keys = entities.Select(e => e.DuplicateOfTorrentId).Distinct().ToList();
        var mamIds = keys.Select(k => long.TryParse(k, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0).ToList();
        var torrentsById = await dbContext.Torrents.AsNoTracking()
            .Where(t => keys.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Title);
        var torrentsByMamId = (await dbContext.Torrents.AsNoTracking()
                .Where(t => mamIds.Contains(t.MamId))
                .Select(t => new { t.MamId, t.Id, t.Title })
                .ToListAsync())
            .DistinctBy(t => t.MamId)
            .ToDictionary(t => t.MamId);
        var selectionTitles = (await dbContext.SelectedTorrents.AsNoTracking()
                .Where(s => mamIds.Contains(s.MamId))
                .Select(s => new { s.MamId, s.MetadataJson })
                .ToListAsync())
            .DistinctBy(s => s.MamId)
            .ToDictionary(s => s.MamId, s => Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Deserialize(s.MetadataJson).Title);

        DuplicateOfViewModel Resolve(string key)
        {
            if (torrentsById.TryGetValue(key, out var title))
            {
                return new DuplicateOfViewModel(title, key, null);
            }

            if (!long.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var mamId))
            {
                return new DuplicateOfViewModel(key, null, null);
            }

            var mamLink = DuplicateViewModel.MamTorrentLink(mamOptions.BaseUrl, mamId);
            if (torrentsByMamId.TryGetValue(mamId, out var torrent))
            {
                return new DuplicateOfViewModel(torrent.Title, torrent.Id, mamLink);
            }

            return selectionTitles.TryGetValue(mamId, out var selectionTitle)
                ? new DuplicateOfViewModel($"{selectionTitle} (selected)", null, mamLink)
                : new DuplicateOfViewModel($"MaM torrent {mamId}", null, mamLink);
        }

        Duplicates = entities
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => DuplicateViewModel.From(e, Resolve(e.DuplicateOfTorrentId), mamOptions.BaseUrl))
            .ToList();
    }

    public async Task<IActionResult> OnPostRemoveSelectedAsync(List<int> selectedIds)
    {
        await dbContext.DuplicateTorrents.Where(d => selectedIds.Contains(d.Id)).ExecuteDeleteAsync();
        return RedirectToPage();
    }
}
