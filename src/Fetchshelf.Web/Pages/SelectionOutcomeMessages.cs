using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.AutoDownloader;

namespace Fetchshelf.Web.Pages;

/// <summary>What a Download click on the Search or Following page says about the selection it started.</summary>
public static class SelectionOutcomeMessages
{
    public static string Describe(
        SelectionOutcome outcome, TorrentMetadata metadata, FormatPreferenceLists preferenceLists, bool downloadStarted)
    {
        var title = $"\"{metadata.Title}\"";
        return outcome switch
        {
            SelectionOutcome.Selected => downloadStarted
                ? $"Selected {title}; the Download job is starting it now."
                : $"Selected {title}; it starts at the next Download run.",
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
