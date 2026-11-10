namespace Fetchshelf.Core.Organizer;

/// <summary>"Is this path inside that folder?", answered by whole folder names.</summary>
public static class PathPrefix
{
    /// <summary>
    /// Whether <paramref name="path"/> is <paramref name="folder"/> itself or inside it, comparing whole path
    /// components: "/data/audio" holds "/data/audio/book" but not "/data/audiobooks2", which a plain string prefix
    /// would claim. A trailing <c>/</c> or <c>\</c> on <paramref name="folder"/> doesn't matter, both separators count,
    /// and the root "/" holds every absolute path. An empty folder holds nothing. Case-sensitive.
    /// </summary>
    public static bool IsSameOrUnder(string path, string folder)
    {
        if (folder.Length == 0)
        {
            return false;
        }

        var trimmed = folder.TrimEnd('/', '\\');
        return path == trimmed
            || (path.StartsWith(trimmed, StringComparison.Ordinal) && path.Length > trimmed.Length && path[trimmed.Length] is '/' or '\\');
    }
}
