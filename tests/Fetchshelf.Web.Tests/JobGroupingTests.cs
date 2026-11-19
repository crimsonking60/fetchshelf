using Fetchshelf.Data.Configuration;
using Fetchshelf.Web.Pages.Jobs;

namespace Fetchshelf.Web.Tests;

public class JobGroupingTests
{
    private static JobStatusViewModel Job(string name) => new(name, null, null, "Never run", false, null);

    private static FetchshelfSettings Settings(params string[] autograbNames) =>
        new() { Autograb = autograbNames.Select(n => new AutograbEntry { Name = n }).ToList() };

    private static string[] Names(JobListViewModel list, JobGroup group) =>
        list.Groups.Single(g => g.Group == group).Jobs.Select(j => j.Name).ToArray();

    [Fact]
    public void Build_PutsStandardJobsOnTop_ConfiguredNext_AndAutograbLast()
    {
        var jobs = new[] { "BM 1", "Bonus", "Clean", "Download", "Audiobookshelf", "Link", "BM 2", "Follow" }.Select(Job);

        var list = JobGrouping.Build(jobs, Settings("BM 1", "BM 2"));

        Assert.Equal([JobGroup.Standard, JobGroup.Configured, JobGroup.Autograb], list.Groups.Select(g => g.Group).ToArray());
        Assert.Equal(["Download", "Link", "Clean"], Names(list, JobGroup.Standard));
        Assert.Equal(["Audiobookshelf", "Bonus", "Follow"], Names(list, JobGroup.Configured));
        Assert.Equal(["BM 1", "BM 2"], Names(list, JobGroup.Autograb));
    }

    [Fact]
    public void Build_AutograbJobs_KeepTheOrderOfTheConfiguration_NotAlphabetical()
    {
        var jobs = new[] { "Zeta", "Alpha", "Mid" }.Select(Job);

        var list = JobGrouping.Build(jobs, Settings("Zeta", "Alpha", "Mid"));

        Assert.Equal(["Zeta", "Alpha", "Mid"], Names(list, JobGroup.Autograb));
    }

    [Fact]
    public void Build_UnnamedAutograbBlocks_AreFoundByTheirGeneratedNames()
    {
        var list = JobGrouping.Build([Job("autograb[0]"), Job("Link")], new FetchshelfSettings { Autograb = [new AutograbEntry()] });

        Assert.Equal(["autograb[0]"], Names(list, JobGroup.Autograb));
    }

    [Fact]
    public void Build_ListJobs_ComeAfterTheBuiltInConfiguredJobs()
    {
        var settings = new FetchshelfSettings
        {
            FileList = [new FileListEntry { Name = "My books" }],
            BookmarksList = [new BookmarksListEntry()],
        };

        var list = JobGrouping.Build(new[] { "My books", "Seeding", "bookmarks_list", "Bonus" }.Select(Job), settings);

        Assert.Equal(["Bonus", "Seeding", "My books", "bookmarks_list"], Names(list, JobGroup.Configured));
    }

    [Fact]
    public void Build_AGroupWithNoJobs_IsLeftOut()
    {
        var list = JobGrouping.Build([Job("Link")], Settings());

        Assert.Equal([JobGroup.Standard], list.Groups.Select(g => g.Group).ToArray());
        Assert.False(list.IsEmpty);
        Assert.True(JobGrouping.Build([], Settings()).IsEmpty);
    }

    [Fact]
    public void Group_CountsItsFailingJobs()
    {
        var failing = Job("Bonus") with { Failing = new Fetchshelf.Core.Jobs.JobFailure(DateTimeOffset.UnixEpoch, 3, null, Fetchshelf.Core.Jobs.JobFailureKind.Other) };

        var list = JobGrouping.Build([failing, Job("Follow")], Settings());

        Assert.Equal(1, list.Groups.Single().FailingCount);
    }
}
