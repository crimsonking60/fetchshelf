using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web.Pages.Following;

/// <summary>
/// The `[[follow.grab]]` blocks in plain words, for the top of the Following page: what the Follow job downloads by
/// itself for a followed series or author, so a book left as *Not free* is no surprise. Names the cost, whether a block
/// takes only ebooks or only audiobooks, and its languages; any other filter is mentioned, not spelled out.
/// </summary>
public static class FollowGrabSummary
{
    /// <summary>One sentence per grab block, in the order the Follow job tries them.</summary>
    public static IReadOnlyList<string> Describe(IReadOnlyList<GrabEntry> blocks) => blocks.Select(Describe).ToList();

    public static string Describe(GrabEntry block)
    {
        var parts = new List<string> { Cost(block.Cost) };
        if (Formats(block.Categories) is { } formats)
        {
            parts.Add(formats);
        }

        if (block.Languages is { Count: > 0 } languages)
        {
            parts.Add("in " + JoinWithOr(languages.Select(Capitalize).ToList()));
        }

        if (HasOtherFilters(block))
        {
            parts.Add("if the block's other filters allow it");
        }

        return string.Join(", ", parts) + ".";
    }

    private static string Cost(ConfiguredCost cost) => cost switch
    {
        ConfiguredCost.Free => "Only books that are free (freeleech, VIP or personal freeleech)",
        ConfiguredCost.Wedge => "Free books, and others with a freeleech wedge",
        ConfiguredCost.TryWedge => "Free books, others with a freeleech wedge while there are wedges to spare, and otherwise paid with ratio",
        ConfiguredCost.Ratio => "Any book: free ones cost nothing, others are paid with ratio",
        _ => cost.ToString(),
    };

    /// <summary>"ebooks only" or "audiobooks only" when a block leaves out the other main type; null when it takes both.</summary>
    private static string? Formats(CategoriesConfig categories)
    {
        var audio = categories.Audio.Names is not { Count: 0 };
        var ebook = categories.Ebook.Names is not { Count: 0 };
        return (audio, ebook) switch
        {
            (false, true) => "ebooks only",
            (true, false) => "audiobooks only",
            _ => null,
        };
    }

    /// <summary>Whether the block narrows what it takes in a way <see cref="Describe(GrabEntry)"/> doesn't spell out.</summary>
    private static bool HasOtherFilters(GrabEntry block) =>
        block.MediaType.Count > 0
        || block.Categories.Audio.Names is { Count: > 0 } || block.Categories.Ebook.Names is { Count: > 0 }
        || block.Categories.Musicology.Names is not { Count: 0 } || block.Categories.Radio.Names is not { Count: 0 }
        || block.Flags.Count > 0 || block.MinSize is not null || block.MaxSize is not null || block.ExcludeUploader.Count > 0
        || block.UploadedAfter is not null || block.UploadedBefore is not null
        || block.MinSeeders is not null || block.MaxSeeders is not null || block.MinLeechers is not null || block.MaxLeechers is not null
        || block.MinSnatched is not null || block.MaxSnatched is not null || block.MaxAuthors is not null;

    private static string Capitalize(string language) =>
        language.Length == 0 ? language : char.ToUpperInvariant(language[0]) + language[1..];

    private static string JoinWithOr(IReadOnlyList<string> items) =>
        items.Count == 1 ? items[0] : $"{string.Join(", ", items.Take(items.Count - 1))} or {items[^1]}";
}
