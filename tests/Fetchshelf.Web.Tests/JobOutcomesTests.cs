using Fetchshelf.Core.Jobs;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Jobs;

namespace Fetchshelf.Web.Tests;

public class JobOutcomesTests
{
    [Fact]
    public void Link_SomeLinked_SaysHowManyWithCounts()
    {
        var outcome = JobOutcomes.Link(linked: 3, failed: 0);

        Assert.Equal("3 torrents linked", outcome.Summary);
        Assert.Equal(3, outcome.Counts!["linked"]);
    }

    [Fact]
    public void Link_SomeFailed_FinishesWithErrorsCarryingTheWholeLine()
    {
        var exception = Assert.Throws<JobCompletedWithErrorsException>(() => JobOutcomes.Link(linked: 2, failed: 1));

        Assert.Equal("2 torrents linked; 1 could not be linked; see the Errors page", exception.Message);
        Assert.Equal(1, exception.Counts!["failed"]);
    }

    [Fact]
    public void Autograb_Results_SaysHowManyWereReadAndSelected() =>
        Assert.Equal(
            "312 search results, 2 selected",
            JobOutcomes.Autograb(new AutograbRunSummary(312, 2, DryRun: false)).Summary);

    [Fact]
    public void Autograb_NoBudget_SaysThereWasNothingToDo() =>
        Assert.Equal(
            "Nothing to do: this run's limit is 0 selections (why)",
            JobOutcomes.Autograb(new AutograbRunSummary(0, 0, DryRun: false, "this run's limit is 0 selections (why)")).Summary);

    [Fact]
    public void Autograb_DryRunWithRemovalChecks_SaysWhatWouldHaveHappened()
    {
        var outcome = JobOutcomes.Autograb(new AutograbRunSummary(100, 4, DryRun: true, CheckedForRemoval: 20, MarkedRemoved: 1));

        Assert.Equal(
            "Dry run: 100 search results, 4 would have been selected; 20 checked for removal from MAM, 1 would have been marked removed",
            outcome.Summary);
        Assert.Equal(1, outcome.Counts!["marked_removed"]);
    }

    [Fact]
    public void HardcoverList_DryRun_SaysWhatWouldHaveBeenSelected()
    {
        var outcome = JobOutcomes.HardcoverList(new HardcoverListRunSummary(5, 0, Books: 20, Searched: 12, DryRun: true));

        Assert.Equal("Dry run: 20 books, 12 searched on MAM, 5 would have been selected", outcome.Summary);
        Assert.Equal(5, outcome.Counts!["selected"]);
    }

    [Fact]
    public void HardcoverList_Selections_SaysHowManyWereSelected() =>
        Assert.Equal(
            "20 books, 12 searched on MAM, 5 selected",
            JobOutcomes.HardcoverList(new HardcoverListRunSummary(5, 0, Books: 20, Searched: 12)).Summary);

    [Fact]
    public void Clean_NothingToRetire_SaysSo() =>
        Assert.Equal("No superseded copies to retire", JobOutcomes.Clean(new CleanRunSummary(0, 0)).Summary);

    [Fact]
    public void Download_SomeQueued_SaysHowManyWereAdded() =>
        Assert.Equal("1 of 3 queued torrents added to qBittorrent", JobOutcomes.Download(new DownloadRunSummary(1, 0, 3)).Summary);
}
