namespace Fetchshelf.Data.AutoDownloader;

/// <summary>
/// One-shot maintenance operation, run with <c>--update-search-title</c>: recomputes the normalized title key of every
/// stored torrent and selection (<see cref="TitleKeys"/>). Startup does the same once by itself when the title rule
/// changes; this is for running it by hand.
/// </summary>
public static class TitleMaintenanceRunner
{
    public static Task<(int Torrents, int Selections)> RunAsync(FetchshelfDbContext dbContext, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TitleKeys.RecomputeAll(dbContext));
    }
}
