using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fetchshelf.Web.Pages.Config;

/// <summary>
/// The Config page, Fetchshelf-spec.md §10.4: a read-only view of the active configuration (secrets
/// hidden), a trigger link per `[[autograb]]` entry, and an "apply to existing torrents" action per
/// `[[tag]]` rule.
/// </summary>
public class IndexModel(FetchshelfSettings settings, RetroactiveTagApplier retroactiveTagApplier) : PageModel
{
    public const string HiddenPlaceholder = "(hidden)";

    public FetchshelfSettings Settings => settings;

    public void OnGet()
    {
    }

    /// <summary>The "apply to existing torrents" action for one `[[tag]]` rule.</summary>
    public async Task<IActionResult> OnPostApplyTagRuleAsync(int ruleIndex)
    {
        if (ruleIndex < 0 || ruleIndex >= settings.Tag.Count)
        {
            return NotFound();
        }

        var entry = settings.Tag[ruleIndex];
        var rule = new TagRule
        {
            Filter = SharedFilterConfigMapper.ToTorrentFilter(entry),
            Category = entry.Category,
            Tags = entry.Tags,
        };
        await retroactiveTagApplier.ApplyAsync(rule);

        return RedirectToPage();
    }
}
