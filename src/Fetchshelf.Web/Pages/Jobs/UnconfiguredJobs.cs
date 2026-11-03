using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web.Pages.Jobs;

/// <summary>
/// The kinds of optional job with nothing configured, one name per kind, alphabetical. Download, Link and Clean are
/// not optional, so they never appear here.
/// </summary>
public static class UnconfiguredJobs
{
    public static IReadOnlyList<string> For(FetchshelfSettings settings)
    {
        var kinds = new (string Name, bool Configured)[]
        {
            ("Audiobookshelf", settings.Audiobookshelf is not null),
            ("Autograb", settings.Autograb.Count > 0),
            ("Snatchlist", settings.Snatchlist.Count > 0),
        };
        return kinds.Where(kind => !kind.Configured).Select(kind => kind.Name).Order(StringComparer.Ordinal).ToList();
    }
}
