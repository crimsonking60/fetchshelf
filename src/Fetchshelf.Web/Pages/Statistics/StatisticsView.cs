using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Statistics;

namespace Fetchshelf.Web.Pages.Statistics;

/// <summary>A grabber's grabs, with what kind of block it is.</summary>
public sealed record GrabberRow(string Name, string Kind, int Count);

/// <summary>Everything the Statistics page shows.</summary>
public sealed record StatisticsView(
    IReadOnlyList<CountRow> ByMediaType,
    IReadOnlyList<CountRow> ByLanguage,
    IReadOnlyList<CountRow> ByLibrary,
    long LibraryBytes,
    IReadOnlyList<MonthRow> AddedPerMonth,
    IReadOnlyList<MonthRow> GrabsPerMonth,
    IReadOnlyList<MonthRow> BytesPerMonth,
    IReadOnlyList<GrabberRow> Grabbers,
    IReadOnlyList<JobRunCounts> JobRuns,
    DateTimeOffset BuiltAt)
{
    /// <summary>The months the monthly tables cover.</summary>
    public const int Months = 12;

    /// <summary>The days the jobs table covers.</summary>
    public const int JobDays = 30;

    public int Books => ByMediaType.Sum(r => r.Count);

    public static async Task<StatisticsView> BuildAsync(
        LibraryStatistics statistics, FetchshelfSettings settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // One after the other: they share the request's database context.
        var byMediaType = Spaced(await statistics.BooksByMediaTypeAsync(cancellationToken));
        var byLanguage = Spaced(await statistics.BooksByLanguageAsync(cancellationToken));
        var byLibrary = await statistics.BooksByLibraryAsync(cancellationToken);
        var bytes = await statistics.LibraryBytesAsync(cancellationToken);
        var added = await statistics.BooksAddedPerMonthAsync(now, Months, cancellationToken);
        var grabs = await statistics.GrabsPerMonthAsync(now, Months, cancellationToken);
        var grabbedBytes = await statistics.BytesGrabbedPerMonthAsync(now, Months, cancellationToken);
        var grabbers = await statistics.GrabsByGrabberAsync(now, Months, cancellationToken);
        var jobRuns = await statistics.JobRunsAsync(now, JobDays, cancellationToken);
        var kindOf = GrabberKinds(settings);
        return new StatisticsView(
            byMediaType, byLanguage, byLibrary, bytes, added, grabs, grabbedBytes,
            grabbers.Select(g => new GrabberRow(g.Label.Length == 0 ? "(unnamed)" : g.Label, kindOf(g.Label), g.Count)).ToList(),
            jobRuns, now);
    }

    /// <summary>Media types and languages as people write them: "Comic Book", "Brazilian Portuguese".</summary>
    private static List<CountRow> Spaced(IReadOnlyList<CountRow> rows) =>
        rows.Select(r => r with { Label = string.Concat(r.Label.Select((c, i) => i > 0 && char.IsUpper(c) && char.IsLower(r.Label[i - 1]) ? " " + c : c.ToString())) })
            .ToList();

    /// <summary>
    /// What kind of block a grabber name is: an autograb or a list by its configured name, the Follow job's
    /// <c>Follow: …</c>, the Upgrade job, or the Search page. A name no longer configured is "Other".
    /// </summary>
    public static Func<string, string> GrabberKinds(FetchshelfSettings settings)
    {
        var autograbs = settings.Autograb.Select(a => a.Name).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lists = ListDefinition.AllOf(settings).Select(l => l.JobName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return name =>
            name.StartsWith(FollowRunner.GrabberPrefix, StringComparison.Ordinal) ? "Follow"
            : name == JobNames.Upgrade ? "Upgrade"
            : name == JobNames.ManualSearchGrabber ? "Search page"
            : lists.Contains(name) ? "List"
            : autograbs.Contains(name) ? "Autograb"
            : "Other";
    }
}
