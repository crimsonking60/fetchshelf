using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Organizer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fetchshelf.Web.Pages.Torrents;

public class EditModel(FetchshelfDbContext dbContext, LinkRunner linkRunner) : PageModel
{
    public string Id { get; private set; } = string.Empty;

    [BindProperty]
    public EditInputModel Input { get; set; } = new();

    public IEnumerable<Language> Languages => Enum.GetValues<Language>();

    public IEnumerable<LegacyCategory> Categories => Enum.GetValues<LegacyCategory>();

    public IEnumerable<BookFlags> AllFlags => Enum.GetValues<BookFlags>().Where(f => f != BookFlags.None);

    public async Task OnGetAsync(string id)
    {
        Id = id;
        var entity = await dbContext.Torrents.FindAsync(id);
        if (entity is not null)
        {
            Input = EditInputModel.From(TorrentMetadataSerializer.Deserialize(entity.MetadataJson));
        }
    }

    public async Task<IActionResult> OnPostAsync(string id)
    {
        Id = id;
        var existing = await dbContext.Torrents.FindAsync(id);
        var mamId = existing is not null ? TorrentMetadataSerializer.Deserialize(existing.MetadataJson).MamId : 0;
        var uploadTime = existing?.CreatedAt ?? DateTimeOffset.UtcNow;

        await linkRunner.SetManualMetadataAsync(id, Input.ToMetadata(mamId, uploadTime));

        return RedirectToPage("/Torrents/Detail", new { id });
    }
}
