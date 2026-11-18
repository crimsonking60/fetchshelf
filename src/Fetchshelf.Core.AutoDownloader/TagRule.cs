namespace Fetchshelf.Core.AutoDownloader;

/// <summary>One `[[tag]]` rule's matching-relevant fields.</summary>
public sealed record TagRule
{
    public required TorrentFilter Filter { get; init; }

    public string? Category { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>`client`: the `name` of the `[[qbittorrent]]` that receives what the rule matches.</summary>
    public string? Client { get; init; }

    /// <summary>`save_path`: the folder qBittorrent saves what the rule matches in, as qBittorrent sees it.</summary>
    public string? SavePath { get; init; }
}

/// <summary>
/// Where a download goes: its qBittorrent category, which `[[qbittorrent]]` (by `name`) receives it, and the folder it is
/// saved in. Each is null when unset: no category, the first `[[qbittorrent]]`, and the category's or qBittorrent's
/// default folder.
/// </summary>
public sealed record DownloadPlacement(string? Category = null, string? Client = null, string? SavePath = null)
{
    public static readonly DownloadPlacement None = new();
}

/// <summary>Resolves a selected torrent's category, client, save path and tags from tag rules.</summary>
public static class TagRuleResolver
{
    /// <summary>
    /// Category, client and save path are each the block's own if set, else the first matching rule's that sets one.
    /// Tags are the union of tags from every matching rule.
    /// </summary>
    public static (DownloadPlacement Placement, IReadOnlySet<string> Tags) Resolve(
        TorrentFilterCandidate candidate, DownloadPlacement own, IReadOnlyList<TagRule> tagRules)
    {
        var (category, client, savePath) = (own.Category, own.Client, own.SavePath);
        var tags = new HashSet<string>();

        foreach (var rule in tagRules)
        {
            if (!TorrentFilterMatcher.Matches(rule.Filter, candidate))
            {
                continue;
            }

            category ??= rule.Category;
            client ??= rule.Client;
            savePath ??= rule.SavePath;

            foreach (var tag in rule.Tags)
            {
                tags.Add(tag);
            }
        }

        return (new DownloadPlacement(category, client, savePath), tags);
    }
}
