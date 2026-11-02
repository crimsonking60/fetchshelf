using System.Runtime.InteropServices;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Organizer;

/// <summary>What <see cref="FileLinker.Link"/> actually did with one file, after any fallback.</summary>
public enum LinkOutcome
{
    Hardlinked,
    Copied,
    Symlinked,
    AlreadyExists,

    /// <summary>The method is <see cref="LibraryLinkMethod.NoLink"/>: no file was placed.</summary>
    NotPlaced,
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
                return LinkOutcome.Hardlinked;
            case LibraryLinkMethod.HardlinkOrCopy:
                return TryOrFallback(
                    () => hardLink(sourcePath, destinationPath), LinkOutcome.Hardlinked,
                    () => File.Copy(sourcePath, destinationPath), LinkOutcome.Copied);
            case LibraryLinkMethod.HardlinkOrSymlink:
                return TryOrFallback(
                    () => hardLink(sourcePath, destinationPath), LinkOutcome.Hardlinked,
                    () => File.CreateSymbolicLink(destinationPath, sourcePath), LinkOutcome.Symlinked);
            case LibraryLinkMethod.Copy:
                File.Copy(sourcePath, destinationPath);
                return LinkOutcome.Copied;
            case LibraryLinkMethod.Symlink:
                File.CreateSymbolicLink(destinationPath, sourcePath);
                return LinkOutcome.Symlinked;
            case LibraryLinkMethod.NoLink:
                return LinkOutcome.NotPlaced;
            default:
                throw new ArgumentOutOfRangeException(nameof(method), method, null);
        }
    }

    private static LinkOutcome TryOrFallback(Action primary, LinkOutcome primaryOutcome, Action fallback, LinkOutcome fallbackOutcome)
    {
        try
        {
            primary();
            return primaryOutcome;
        }
        catch (IOException)
        {
            fallback();
            return fallbackOutcome;
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
