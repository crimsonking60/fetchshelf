using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web.Pages.Jobs;

/// <summary>The three kinds of job the Jobs page separates.</summary>
public enum JobGroup
{
    /// <summary>The jobs every installation has: Download, Link and Clean.</summary>
    Standard,

    /// <summary>The built-in jobs that exist when configured (Follow, Bonus, Audiobookshelf, ...) and the list jobs.</summary>
    Configured,

    /// <summary>One job for each <c>[[autograb]]</c> block, of which there may be many.</summary>
    Autograb,
}

/// <summary>One section of the Jobs page: its heading text and the jobs in it.</summary>
public sealed record JobGroupViewModel(JobGroup Group, string Title, string Caption, IReadOnlyList<JobStatusViewModel> Jobs)
{
    /// <summary>Jobs in the group whose runs keep failing, so a failure shows on a heading that is scrolled past.</summary>
    public int FailingCount => Jobs.Count(j => j.Failing is not null);
}

/// <summary>The Jobs page's list: the groups that have jobs, and the optional kinds with nothing configured.</summary>
public sealed record JobListViewModel(IReadOnlyList<JobGroupViewModel> Groups, IReadOnlyList<string> NotConfigured)
{
    public bool IsEmpty => Groups.Count == 0;
}

/// <summary>
/// Sorts the registered jobs into the page's groups. A job's group follows from its name and the configuration: the
/// standard jobs by name, autograb jobs by the names of the <c>[[autograb]]</c> blocks, and everything else is configured.
/// </summary>
public static class JobGrouping
{
    /// <summary>The standard jobs, in the order the page shows them.</summary>
    public static readonly IReadOnlyList<string> StandardNames = [JobNames.Download, JobNames.Link, JobNames.Clean];

    public static JobListViewModel Build(IEnumerable<JobStatusViewModel> jobs, FetchshelfSettings settings)
    {
        var byName = jobs.ToDictionary(j => j.Name, StringComparer.Ordinal);

        var standard = StandardNames.Where(byName.ContainsKey).Select(n => byName[n]).ToList();
        var autograb = settings.Autograb
            .Select((entry, i) => AutoDownloaderJobNames.Autograb(entry, i))
            .Where(byName.ContainsKey).Distinct(StringComparer.Ordinal).Select(n => byName[n]).ToList();

        // The list jobs come last, in the order of the configuration; every other job is by name.
        var listNames = settings.HardcoverList.Select((entry, i) => AutoDownloaderJobNames.HardcoverList(entry, i))
            .Concat(settings.FileList.Select((entry, i) => AutoDownloaderJobNames.FileList(entry, i)))
            .Concat(settings.BookmarksList.Select(AutoDownloaderJobNames.BookmarksList))
            .Where(byName.ContainsKey).Distinct(StringComparer.Ordinal).ToList();

        var placed = standard.Concat(autograb).Select(j => j.Name).Concat(listNames).ToHashSet(StringComparer.Ordinal);
        var configured = byName.Values.Where(j => !placed.Contains(j.Name)).OrderBy(j => j.Name, StringComparer.Ordinal)
            .Concat(listNames.Select(n => byName[n])).ToList();

        List<JobGroupViewModel> groups = [];
        AddIfAny(groups, JobGroup.Standard, "Standard jobs", "Present in every installation: they send queued torrents to qBittorrent and put finished ones in the library.", standard);
        AddIfAny(groups, JobGroup.Configured, "Configured jobs", "Present when their section or list is in the configuration.", configured);
        AddIfAny(groups, JobGroup.Autograb, "Autograb jobs", "One for each [[autograb]] block, in the order of the configuration.", autograb);
        return new JobListViewModel(groups, UnconfiguredJobs.For(settings));
    }

    private static void AddIfAny(List<JobGroupViewModel> groups, JobGroup group, string title, string caption, List<JobStatusViewModel> jobs)
    {
        if (jobs.Count > 0)
        {
            groups.Add(new JobGroupViewModel(group, title, caption, jobs));
        }
    }
}
