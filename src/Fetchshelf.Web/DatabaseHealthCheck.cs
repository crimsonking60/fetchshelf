using Fetchshelf.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Fetchshelf.Web;

/// <summary>
/// Healthy when the database can be opened. Deliberately the only check: MAM, qBittorrent and Audiobookshelf being
/// down is reported by the jobs, and must not mark Fetchshelf itself as unhealthy.
/// </summary>
public sealed class DatabaseHealthCheck(IDbContextFactory<FetchshelfDbContext> dbContextFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The database cannot be opened.");
    }
}
