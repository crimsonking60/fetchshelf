using System.Runtime.InteropServices;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Organizer;

public enum LinkOutcome
{
    Linked,
    AlreadyExists,
}

/// <summary>
/// Links a single file into the library per its configured <see cref="LibraryLinkMethod"/>,
/// </summary>
public static class FileLinker
{
    /// <summary>
    /// <paramref name="hardLink"/> defaults to the real POSIX `link()` syscall; tests may inject a
    /// fake to simulate a hardlink failure (e.g. cross-device) deterministically.
    /// </summary>
    public static LinkOutcome Link(
        string sourcePath, string destinationPath, LibraryLinkMethod method, Action<string, string>? hardLink = null)
    {
        hardLink ??= CreateHardLink;

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        if (File.Exists(destinationPath))
        {
            return LinkOutcome.AlreadyExists;
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Cannot link: source file not found: '{sourcePath}'.", sourcePath);
        }

        switch (method)
        {
            case LibraryLinkMethod.Hardlink:
                hardLink(sourcePath, destinationPath);
                break;
            case LibraryLinkMethod.HardlinkOrCopy:
                TryOrFallback(() => hardLink(sourcePath, destinationPath), () => File.Copy(sourcePath, destinationPath));
                break;
            case LibraryLinkMethod.HardlinkOrSymlink:
                TryOrFallback(() => hardLink(sourcePath, destinationPath), () => File.CreateSymbolicLink(destinationPath, sourcePath));
                break;
            case LibraryLinkMethod.Copy:
                File.Copy(sourcePath, destinationPath);
                break;
            case LibraryLinkMethod.Symlink:
                File.CreateSymbolicLink(destinationPath, sourcePath);
                break;
            case LibraryLinkMethod.NoLink:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(method), method, null);
        }

        return LinkOutcome.Linked;
    }

    private static void TryOrFallback(Action primary, Action fallback)
    {
        try
        {
            primary();
        }
        catch (IOException)
        {
            fallback();
        }
    }

    /// <summary>
    /// No cross-platform BCL API creates hardlinks; the deployment target is Linux-only (Docker),
    /// so this P/Invokes the POSIX `link()` syscall directly.
    /// </summary>
    private static void CreateHardLink(string sourcePath, string destinationPath)
    {
        var result = link(sourcePath, destinationPath);
        if (result != 0)
        {
            var errno = Marshal.GetLastWin32Error();
            throw new IOException(
                $"Hardlink failed (errno {errno}) linking '{sourcePath}' to '{destinationPath}'.");
        }
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "link")]
    private static extern int link(string oldpath, string newpath);
}
