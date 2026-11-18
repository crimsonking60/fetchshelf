namespace Fetchshelf.Clients.Mam;

/// <summary>Fetches a torrent's cover image from MAM.</summary>
public interface IMamCoverSource
{
    /// <summary>The cover of MAM torrent <paramref name="mamId"/>; null when MAM has none, or doesn't serve it as an image.</summary>
    Task<MamImage?> FetchAsync(long mamId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Looks the torrent up by its id with <see cref="MamSearchQuery.ReturnThumbnail"/>, then fetches the cover from the
/// address the Search page's detail view uses (<see cref="MamCoverAddress"/>). Both requests hold
/// <see cref="MamAccessGate"/>, taken per cover, as <see cref="MamMetadataLookup"/> does per lookup.
/// </summary>
public sealed class MamCoverSource(MamClient client, MamAccessGate accessGate, MamClientOptions options) : IMamCoverSource
{
    public async Task<MamImage?> FetchAsync(long mamId, CancellationToken cancellationToken = default)
    {
        using var _ = await accessGate.AcquireAsync("Fetching a cover (Link)", cancellationToken);
        var result = await client.SearchAsync(new MamSearchQuery { Id = mamId, ReturnThumbnail = true }, cancellationToken);
        if (result.Data.FirstOrDefault() is not { } record || MamCoverAddress.For(record, options) is not { } url)
        {
            return null;
        }

        return await client.DownloadImageAsync(url, cancellationToken);
    }
}
