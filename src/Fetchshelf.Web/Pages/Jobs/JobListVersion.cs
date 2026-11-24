using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Fetchshelf.Web.Pages.Jobs;

/// <summary>
/// A short fingerprint of everything the job list shows except the passing of time, so the page's poll can be answered
/// with "nothing changed" and leave the list on screen alone. "Running for 2m 10s" and "in 12 min" are counted in the
/// browser (jobs.js) from the instants this covers, so a running job or a coming run changes nothing here.
/// </summary>
public static class JobListVersion
{
    public static string Of(JobListViewModel list, bool jobExplanations, bool dryJobButtons, int libraryCheckFindings)
    {
        var text = new StringBuilder();
        Append(text, jobExplanations ? "explain" : "-", dryJobButtons ? "dry" : "-", libraryCheckFindings > 0 ? "findings" : "-");
        foreach (var group in list.Groups)
        {
            Append(text, "group", group.Group.ToString(), group.Title, group.Caption);
            foreach (var job in group.Jobs)
            {
                Append(
                    text,
                    job.Name,
                    job.Description,
                    Instant(job.LastStartedAt),
                    job.LastResultText,
                    job.IsRunning ? "running" : "idle",
                    Instant(job.NextScheduledRun),
                    job.RunNumber.ToString(CultureInfo.InvariantCulture),
                    Instant(job.RunningSince),
                    job.LastDuration is { } duration ? JobRunLabels.Duration(duration) : null,
                    Instant(job.Failing?.Since),
                    job.Failing?.FailedRuns.ToString(CultureInfo.InvariantCulture),
                    job.HasDryRun ? "dry" : "-",
                    job.CountsText);
            }
        }

        Append(text, ["not configured", .. list.NotConfigured]);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    private static string? Instant(DateTimeOffset? instant) => instant?.UtcTicks.ToString(CultureInfo.InvariantCulture);

    // Each value ends in a character no value holds, so no two different lists can run together into the same text.
    private static void Append(StringBuilder text, params string?[] values)
    {
        foreach (var value in values)
        {
            text.Append(value).Append('\u001f');
        }

        text.Append('\u001e');
    }
}
