using Fetchshelf.Clients.Mam;

namespace Fetchshelf.Web.Jobs;

/// <summary>
/// Reads the account from MAM once, when Fetchshelf has started, so the Jobs and Account pages have its numbers from the
/// start rather than saying none have been read until a job happens to read them. Only with a `mam_id` (registered
/// with the background jobs), through the MAM gate like any other caller, and not at all when a job has read the
/// account already; a read that fails is logged and nothing else happens.
/// </summary>
public sealed class AccountStartupRead(MamClient mamClient, MamAccessGate gate, ILogger<AccountStartupRead> logger) : BackgroundService
{
    /// <summary>The name it holds the MAM gate under, as the Account page shows who holds it.</summary>
    public const string GateOwner = "Startup account read";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using (await gate.AcquireAsync(GateOwner, stoppingToken))
            {
                // A job that ran first (they start with Fetchshelf too) has read it already.
                if (mamClient.LastAccountState is not null)
                {
                    return;
                }

                await mamClient.GetAccountStateAsync(stoppingToken);
                logger.LogInformation("Read the account from MAM at startup");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Fetchshelf is stopping.
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not read the account from MAM at startup: {Reason}", ex.Message);
        }
    }
}
