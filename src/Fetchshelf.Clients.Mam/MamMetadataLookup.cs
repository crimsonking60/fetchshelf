using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Clients.Mam;

/// <summary>Real <see cref="IMetadataLookup"/> backed by MAM, replacing <see cref="NullMetadataLookup"/>.</summary>
public sealed class MamMetadataLookup(MamClient client) : IMetadataLookup
{
    public Task<TorrentMetadata?> TryLookupByInfoHashAsync(string infoHash, CancellationToken cancellationToken = default) =>
        client.LookupAsync(infoHash, cancellationToken);
}
