namespace Fetchshelf.Core.Metadata;

/// <summary>
/// The key two author or narrator names are compared by: folded like titles (<see cref="TextFolding"/>), lowercased,
/// and with everything but letters and digits removed. "J. R. R. Tolkien", "J.R.R. Tolkien" and "j.r.r. tolkien" all
/// key as "jrrtolkien", and "Le Guin" and "LeGuin" as "leguin". Name order, initials against full names, and pen names
/// are not handled; <see cref="WithoutMiddleInitials"/> leaves out initials between the first and last name.
/// </summary>
public static class PersonNameKey
{
    public static string Of(string name) =>
        string.Concat(TextFolding.Fold(name).ToLowerInvariant().EnumerateRunes().Where(System.Text.Rune.IsLetterOrDigit));

    /// <summary>
    /// The key without the one-letter words between the first and the last: "Steve J McHugh" and "Steve McHugh" both key
    /// as "stevemchugh". A first initial stays, so "J. Smith" and "K. Smith" still differ.
    /// </summary>
    public static string WithoutMiddleInitials(string name)
    {
        var words = Words(name);
        return string.Concat(words.Where((word, i) => i == 0 || i == words.Count - 1 || word.Length > 1));
    }

    private static List<string> Words(string name)
    {
        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        foreach (var rune in TextFolding.Fold(name).ToLowerInvariant().EnumerateRunes())
        {
            if (System.Text.Rune.IsLetterOrDigit(rune))
            {
                word.Append(rune.ToString());
            }
            else if (word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }

        return words;
    }
}
