using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Clients.Mam;

/// <summary>Real <see cref="IMetadataLookup"/> backed by MaM, replacing <see cref="NullMetadataLookup"/> (Fetchshelf-spec.md §8.2 step 4).</summary>
public sealed class MamMetadataLookup(MamClient client) : IMetadataLookup
{
    public Task<TorrentMetadata?> TryLookupByInfoHashAsync(string infoHash, CancellationToken cancellationToken = default) =>
        client.LookupAsync(infoHash, cancellationToken);
}
