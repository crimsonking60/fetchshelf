using Fetchshelf.Data.Organizer;
using Microsoft.Extensions.Caching.Memory;

namespace Fetchshelf.Web;

/// <summary>
/// The cover a search result's details show with `[search] covers = true`: from the cover sources, by the result's title,
/// authors, ISBN and ASIN, never from MAM. A cover, or the lack of one, is kept in memory for an hour, so opening the same
/// result again asks nothing; a refusal isn't kept, so the next look asks again.
/// </summary>
public sealed class SearchCoverLookup(CoverSourceList sources, IMemoryCache cache, ILogger<SearchCoverLookup> logger)
{
    private static readonly TimeSpan Kept = TimeSpan.FromHours(1);

    public bool CanFind(CoverQuery book) => sources.Sources.Any(source => source.CanFind(book));

    public async Task<CoverImage?> GetAsync(CoverQuery book, CancellationToken cancellationToken)
    {
        var key = ("search-cover", book.Title, string.Join('|', book.Authors), book.Isbn, book.AudiobookAsin);
        if (cache.TryGetValue(key, out CoverImage? kept))
        {
            return kept;
        }

        CoverImage? cover;
        try
        {
            // A chain of its own: a source refusing this request shouldn't be left out for the next.
            cover = await new ChainedCoverSource(sources.Sources).FetchAsync(book, cancellationToken);
        }
        catch (Exception ex) when (ex is CoverSourceRefusedException or CoverSourceDeferredException
            || (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException))
        {
            logger.LogInformation("No cover for search result {MamId}: {Error}", book.MamId, ex.Message);
            return null;
        }

        cache.Set(key, cover, Kept);
        return cover;
    }
}
