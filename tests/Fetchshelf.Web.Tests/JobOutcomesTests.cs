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
        var outcome = JobOutcomes.Link(new LinkRunSummary(Linked: 3, Failed: 0));

        Assert.Equal("3 torrents linked", outcome.Summary);
        Assert.Equal(3, outcome.Counts!["linked"]);
    }

    [Fact]
    public void Link_Nothing_SaysNothingNewToLink() =>
        Assert.Equal("Nothing new to link", JobOutcomes.Link(new LinkRunSummary(0, 0)).Summary);

    [Fact]
    public void Link_WithABacklog_SaysWhatWaits_AndCountsIt()
    {
        var outcome = JobOutcomes.Link(new LinkRunSummary(Linked: 3, Failed: 0, WaitingForLookup: 450, WaitingForMam: 12));

        Assert.Equal("3 torrents linked; 450 more to look up on MAM in the next runs; 12 torrents MAM didn't know, looked up again once a day", outcome.Summary);
        Assert.Equal((450L, 12L), (outcome.Counts!["waiting_for_lookup"], outcome.Counts["waiting_for_mam"]));
    }

    [Fact]
    public void Link_OnlyABacklog_IsNotNothingNewToLink() =>
        Assert.Equal(
            "Nothing linked; 1 torrent MAM didn't know, looked up again once a day",
            JobOutcomes.Link(new LinkRunSummary(0, 0, WaitingForMam: 1)).Summary);

    [Fact]
    public void Link_WithCovers_SaysHowManyWereWrittenAndWait_AndCountsThem()
    {
        var outcome = JobOutcomes.Link(new LinkRunSummary(Linked: 1, Failed: 0), new CoverRunSummary(FromFiles: 0, Fetched: 20, Waiting: 130));

        Assert.Equal("1 torrent linked; 20 covers fetched, up to 130 more in the next runs", outcome.Summary);
        Assert.Equal((20L, 130L), (outcome.Counts!["covers_written"], outcome.Counts["covers_waiting"]));
    }

    [Theory]
    [InlineData(12, 0, "1 torrent linked; 12 covers written from the books' files")]
    [InlineData(8, 4, "1 torrent linked; 12 covers written (8 from the books' files, 4 fetched)")]
    public void Link_WithCovers_SaysWhereTheyCameFrom(int fromFiles, int fetched, string expected) =>
        Assert.Equal(expected, JobOutcomes.Link(new LinkRunSummary(Linked: 1, Failed: 0), new CoverRunSummary(fromFiles, fetched, 0)).Summary);

    [Fact]
    public void Link_WithCovers_NamesTheSourcesTheyWereFetchedFrom()
    {
        var both = new CoverRunSummary(8, 4, 40) { FetchedFrom = new Dictionary<string, int> { ["Audnexus"] = 1, ["Open Library"] = 3 } };
        var one = new CoverRunSummary(0, 3, 0) { FetchedFrom = new Dictionary<string, int> { ["Open Library"] = 3 } };

        Assert.Equal(
            "1 torrent linked; 12 covers written (8 from the books' files, 3 from Open Library, 1 from Audnexus), up to 40 more in the next runs",
            JobOutcomes.Link(new LinkRunSummary(Linked: 1, Failed: 0), both).Summary);
        Assert.Equal("1 torrent linked; 3 covers written from Open Library", JobOutcomes.Link(new LinkRunSummary(Linked: 1, Failed: 0), one).Summary);
    }

    [Fact]
    public void Link_WithoutCovers_SaysNothingOfThem()
    {
        Assert.Equal("Nothing new to link", JobOutcomes.Link(new LinkRunSummary(0, 0), new CoverRunSummary(0, 0, 0)).Summary);
        Assert.False(JobOutcomes.Link(new LinkRunSummary(0, 0)).Counts!.ContainsKey("covers_written"));
    }

    [Fact]
    public void LibraryCheck_SaysWhatItFoundByKind_OrThatEverythingMatches()
    {
        Assert.Equal(
            "2 unknown folders, 1 book with its folder missing, 3 books with files missing; see the Library check page",
            JobOutcomes.LibraryCheck(new LibraryCheckSummary(2, 0, 1, 3)).Summary);
        Assert.Equal("The library matches the records", JobOutcomes.LibraryCheck(new LibraryCheckSummary(0, 0, 0, 0)).Summary);
    }

    [Fact]
    public void Link_SomeFailed_FinishesWithErrorsCarryingTheWholeLine()
    {
        var exception = Assert.Throws<JobCompletedWithErrorsException>(() => JobOutcomes.Link(new LinkRunSummary(Linked: 2, Failed: 1)));

        Assert.Equal("2 torrents linked; 1 could not be linked; see the Errors page", exception.Message);
        Assert.Equal(1, exception.Counts!["failed"]);
    }

    [Fact]
    public void Upgrade_NamesTheBooksSearched_TheFirstFiveAndHowManyMore() =>
        Assert.Equal(
            "7 books searched (\"A\", \"B\", \"C\", \"D\", \"E\" and 2 more), 1 better copy selected",
            JobOutcomes.Upgrade(new UpgradeRunSummary(7, 1, 0, SearchedTitles: ["A", "B", "C", "D", "E", "F", "G"])).Summary);

    [Fact]
    public void Bonus_Bought_NamesThePurchases()
    {
        var outcome = JobOutcomes.Bonus(new BonusRunSummary(["a wedge", "50 GB of upload credit"], DryRun: false, "why"));

        Assert.Equal("Bought a wedge, 50 GB of upload credit", outcome.Summary);
        Assert.Equal(2, outcome.Counts!["bought"]);
    }

    [Fact]
    public void Bonus_NothingBought_SaysWhy() =>
        Assert.Equal(
            "Nothing bought: the 41,250 points leave nothing above keep_points 55,000",
            JobOutcomes.Bonus(new BonusRunSummary([], DryRun: false, "the 41,250 points leave nothing above keep_points 55,000")).Summary);

    [Fact]
    public void Bonus_DryRun_SaysWhatItWouldBuyAndCountsNothing()
    {
        var outcome = JobOutcomes.Bonus(new BonusRunSummary(["a wedge"], DryRun: true, "why"));

        Assert.Equal("Dry run: Would buy a wedge", outcome.Summary);
        Assert.Equal(0, outcome.Counts!["bought"]);
    }

    [Fact]
    public void Bonus_FailureAfterPurchases_FinishesWithErrorsNamingBoth()
    {
        var exception = Assert.Throws<JobCompletedWithErrorsException>(() =>
            JobOutcomes.Bonus(new BonusRunSummary(["a wedge"], DryRun: false, "MAM refused", Failure: "MAM refused 50 GB of upload credit: Invalid amount")));

        Assert.Equal("Bought a wedge; then MAM refused 50 GB of upload credit: Invalid amount", exception.Message);
    }

    [Fact]
    public void Bonus_FailureBeforeAnyPurchase_FinishesWithErrorsSayingWhy()
    {
        var exception = Assert.Throws<JobCompletedWithErrorsException>(() =>
            JobOutcomes.Bonus(new BonusRunSummary([], DryRun: false, "x", Failure: "MAM refused 50 GB of upload credit: Invalid amount")));

        Assert.Equal("Nothing bought: MAM refused 50 GB of upload credit: Invalid amount", exception.Message);
    }

    [Fact]
    public void Upgrade_NothingSearched_SaysWhy() =>
        Assert.Equal(
            "Nothing searched: why",
            JobOutcomes.Upgrade(new UpgradeRunSummary(0, 0, 0, Note: "why")).Summary);

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
    public void List_DryRun_SaysWhatWouldHaveBeenSelected()
    {
        var outcome = JobOutcomes.List(new ListRunSummary(5, 0, Books: 20, Searched: 12, DryRun: true));

        Assert.Equal("Dry run: 20 books, 12 searched on MAM, 5 would have been selected", outcome.Summary);
        Assert.Equal(5, outcome.Counts!["selected"]);
    }

    [Fact]
    public void List_OfBookmarks_CountsBookmarksLookedAt() =>
        Assert.Equal(
            "Dry run: 40 bookmarks, 12 looked at, 2 would have been selected",
            JobOutcomes.List(new ListRunSummary(2, 0, Books: 40, Searched: 12, DryRun: true, Bookmarks: true)).Summary);

    [Fact]
    public void List_UnreadableLinesOfAFile_AreCountedInTheSummary()
    {
        var outcome = JobOutcomes.List(new ListRunSummary(1, 0, Books: 20, Searched: 12, UnreadableLines: 2));

        Assert.Equal("20 books, 12 searched on MAM, 1 selected; 2 lines of the file could not be read, see the log", outcome.Summary);
        Assert.Equal(2, outcome.Counts!["unreadable_lines"]);
    }

    [Fact]
    public void List_Selections_SaysHowManyWereSelected() =>
        Assert.Equal(
            "20 books, 12 searched on MAM, 5 selected",
            JobOutcomes.List(new ListRunSummary(5, 0, Books: 20, Searched: 12)).Summary);

    [Fact]
    public void Clean_NothingToRetire_SaysSo() =>
        Assert.Equal("No superseded copies to retire", JobOutcomes.Clean(new CleanRunSummary(0, 0)).Summary);

    [Fact]
    public void Clean_WeeklyCoverPrune_IsAddedWhenItRemovedSomething()
    {
        var pruned = JobOutcomes.Clean(new CleanRunSummary(0, 0), prunedCovers: 12);
        var nothingPruned = JobOutcomes.Clean(new CleanRunSummary(0, 0), prunedCovers: 0);

        Assert.Equal("No superseded copies to retire; 12 cached covers of books no longer in the library removed", pruned.Summary);
        Assert.Equal(12, pruned.Counts!["covers_pruned"]);
        Assert.Equal("No superseded copies to retire", nothingPruned.Summary);
        Assert.False(JobOutcomes.Clean(new CleanRunSummary(0, 0)).Counts!.ContainsKey("covers_pruned"));
    }

    [Fact]
    public void Download_SelectionsLeftQueued_SaysWhichBudgetHeldThemBack()
    {
        var outcome = JobOutcomes.Download(new DownloadRunSummary(1, 0, 4, 3, "the ratio budget is 0 B (ratio 2.01, min_ratio 2)"));

        Assert.Equal("1 of 4 queued torrents added to qBittorrent; 3 stay queued: the ratio budget is 0 B (ratio 2.01, min_ratio 2)", outcome.Summary);
        Assert.Equal(3, outcome.Counts!["left_queued"]);
    }

    [Fact]
    public void Download_TorrentsThatFailed_PointToTheErrorsPage()
    {
        var failed = Assert.Throws<JobCompletedWithErrorsException>(() => JobOutcomes.Download(new DownloadRunSummary(8, 1, 15, 5, "no slot is free")));

        Assert.Equal("8 of 15 queued torrents added to qBittorrent; 5 stay queued: no slot is free; 1 could not be downloaded; see the Errors page", failed.Message);
    }

    [Fact]
    public void Download_StoppedByMam_SaysWhatMamAnswered_AndCountsOnlyTheOtherFailures()
    {
        var stopped = Assert.Throws<JobCompletedWithErrorsException>(() =>
            JobOutcomes.Download(new DownloadRunSummary(2, 1, 5, StoppedByMam: true, StopReason: "MAM answered 429 (too many requests).")));
        var both = Assert.Throws<JobCompletedWithErrorsException>(() =>
            JobOutcomes.Download(new DownloadRunSummary(2, 2, 5, StoppedByMam: true, StopReason: "MAM answered 429 (too many requests).")));

        Assert.Equal(
            "2 of 5 queued torrents added to qBittorrent; stopped when MAM refused a request (MAM answered 429 (too many requests).); "
            + "the rest stay queued for the next run",
            stopped.Message);
        Assert.EndsWith("the rest stay queued for the next run; 1 could not be downloaded; see the Errors page", both.Message);
    }

    [Fact]
    public void Download_SomeQueued_SaysHowManyWereAdded() =>
        Assert.Equal("1 of 3 queued torrents added to qBittorrent", JobOutcomes.Download(new DownloadRunSummary(1, 0, 3)).Summary);

    [Fact]
    public void Follow_SeriesAddedFromAudiobookshelf_AreInTheSummary()
    {
        var outcome = JobOutcomes.Follow(new FollowRunSummary(3, 1, 2, 0, AddedFromAudiobookshelf: 40));

        Assert.Equal("3 series and authors searched, 1 selected, 2 new to look at; 40 series added from Audiobookshelf", outcome.Summary);
        Assert.Equal(40, outcome.Counts!["added_from_audiobookshelf"]);
    }

    [Fact]
    public void Follow_DryRun_SaysWhatWouldHaveBeenAddedFromAudiobookshelf() =>
        Assert.EndsWith(
            "; 40 series would have been added from Audiobookshelf",
            JobOutcomes.Follow(new FollowRunSummary(3, 1, 2, 0, DryRun: true, AddedFromAudiobookshelf: 40)).Summary);
}
