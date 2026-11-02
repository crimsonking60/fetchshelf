namespace Fetchshelf.Core.AutoDownloader;

/// <summary>One `[[tag]]` rule's matching-relevant fields.</summary>
public sealed record TagRule
{
    public required TorrentFilter Filter { get; init; }

    public string? Category { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>Resolves a selected torrent's category and tags from tag rules.</summary>
public static class TagRuleResolver
{
    /// <summary>
    /// Category is the autograbber's own category if set, else the first matching rule's category.
    /// Tags are the union of tags from every matching rule.
    /// </summary>
    public static (string? Category, IReadOnlySet<string> Tags) Resolve(
        TorrentFilterCandidate candidate, string? autograbCategory, IReadOnlyList<TagRule> tagRules)
    {
        string? category = autograbCategory;
        var tags = new HashSet<string>();

        foreach (var rule in tagRules)
        {
            if (!TorrentFilterMatcher.Matches(rule.Filter, candidate))
            {
                continue;
            }

            if (category is null && rule.Category is not null)
            {
                category = rule.Category;
            }

            foreach (var tag in rule.Tags)
            {
                tags.Add(tag);
            }
        }

        return (category, tags);
    }
}
