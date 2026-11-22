using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data.Organizer;

/// <summary>A cover image as fetched: its bytes and its content type, such as <c>image/jpeg</c>.</summary>
public sealed record CoverImage(byte[] Bytes, string ContentType);

/// <summary>
/// Where the covers <see cref="CoverWriter"/> writes into book folders come from (`covers_from_mam`). MAM's cover server
/// is not one: since 2026-11-22 it refuses Fetchshelf's requests, and it was never among the parts of MAM its API
/// documentation allows automating. Until a source is registered, no covers are fetched.
/// </summary>
public interface ICoverSource
{
    /// <summary>The book's cover, or null when the source has none. Throws <see cref="CoverSourceRefusedException"/> to end the run.</summary>
    Task<CoverImage?> FetchAsync(TorrentEntity torrent, CancellationToken cancellationToken = default);
}

/// <summary>The source refused, or asked to slow down: the run stops, and the rest wait for a later one.</summary>
public sealed class CoverSourceRefusedException(string message) : Exception(message);
