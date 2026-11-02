using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Web.Pages.Torrents;

/// <summary>Readable labels for the stored <see cref="LibraryMismatchState"/> names. Values it does not know are shown unchanged.</summary>
public static class LibraryMismatchLabel
{
    public static string? For(string? state) => state switch
    {
        nameof(LibraryMismatchState.NoLibrary) => "No library",
        nameof(LibraryMismatchState.NewLibraryDir) => "Different library",
        nameof(LibraryMismatchState.NewPath) => "Different folder",
        _ => state,
    };
}
