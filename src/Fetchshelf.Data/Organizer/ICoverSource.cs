using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data.Organizer;

/// <summary>A cover image as fetched: its bytes, its content type (such as <c>image/jpeg</c>), and the source's name.</summary>
public sealed record CoverImage(byte[] Bytes, string ContentType, string? Source = null);

/// <summary>
/// What cover sources find a book by: its title and authors, its ISBN and, for an audiobook, its ASIN. Nothing else about
/// the book or the user is sent to them.
/// </summary>
/// <param name="MamId">The torrent's MAM id, for logging only; never sent.</param>
/// <param name="Isbn">An ISBN-13 (or an ISBN-10 that has none), checked; never an ASIN, which MAM's ISBN field may hold.</param>
/// <param name="AudiobookAsin">An audiobook's ASIN, checked; null for anything but an audiobook.</param>
public sealed record CoverQuery(long MamId, string Title, IReadOnlyList<string> Authors, string? Isbn, string? AudiobookAsin)
{
    /// <summary>The query for a torrent Fetchshelf has.</summary>
    public static CoverQuery For(TorrentEntity torrent) => For(torrent.MamId, TorrentMetadataSerializer.Deserialize(torrent.MetadataJson));

    /// <summary>The query for a book's metadata, such as a search result's.</summary>
    public static CoverQuery For(long mamId, TorrentMetadata metadata) => Of(
        mamId, metadata.Title, metadata.Authors, metadata.Isbn, metadata.Asin,
        audiobook: metadata.MediaType is MediaType.Audiobook or MediaType.PeriodicalAudiobook);

    /// <summary>
    /// A query from values as given, such as from an address: only a valid ISBN and ASIN are kept, the ASIN only for an
    /// audiobook, and it may also come from the ISBN field.
    /// </summary>
    public static CoverQuery Of(long mamId, string title, IEnumerable<string> authors, string? isbn, string? asin, bool audiobook)
    {
        var isbnKeys = BookIdentifiers.Keys(isbn).ToList();
        var validIsbn = isbnKeys.FirstOrDefault(key => key.Length == 13) ?? isbnKeys.FirstOrDefault(key => key.Length == 10 && !key.StartsWith('B'));
        var validAsin = audiobook
            ? BookIdentifiers.Keys(asin).Concat(isbnKeys).FirstOrDefault(key => key.Length == 10 && key.StartsWith('B'))
            : null;
        return new CoverQuery(mamId, title.Trim(), authors.Select(a => a.Trim()).Where(a => a.Length > 0).ToList(), validIsbn, validAsin);
    }
}

/// <summary>
/// Where the covers <see cref="CoverWriter"/> fetches for book folders come from (`covers`), when the books' own files
/// have none, and those the Search page shows (`[search] covers`): Hardcover, Open Library and Audnexus, through
/// <see cref="ChainedCoverSource"/>. Never MAM: its image server refuses Fetchshelf, and it was never among the parts of
/// MAM its API documentation allows automating.
/// </summary>
public interface ICoverSource
{
    /// <summary>
    /// Whether the source can look for the book at all (it has what the source finds books by, such as an ISBN). A book
    /// no source can look for is settled without a request.
    /// </summary>
    bool CanFind(CoverQuery book) => true;

    /// <summary>
    /// The book's cover, or null when the source has none. Throws <see cref="CoverSourceRefusedException"/> to end fetching
    /// for the run, or <see cref="CoverSourceDeferredException"/> to leave this book for a later run.
    /// </summary>
    Task<CoverImage?> FetchAsync(CoverQuery book, CancellationToken cancellationToken = default);
}

/// <summary>The source refused, or asked to slow down: the run stops fetching, and the rest wait for a later one.</summary>
public sealed class CoverSourceRefusedException(string message) : Exception(message);

/// <summary>
/// The sources that could find this book are refused for this run, though others still work: the book waits for a later
/// run rather than being settled without a cover.
/// </summary>
public sealed class CoverSourceDeferredException(string message) : Exception(message);
