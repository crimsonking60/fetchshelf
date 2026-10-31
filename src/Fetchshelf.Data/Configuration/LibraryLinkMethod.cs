namespace Fetchshelf.Data.Configuration;

/// <summary>How a linked file is placed into the library.</summary>
public enum LibraryLinkMethod
{
    Hardlink,
    HardlinkOrCopy,
    HardlinkOrSymlink,
    Copy,
    Symlink,
    NoLink,
}
