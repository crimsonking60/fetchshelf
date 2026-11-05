using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Lists;

/// <summary>One row on the Lists page.</summary>
public sealed record ListSummaryViewModel(string Id, string Name, int Books, int Missing, DateTimeOffset? UpdatedAt);

/// <summary>
/// The Lists page: every configured `[[hardcover_list]]` with how many of its books still miss a format, plus any
/// list in the database whose entry has been removed from the config, shown separately.
/// </summary>
public class IndexModel(FetchshelfDbContext dbContext, FetchshelfSettings settings) : PageModel
{
    public IReadOnlyList<ListSummaryViewModel> ConfiguredLists { get; private set; } = [];

    public IReadOnlyList<ListSummaryViewModel> InactiveLists { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var stored = await dbContext.Lists.AsNoTracking().ToDictionaryAsync(l => l.Id);
        var items = await dbContext.ListItems.AsNoTracking()
            .Where(i => i.RemovedAt == null)
            .Select(i => new { i.ListId, i.AllowAudio, i.AudioStatus, i.AllowEbook, i.EbookStatus, i.MarkedDoneAt })
            .ToListAsync();
        var counts = items.GroupBy(i => i.ListId).ToDictionary(
            g => g.Key,
            g => (Books: g.Count(), Missing: g.Count(i => i.MarkedDoneAt == null
                && ((i.AllowAudio && !ListItemSideStatus.IsResolved(i.AudioStatus)) || (i.AllowEbook && !ListItemSideStatus.IsResolved(i.EbookStatus))))));

        ListSummaryViewModel Summary(string id, string name)
        {
            var (books, missing) = counts.GetValueOrDefault(id);
            return new ListSummaryViewModel(id, name, books, missing, stored.GetValueOrDefault(id)?.UpdatedAt);
        }

        var configuredIds = settings.HardcoverList.Select(HardcoverListImporter.ListIdFor).ToHashSet(StringComparer.Ordinal);
        ConfiguredLists = settings.HardcoverList
            .Select(entry => Summary(HardcoverListImporter.ListIdFor(entry), HardcoverListImporter.DisplayNameFor(entry)))
            .DistinctBy(l => l.Id)
            .ToList();
        InactiveLists = stored.Values
            .Where(l => !configuredIds.Contains(l.Id))
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Select(l => Summary(l.Id, l.Name))
            .ToList();
    }
}
