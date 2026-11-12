using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

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
        if (await OriginalMetadataAsync(id) is { } metadata)
        {
            Input = EditInputModel.From(metadata);
        }
    }

    public async Task<IActionResult> OnPostAsync(string id)
    {
        Id = id;
        var original = await OriginalMetadataAsync(id);
        await linkRunner.SetManualMetadataAsync(id, Input.ToMetadata(original), original);

        return RedirectToPage("/Torrents/Detail", new { id });
    }

    /// <summary>
    /// The metadata the form starts from: the torrent's record, or for a torrent without one, what MAM said about
    /// it when the Link job refused it for having no author.
    /// </summary>
    private async Task<TorrentMetadata?> OriginalMetadataAsync(string id)
    {
        if (await dbContext.Torrents.FindAsync(id) is { } entity)
        {
            return TorrentMetadataSerializer.Deserialize(entity.MetadataJson);
        }

        var linkError = await dbContext.ErroredTorrents
            .FirstOrDefaultAsync(e => e.Stage == ErrorStage.Linker && e.Key == id && e.DisplayMetadataJson != null);
        return linkError is null ? null : TorrentMetadataSerializer.Deserialize(linkError.DisplayMetadataJson!);
    }
}
