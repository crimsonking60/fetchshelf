namespace Fetchshelf.Core.Organizer;

/// <summary>
/// The id of a book record of a split collection: the collection's info hash, a dash and the book's place in it, such
/// as <c>939c…5848-4</c>. A dash rather than a slash, so the id is one segment in the torrent page's and OPDS's
/// addresses. qBittorrent and MAM know only the collection, so everything that asks them about a record asks with
/// <see cref="HashOf"/>.
/// </summary>
public static class CollectionBookId
{
    public static string For(string collectionHash, int position) => $"{collectionHash}-{position}";

    /// <summary>Whether <paramref name="id"/> is a book record's id: an info hash, a dash and a number.</summary>
    public static bool IsBook(string id) => Parse(id) is not null;

    /// <summary>The info hash qBittorrent and MAM know a record by: its collection's for a book, else its own id.</summary>
    public static string HashOf(string id) => Parse(id)?.Hash ?? id;

    private static (string Hash, int Position)? Parse(string id)
    {
        var dash = id.LastIndexOf('-');
        return dash is 40 or 64
            && id[..dash].All(char.IsAsciiHexDigit)
            && int.TryParse(id.AsSpan(dash + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var position)
            && position > 0
                ? (id[..dash], position)
                : null;
    }
}
