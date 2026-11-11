namespace Fetchshelf.Core.Jobs;

/// <summary>A line on the Errors page: one failing job, or several that MAM's refusal of the session or account stopped.</summary>
/// <param name="Jobs">The failing jobs, in the order given.</param>
/// <param name="Kind">What kind of error they failed with.</param>
/// <param name="Since">When the earliest of them started failing.</param>
public sealed record FailingJobGroup(IReadOnlyList<(string Name, JobFailure Failure)> Jobs, JobFailureKind Kind, DateTimeOffset Since)
{
    /// <summary>Whether this line stands for several jobs that one MAM refusal stopped.</summary>
    public bool IsMamRefusal => Jobs.Count > 1;
}

public static class FailingJobGroups
{
    /// <summary>
    /// A 403 or a 406 from MAM stops every MAM job, each with a failure of its own; several jobs failing with the same
    /// one are grouped into one line, since one fix (a new `mam_id`, or MAM unblocking the account) clears them all.
    /// Every other failing job keeps a line of its own. The groups come first, then the other jobs in the order given.
    /// </summary>
    public static IReadOnlyList<FailingJobGroup> Group(IEnumerable<(string Name, JobFailure Failure)> failing)
    {
        var jobs = failing.ToList();
        var groups = new List<FailingJobGroup>();
        var grouped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in new[] { JobFailureKind.MamRefused, JobFailureKind.MamBlocked })
        {
            var refused = jobs.Where(j => j.Failure.LatestKind == kind).ToList();
            if (refused.Count > 1)
            {
                groups.Add(new FailingJobGroup(refused, kind, refused.Min(j => j.Failure.Since)));
                grouped.UnionWith(refused.Select(j => j.Name));
            }
        }

        groups.AddRange(jobs
            .Where(j => !grouped.Contains(j.Name))
            .Select(j => new FailingJobGroup([j], j.Failure.LatestKind, j.Failure.Since)));
        return groups;
    }
}
