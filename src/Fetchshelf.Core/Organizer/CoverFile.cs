namespace Fetchshelf.Core.Organizer;

/// <summary>Whether a book's folder needs a cover image, and what the one written there is called.</summary>
public static class CoverFile
{
    /// <summary>The image types a cover is written as, and the name each is written under.</summary>
    private static readonly Dictionary<string, string> NamesByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "cover.jpg",
        ["image/jpg"] = "cover.jpg",
        ["image/png"] = "cover.png",
        ["image/webp"] = "cover.webp",
    };

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>Every name a cover can be written under, which the folder's other extras are removed with.</summary>
    public static IReadOnlyList<string> Names { get; } = NamesByContentType.Values.Distinct().ToList();

    /// <summary>
    /// Whether a folder holding <paramref name="fileNames"/> needs a cover: none of them is an image, whatever its name. A
    /// cover the user, Audiobookshelf or an earlier run put there is never replaced.
    /// </summary>
    public static bool IsNeeded(IEnumerable<string> fileNames) =>
        !fileNames.Any(name => ImageExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase));

    /// <summary>The name a cover of <paramref name="contentType"/> is written under; null for a type that isn't written.</summary>
    public static string? NameFor(string contentType) => NamesByContentType.GetValueOrDefault(contentType);
}
