using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Organizer;

/// <summary>
/// Looks up a torrent's metadata by info-hash. No implementation is
/// registered until the MaM client lands (build-order step 4) — see <see cref="NullMetadataLookup"/>.
/// </summary>
public interface IMetadataLookup
{
    Task<TorrentMetadata?> TryLookupByInfoHashAsync(string infoHash, CancellationToken cancellationToken = default);
}

/// <summary>Always reports "not found." The only registered <see cref="IMetadataLookup"/> until the MaM client lands.</summary>
public sealed class NullMetadataLookup : IMetadataLookup
{
    public Task<TorrentMetadata?> TryLookupByInfoHashAsync(string infoHash, CancellationToken cancellationToken = default) =>
        Task.FromResult<TorrentMetadata?>(null);
}
