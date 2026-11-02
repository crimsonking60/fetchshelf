using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Organizer;

/// <summary>Looks up a torrent's metadata by info-hash; null when the source does not know the torrent.</summary>
public interface IMetadataLookup
{
    Task<TorrentMetadata?> TryLookupByInfoHashAsync(string infoHash, CancellationToken cancellationToken = default);
}
