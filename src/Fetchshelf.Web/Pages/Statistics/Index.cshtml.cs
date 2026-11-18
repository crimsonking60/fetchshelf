using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Statistics;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;

namespace Fetchshelf.Web.Pages.Statistics;

public class IndexModel(LibraryStatistics statistics, FetchshelfSettings settings, IMemoryCache cache, TimeProvider timeProvider) : PageModel
{
    /// <summary>How long the numbers are kept, so reloading the page doesn't read every torrent and event again.</summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    private const string CacheKey = "statistics-page";

    public StatisticsView View { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        View = (await cache.GetOrCreateAsync(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return StatisticsView.BuildAsync(statistics, settings, timeProvider.GetUtcNow(), cancellationToken);
        }))!;
    }

    /// <summary>A bar's width, as a percentage of the largest value in its table.</summary>
    public static string BarWidth(long value, long max) =>
        max <= 0 ? "0%" : $"{Math.Round(100.0 * value / max, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}%";
}
