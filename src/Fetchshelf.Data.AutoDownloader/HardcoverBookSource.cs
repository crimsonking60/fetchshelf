using Fetchshelf.Clients.Hardcover;
using Fetchshelf.Core.Lists;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>
/// Reads the books a `[[hardcover_list]]` follows from Hardcover: a reading status or one of the token's user's own lists,
/// finding out once which user that is, or another user's list by their name.
/// </summary>
public sealed class HardcoverBookSource(HardcoverClient client)
{
    private long? _userId;

    public async Task<IReadOnlyList<HardcoverBook>> GetBooksAsync(HardcoverListEntry entry, CancellationToken cancellationToken = default)
    {
        // Another user's list is read by their name: the token's user isn't needed for it.
        if (HardcoverListReference.TryParse(entry.List, out var reference) && reference.Owner is { } owner)
        {
            return await client.GetListBooksAsync(owner, reference.Slug, cancellationToken);
        }

        _userId ??= (await client.GetMeAsync(cancellationToken)).Id;
        return reference is not null
            ? await client.GetListBooksAsync(_userId.Value, reference.Slug, cancellationToken)
            : await client.GetBooksWithStatusAsync(_userId.Value, ToReadingStatus(entry.Status!.Value), cancellationToken);
    }

    public static HardcoverReadingStatus ToReadingStatus(HardcoverListStatus status) => status switch
    {
        HardcoverListStatus.WantToRead => HardcoverReadingStatus.WantToRead,
        HardcoverListStatus.CurrentlyReading => HardcoverReadingStatus.CurrentlyReading,
        HardcoverListStatus.Read => HardcoverReadingStatus.Read,
        HardcoverListStatus.Paused => HardcoverReadingStatus.Paused,
        HardcoverListStatus.DidNotFinish => HardcoverReadingStatus.DidNotFinish,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}

/// <summary>One `[[hardcover_list]]`'s books, as list books: the Hardcover id is each book's id.</summary>
public sealed class HardcoverListSource(HardcoverBookSource books, HardcoverListEntry entry) : IListBookSource
{
    public async Task<IReadOnlyList<ListBook>> GetBooksAsync(CancellationToken cancellationToken = default) =>
        (await books.GetBooksAsync(entry, cancellationToken)).Select(ToListBook).ToList();

    public static ListBook ToListBook(HardcoverBook book)
    {
        var series = book.Series.FirstOrDefault();
        return new ListBook(book.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), book.Title, book.Authors)
        {
            SeriesName = series?.Name,
            SeriesNumber = series?.Position,
            CoverUrl = book.CoverUrl,
            BookUrl = book.Url,
            Identifiers = book.Identifiers,
        };
    }
}
