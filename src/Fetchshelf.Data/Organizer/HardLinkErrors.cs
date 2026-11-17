namespace Fetchshelf.Data.Organizer;

/// <summary>
/// A hardlink this file system can't make here: the source is on another device, or the file system has no hardlinks
/// or none left for the file. <c>hardlink_or_copy</c> and <c>hardlink_or_symlink</c> fall back on this, and only on this.
/// </summary>
public sealed class HardLinkNotPossibleException(string message) : IOException(message);

/// <summary>The Linux errno values <c>link()</c> fails with, by name, and which of them mean a hardlink isn't possible here.</summary>
public static class HardLinkErrors
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [1] = "EPERM",
        [2] = "ENOENT",
        [5] = "EIO",
        [13] = "EACCES",
        [17] = "EEXIST",
        [18] = "EXDEV",
        [20] = "ENOTDIR",
        [28] = "ENOSPC",
        [30] = "EROFS",
        [31] = "EMLINK",
        [36] = "ENAMETOOLONG",
        [38] = "ENOSYS",
        [40] = "ELOOP",
        [95] = "EOPNOTSUPP",
        [122] = "EDQUOT",
    };

    /// <summary>
    /// Another device (<c>EXDEV</c>), a file system that doesn't allow hardlinks (<c>EPERM</c>, <c>EOPNOTSUPP</c>, and
    /// <c>ENOSYS</c> from some FUSE and network file systems), and a file at its link limit (<c>EMLINK</c>). Anything
    /// else, such as a full disk or a permission problem, would make a copy or a symlink fail too, or half succeed, and
    /// is reported as itself.
    /// </summary>
    private static readonly HashSet<int> NotPossibleHere = [1, 18, 31, 38, 95];

    /// <summary>The errno's name, such as <c>ENOSPC</c>; the number itself for one not in the table.</summary>
    public static string NameOf(int errno) => Names.TryGetValue(errno, out var name) ? name : errno.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The exception for a failed <c>link()</c>: <see cref="HardLinkNotPossibleException"/> when a hardlink can't be made
    /// here, a plain <see cref="IOException"/> naming the cause otherwise.
    /// </summary>
    public static IOException For(int errno, string sourcePath, string destinationPath)
    {
        var message = $"Hardlink failed ({NameOf(errno)}, errno {errno}) linking '{sourcePath}' to '{destinationPath}'.";
        return NotPossibleHere.Contains(errno) ? new HardLinkNotPossibleException(message) : new IOException(message);
    }
}
