namespace Fetchshelf.Data.Configuration;

/// <summary>
/// One `[[library]]` entry: maps finished torrents to an output folder.
/// Exactly one of <see cref="Category"/> or <see cref="DownloadDir"/> must be set, never both.
/// </summary>
public sealed class Library
{
    public string LibraryDir { get; set; } = string.Empty;

    public string? Category { get; set; }

    public string? DownloadDir { get; set; }

    public string? Name { get; set; }

    private LibraryLinkMethod? _method;

    /// <summary>How files are placed. Falls back to the global `link_method` when the library sets none.</summary>
    public LibraryLinkMethod Method
    {
        get => _method ?? LibraryLinkMethod.HardlinkOrCopy;
        set => _method = value;
    }

    /// <summary>True when this library's own `method` was set, so the global `link_method` must not replace it.</summary>
    internal bool HasOwnMethod => _method is not null;

    public List<string> AllowTags { get; set; } = [];

    public List<string> DenyTags { get; set; } = [];

    /// <summary>Overrides the global `audio_types` preference list for this library when non-null.</summary>
    public List<string>? AudioTypes { get; set; }

    /// <summary>Overrides the global `ebook_types` preference list for this library when non-null.</summary>
    public List<string>? EbookTypes { get; set; }
}
