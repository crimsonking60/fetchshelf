using Fetchshelf.Core.Jobs;

namespace Fetchshelf.Web.Pages.Errors;

/// <summary>How a failing job, or a group of them, reads on the Errors page.</summary>
public static class FailingJobText
{
    /// <summary>What stopped a group of jobs, such as "MAM refused mam_id (HTTP 403)".</summary>
    public static string Cause(JobFailureKind kind) => kind switch
    {
        JobFailureKind.MamRefused => "MAM refused mam_id (HTTP 403)",
        JobFailureKind.MamBlocked => "MAM blocked the account (HTTP 406)",
        JobFailureKind.MamRateLimited => "MAM asked for fewer requests (HTTP 429)",
        _ => "Failing",
    };

    /// <summary>What to do about a MAM refusal; null for other failures, whose error says it.</summary>
    public static string? Advice(JobFailureKind kind) => kind switch
    {
        JobFailureKind.MamRefused =>
            "The mam_id may have been invalidated or be locked to another IP address. Fix mam_id in config.toml and restart "
            + "Fetchshelf; the Account page says until when MAM calls are paused.",
        JobFailureKind.MamBlocked => "MAM lifts the block itself; the jobs succeed again on their next runs.",
        JobFailureKind.MamRateLimited => "The calls resume by themselves after a minute.",
        _ => null,
    };

    /// <summary>
    /// The jobs a group names, and how many it leaves out: all of them up to three, else the first two and "2 more".
    /// </summary>
    public static (IReadOnlyList<string> Shown, int More) Names(FailingJobGroup group)
    {
        var names = group.Jobs.Select(j => j.Name).ToList();
        return names.Count <= 3 ? (names, 0) : (names.Take(2).ToList(), names.Count - 2);
    }

    /// <summary>"(6 runs)" or "(1 run)".</summary>
    public static string Runs(int failedRuns) => $"({failedRuns} {(failedRuns == 1 ? "run" : "runs")})";
}
