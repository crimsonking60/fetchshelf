namespace Fetchshelf.Core.Metadata;

/// <summary>Implements the "same book" equivalence test.</summary>
public static class SameBookComparer
{
    /// <summary>
    /// Two torrents are the same book only when all hold: media types are equal (except Ebook and
    /// Comic Book are interchangeable); languages are equal; editions agree (both none, or both
    /// present with the same number - and if that number is 0, the labels must also match; if
    /// exactly one has an edition, they are different); they share at least one author; and either
    /// both have no narrators or they share at least one narrator. Names are compared by <see cref="PersonNameKey"/>.
    /// </summary>
    public static bool AreSameBook(TorrentMetadata a, TorrentMetadata b)
    {
        return MediaTypesMatch(a.MediaType, b.MediaType)
            && a.Language == b.Language
            && EditionsAgree(a.Edition, b.Edition)
            && ShareAtLeastOne(a.Authors, b.Authors)
            && NarratorsAgree(a.Narrators, b.Narrators);
    }

    private static bool MediaTypesMatch(MediaType a, MediaType b)
    {
        if (a == b)
        {
            return true;
        }

        var interchangeable = new HashSet<MediaType> { MediaType.Ebook, MediaType.ComicBook };
        return interchangeable.Contains(a) && interchangeable.Contains(b);
    }

    private static bool EditionsAgree(Edition? a, Edition? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        if (a.Number != b.Number)
        {
            return false;
        }

        return a.Number != 0 || string.Equals(a.Label, b.Label, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the two lists name one person in common, compared by <see cref="PersonNameKey"/>, so spellings that
    /// differ only in case, spacing or punctuation agree. A name with no letters or digits matches nobody.
    /// </summary>
    private static bool ShareAtLeastOne(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var keys = a.Select(PersonNameKey.Of).Where(key => key.Length > 0).ToHashSet(StringComparer.Ordinal);
        return b.Select(PersonNameKey.Of).Any(keys.Contains);
    }

    private static bool NarratorsAgree(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
        {
            return true;
        }

        return ShareAtLeastOne(a, b);
    }
}
