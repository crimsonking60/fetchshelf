using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data.Organizer;

/// <summary>A cover image as fetched: its bytes, its content type (such as <c>image/jpeg</c>), and the source's name.</summary>
public sealed record CoverImage(byte[] Bytes, string ContentType, string? Source = null);

/// <summary>
/// Where the covers <see cref="CoverWriter"/> fetches for book folders come from (`covers`), when the books' own files
/// have none: Open Library and Audnexus, through <see cref="ChainedCoverSource"/>. Never MAM: its image server refuses
/// Fetchshelf, and it was never among the parts of MAM its API documentation allows automating.
/// </summary>
public interface ICoverSource
{
    /// <summary>
    /// Whether the source can look for the book at all (it has what the source finds books by, such as an ISBN). A book
    /// no source can look for is settled without a request.
    /// </summary>
    bool CanFind(TorrentEntity torrent) => true;

    /// <summary>
    /// The book's cover, or null when the source has none. Throws <see cref="CoverSourceRefusedException"/> to end fetching
    /// for the run, or <see cref="CoverSourceDeferredException"/> to leave this book for a later run.
    /// </summary>
    Task<CoverImage?> FetchAsync(TorrentEntity torrent, CancellationToken cancellationToken = default);
}

/// <summary>The source refused, or asked to slow down: the run stops fetching, and the rest wait for a later one.</summary>
public sealed class CoverSourceRefusedException(string message) : Exception(message);

/// <summary>
/// The sources that could find this book are refused for this run, though others still work: the book waits for a later
/// run rather than being settled without a cover.
/// </summary>
public sealed class CoverSourceDeferredException(string message) : Exception(message);
