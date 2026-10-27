using Fetchshelf.Clients.Goodreads;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Lists;

/// <summary>
/// The Lists page (`/lists`), Fetchshelf-spec.md §10.4, §7.8: every configured Goodreads list plus any list
/// in the database whose config entry has been removed, shown separately as inactive.
/// </summary>
public class IndexModel(FetchshelfDbContext dbContext, FetchshelfSettings settings) : PageModel
{
    public IReadOnlyList<ListSummaryViewModel> ConfiguredLists { get; private set; } = [];

    public IReadOnlyList<ListSummaryViewModel> InactiveLists { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var dbLists = await dbContext.Lists.ToDictionaryAsync(l => l.Id);
        var configuredIds = new HashSet<string>();
        var configured = new List<ListSummaryViewModel>();

        foreach (var entry in settings.GoodreadsList)
        {
            string id;
            try
            {
                id = GoodreadsListId.Derive(entry.Url);
            }
            catch (FormatException)
            {
                continue;
            }

            configuredIds.Add(id);
            dbLists.TryGetValue(id, out var dbList);
            var name = string.IsNullOrWhiteSpace(entry.Name) ? dbList?.Name ?? id : entry.Name;
            configured.Add(new ListSummaryViewModel(id, name, dbList?.UpdatedAt));
        }

        ConfiguredLists = configured;
        InactiveLists = dbLists.Values
            .Where(l => !configuredIds.Contains(l.Id))
            .Select(l => new ListSummaryViewModel(l.Id, l.Name, l.UpdatedAt))
            .ToList();
    }
}
