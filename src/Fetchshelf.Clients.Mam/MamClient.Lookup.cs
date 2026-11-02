using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Clients.Mam;

public sealed partial class MamClient
{
    /// <summary>Looks up one torrent by MAM id, returning cleaned metadata, or null if MAM doesn't recognize it.</summary>
    public async Task<TorrentMetadata?> LookupAsync(long mamId, CancellationToken cancellationToken = default)
    {
        var result = await SearchAsync(new MamSearchQuery { Id = mamId }, cancellationToken);
        var record = result.Data.FirstOrDefault();
        return record is null ? null : MamTorrentMapper.Map(record);
    }

    /// <summary>Looks up one torrent by info-hash, returning cleaned metadata, or null if MAM doesn't recognize it.</summary>
    public async Task<TorrentMetadata?> LookupAsync(string infoHash, CancellationToken cancellationToken = default)
    {
        var result = await SearchAsync(new MamSearchQuery { Hash = infoHash }, cancellationToken);
        var record = result.Data.FirstOrDefault();
        return record is null ? null : MamTorrentMapper.Map(record);
    }
}
