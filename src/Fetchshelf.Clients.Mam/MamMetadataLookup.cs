using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Clients.Mam;

/// <summary><see cref="IMetadataLookup"/> backed by a MAM search by info-hash.</summary>
public sealed class MamMetadataLookup(MamClient client, MamAccessGate accessGate) : IMetadataLookup
{
    /// <remarks>
    /// The gate is taken per lookup rather than per Link run, so a long Link run lets an autograb or snatchlist job in
    /// between its lookups instead of holding them off until it ends.
    /// </remarks>
    public async Task<TorrentMetadata?> TryLookupByInfoHashAsync(string infoHash, CancellationToken cancellationToken = default)
    {
        using var _ = await accessGate.AcquireAsync("Looking a torrent up (Link, or Refresh on a torrent's page)", cancellationToken);
        return await client.LookupAsync(infoHash, cancellationToken);
    }
}
