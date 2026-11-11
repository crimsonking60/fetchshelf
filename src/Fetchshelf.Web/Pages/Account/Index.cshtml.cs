using Fetchshelf.Clients.Mam;
using Fetchshelf.Data.Configuration;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fetchshelf.Web.Pages.Account;

public class IndexModel(AccountSummaryBuilder summaryBuilder, MamClient mamClient, MamAccessGate accessGate, FetchshelfSettings settings)
    : PageModel
{
    /// <summary>How long Refresh waits for a job that is talking to MAM before saying MAM is busy.</summary>
    public static readonly TimeSpan RefreshWait = TimeSpan.FromSeconds(5);

    public bool Configured => settings.BackgroundJobsEnabled;

    public AccountSummary Summary { get; private set; } = new();

    /// <summary>Who held the gate when Refresh gave up waiting for it; null unless that happened.</summary>
    public string? BusyWith { get; private set; }

    /// <summary>Why Refresh couldn't read the account; null unless it failed.</summary>
    public string? RefreshError { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Summary = await summaryBuilder.BuildAsync(cancellationToken);

    /// <summary>
    /// Reads the account from MAM again. Like every MAM call it takes the access gate, but waits at most
    /// <see cref="RefreshWait"/> for a job holding it, and then says which job is busy rather than waiting it out.
    /// </summary>
    public async Task OnPostRefreshAsync(CancellationToken cancellationToken)
    {
        if (Configured)
        {
            using var gate = await accessGate.TryAcquireAsync("Account page", RefreshWait, cancellationToken);
            if (gate is null)
            {
                BusyWith = accessGate.Owner ?? "another job";
            }
            else
            {
                try
                {
                    await mamClient.RefreshAccountStateAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is MamForbiddenException or MamRateLimitedException or MamAccountBlockedException or HttpRequestException
                    || MamClient.IsNoAnswer(ex, cancellationToken))
                {
                    RefreshError = ex is HttpRequestException or TaskCanceledException ? MamClient.DescribeNoAnswer(ex) : ex.Message;
                }
            }
        }

        Summary = await summaryBuilder.BuildAsync(cancellationToken);
    }
}
