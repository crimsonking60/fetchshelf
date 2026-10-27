using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Web.Tests;

internal sealed class FakeMetadataLookup(TorrentMetadata? result) : IMetadataLookup
{
    public TorrentMetadata? Result { get; set; } = result;

    public Task<TorrentMetadata?> TryLookupByInfoHashAsync(string infoHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result);
}
