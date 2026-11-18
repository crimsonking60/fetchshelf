namespace Fetchshelf.Data.Configuration;

/// <summary>One `[[tag]]` entry: the shared filter plus optional category/tags.</summary>
public sealed class TagRuleEntry : SharedFilterConfig
{
    public string? Category { get; set; }

    /// <summary>`client`: the `name` of the `[[qbittorrent]]` that receives what this rule matches; unset means the first one.</summary>
    public string? Client { get; set; }

    /// <summary>`save_path`: the folder qBittorrent saves what this rule matches in, as qBittorrent sees it; unset leaves it to qBittorrent.</summary>
    public string? SavePath { get; set; }

    public List<string> Tags { get; set; } = [];
}
