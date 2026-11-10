namespace Fetchshelf.Core.Metadata;

/// <summary>
/// The key two author or narrator names are compared by: folded like titles (<see cref="TextFolding"/>), lowercased,
/// and with everything but letters and digits removed. "J. R. R. Tolkien", "J.R.R. Tolkien" and "j.r.r. tolkien" all
/// key as "jrrtolkien", and "Le Guin" and "LeGuin" as "leguin". Name order, initials against full names, and pen names
/// are not handled.
/// </summary>
public static class PersonNameKey
{
    public static string Of(string name) =>
        string.Concat(TextFolding.Fold(name).ToLowerInvariant().EnumerateRunes().Where(System.Text.Rune.IsLetterOrDigit));
}
