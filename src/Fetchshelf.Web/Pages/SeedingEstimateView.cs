using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages;

/// <summary>
/// The Seeding job's estimate next to MAM's own counts, as the Account page and the Unsatisfied burndown page show it
/// (<c>Shared/_SeedingEstimate</c>).
/// </summary>
/// <param name="MamSeeding">MAM's count of unsatisfied snatches seeding; null when the account hasn't been read.</param>
/// <param name="MamNotSeeding">MAM's count of unsatisfied snatches not seeding; null when the account hasn't been read.</param>
public sealed record SeedingEstimateView(int Seeding, int NotSeeding, int PastThePeriod, long? MamSeeding, long? MamNotSeeding)
{
    /// <summary>MAM's counts are known and differ from the estimate, so the page says both.</summary>
    public bool DiffersFromMam => MamSeeding is { } seeding && MamNotSeeding is { } notSeeding && (seeding != Seeding || notSeeding != NotSeeding);

    /// <summary>The estimate from the stored seeding states, next to MAM's counts from <paramref name="account"/> when it was read.</summary>
    public static async Task<SeedingEstimateView> LoadAsync(FetchshelfDbContext dbContext, MamAccountState? account, CancellationToken cancellationToken)
    {
        var counts = await dbContext.Torrents.AsNoTracking()
            .Where(t => t.SeedingState != null)
            .GroupBy(t => t.SeedingState!)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.State, g => g.Count, cancellationToken);
        int Counted(SeedingState state) => counts.TryGetValue(state.ToString(), out var count) ? count : 0;
        long? MamCount(string key) => account is null ? null : account.SnatchKinds.FirstOrDefault(k => k.Key == key)?.Count ?? 0;
        return new SeedingEstimateView(
            Counted(SeedingState.SeedingUnsatisfied), Counted(SeedingState.NotSeedingUnsatisfied), Counted(SeedingState.PastThePeriod),
            MamCount("seedUnsat"), MamCount("inactUnsat"));
    }
}
