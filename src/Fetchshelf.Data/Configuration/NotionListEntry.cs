namespace Fetchshelf.Data.Configuration;

/// <summary>One `[[notion_list]]` entry, Fetchshelf-spec.md §4.2, §7.8.</summary>
public sealed class NotionListEntry
{
    public string DataSource { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Names of the row's URL-type properties that encode a MaM id as their trailing path segment.</summary>
    public List<string> MamFields { get; set; } = [];

    public List<GrabEntry> Grab { get; set; } = [];

    public int? SearchInterval { get; set; }

    public int? UnsatBuffer { get; set; }

    public int? WedgeBuffer { get; set; }

    public bool DryRun { get; set; }
}
