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

/// <summary>What <see cref="FileLinker.Link"/> did with one file, and where the file ended up.</summary>
/// <param name="PlacedAt">
/// The path the file is at: the destination asked for, or, when a different file already had that name, the free
/// name it was placed under instead (<c>Book (2).epub</c>).
/// </param>
public sealed record LinkResult(LinkOutcome Outcome, string PlacedAt);

/// <summary>
/// Links a single file into the library per its configured <see cref="LibraryLinkMethod"/>,
/// </summary>
public static class FileLinker
{
    private const string PartialSuffix = ".fetchshelf-partial";

    /// <summary>
    /// Places <paramref name="sourcePath"/> at <paramref name="destinationPath"/>. When something is already there, it
    /// is looked at: the same file (a symlink to the source, or a file of the source's size) counts as already placed;
    /// a different file is left alone, and the source goes under the first free name of the form <c>name (2).ext</c>
    /// instead, again counting a same file found there as already placed. A copy is written under a temporary name
    /// and renamed into place, so an interrupted copy never sits at the real name.
    /// <paramref name="hardLink"/> defaults to the real POSIX `link()` syscall; tests may inject a
    /// fake to simulate a hardlink failure (e.g. cross-device) deterministically.
    /// </summary>
    public static LinkResult Link(
        string sourcePath, string destinationPath, LibraryLinkMethod method, Action<string, string>? hardLink = null)
    {
        hardLink ??= CreateHardLink;

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        if (!File.Exists(sourcePath))
        {
            // Nothing to compare with: what is there already is taken as the file, as before.
            return Occupied(destinationPath)
                ? new LinkResult(LinkOutcome.AlreadyExists, destinationPath)
                : throw new FileNotFoundException($"Cannot link: source file not found: '{sourcePath}'.", sourcePath);
        }

        var target = destinationPath;
        for (var number = 2; Occupied(target); number++)
        {
            if (IsSameFile(sourcePath, target))
            {
                return new LinkResult(LinkOutcome.AlreadyExists, target);
            }

            target = NumberedName(destinationPath, number);
        }

        return new LinkResult(Place(sourcePath, target, method, hardLink), target);
    }

    /// <summary><c>Book.epub</c> → <c>Book (2).epub</c>.</summary>
    public static string NumberedName(string path, int number) =>
        Path.Combine(
            Path.GetDirectoryName(path) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(path)} ({number}){Path.GetExtension(path)}");

    /// <summary>Whether anything is at <paramref name="path"/>, a symlink whose target is gone included.</summary>
    private static bool Occupied(string path) => File.Exists(path) || new FileInfo(path).LinkTarget is not null;

    /// <summary>
    /// Whether <paramref name="existingPath"/> is already the source placed there: a symlink to it, or a file of the
    /// same size. Size is the test on purpose: it works the same for hardlinks and copies on every file system.
    /// </summary>
    private static bool IsSameFile(string sourcePath, string existingPath)
    {
        var existing = new FileInfo(existingPath);
        if (existing.LinkTarget is { } linkTarget)
        {
            var resolved = Path.IsPathRooted(linkTarget) ? linkTarget : Path.Combine(existing.DirectoryName ?? string.Empty, linkTarget);
            return string.Equals(Path.GetFullPath(resolved), Path.GetFullPath(sourcePath), StringComparison.Ordinal);
        }

        return existing.Exists && existing.Length == new FileInfo(sourcePath).Length;
    }

    private static LinkOutcome Place(string sourcePath, string destinationPath, LibraryLinkMethod method, Action<string, string> hardLink)
    {

        switch (method)
        {
            case LibraryLinkMethod.Hardlink:
                hardLink(sourcePath, destinationPath);
                return LinkOutcome.Hardlinked;
            case LibraryLinkMethod.HardlinkOrCopy:
                return TryOrFallback(
                    () => hardLink(sourcePath, destinationPath), LinkOutcome.Hardlinked,
                    () => CopyThroughTemporaryName(sourcePath, destinationPath), LinkOutcome.Copied);
            case LibraryLinkMethod.HardlinkOrSymlink:
                return TryOrFallback(
                    () => hardLink(sourcePath, destinationPath), LinkOutcome.Hardlinked,
                    () => File.CreateSymbolicLink(destinationPath, sourcePath), LinkOutcome.Symlinked);
            case LibraryLinkMethod.Copy:
                CopyThroughTemporaryName(sourcePath, destinationPath);
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

    /// <summary>
    /// Copies to a hidden temporary name next to the destination, then renames it into place, so a copy cut off by a
    /// crash or a full disk never sits at the real name looking complete. A temporary file left by an earlier attempt
    /// is overwritten.
    /// </summary>
    private static void CopyThroughTemporaryName(string sourcePath, string destinationPath)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(destinationPath) ?? string.Empty, $".{Path.GetFileName(destinationPath)}{PartialSuffix}");
        try
        {
            File.Copy(sourcePath, temporary, overwrite: true);
            File.Move(temporary, destinationPath);
        }
        catch
        {
            File.Delete(temporary);
            throw;
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
