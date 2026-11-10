using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Organizer;

/// <summary>Which configured library a book folder belongs to, by where the folder is.</summary>
public static class LibraryFolders
{
    /// <summary>The index of the library whose folder holds <paramref name="path"/>, the deepest one if folders nest; null when none does.</summary>
    public static int? IndexContaining(IReadOnlyList<Library> libraries, string path) =>
        libraries
            .Select((library, index) => (Library: library, Index: index))
            .Where(l => PathPrefix.IsSameOrUnder(path, l.Library.LibraryDir))
            .OrderByDescending(l => l.Library.LibraryDir.Length)
            .Select(l => (int?)l.Index)
            .FirstOrDefault();
}
