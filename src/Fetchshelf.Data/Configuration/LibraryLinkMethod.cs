namespace Fetchshelf.Data.Configuration;

/// <summary>How a linked file is placed into the library, Fetchshelf-spec.md §8.1.</summary>
public enum LibraryLinkMethod
{
    Hardlink,
    HardlinkOrCopy,
    HardlinkOrSymlink,
    Copy,
    Symlink,
    NoLink,
}
