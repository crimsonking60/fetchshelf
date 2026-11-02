namespace Fetchshelf.Core.AutoDownloader.Tests;

public class SelectionBudgetTests
{
    [Fact]
    public void PlainUnsatBasedBudget()
    {
        var budget = SelectionBudget.Compute(unsatLimit: 20, unsatCount: 5, unsatBuffer: 10, maxActiveDownloads: null, currentActiveForGrabber: 0);

        Assert.Equal(5, budget);
    }

    [Fact]
    public void MaxActiveDownloads_CapsLargerUnsatBudget()
    {
        var budget = SelectionBudget.Compute(unsatLimit: 50, unsatCount: 0, unsatBuffer: 0, maxActiveDownloads: 3, currentActiveForGrabber: 2);

        Assert.Equal(1, budget);
    }

    [Fact]
    public void PerGrabberBuffer_OverridesGlobalBuffer()
    {
        // Caller passes the already-resolved effective buffer (grabber's own, else global) - this
        // test exercises that a smaller per-grabber buffer yields a correspondingly larger budget
        // than the global buffer would.
        var withGrabberBuffer = SelectionBudget.Compute(unsatLimit: 20, unsatCount: 5, unsatBuffer: 5, maxActiveDownloads: null, currentActiveForGrabber: 0);
        var withGlobalBuffer = SelectionBudget.Compute(unsatLimit: 20, unsatCount: 5, unsatBuffer: 10, maxActiveDownloads: null, currentActiveForGrabber: 0);

        Assert.Equal(10, withGrabberBuffer);
        Assert.Equal(5, withGlobalBuffer);
        Assert.NotEqual(withGrabberBuffer, withGlobalBuffer);
    }

    [Fact]
    public void Explain_UnsatSlotsAreTheLimit_ShowsTheSlotSum() =>
        Assert.Equal(
            "unsatisfied-snatch slots: 200 allowed - 10 in use - unsat_buffer 10",
            SelectionBudget.Explain(unsatLimit: 200, unsatCount: 10, unsatBuffer: 10, maxActiveDownloads: 500, currentActiveForGrabber: 0));

    [Fact]
    public void Explain_MaxActiveDownloadsIsTighter_NamesIt() =>
        Assert.Equal(
            "max_active_downloads 3 - 2 already queued",
            SelectionBudget.Explain(unsatLimit: 50, unsatCount: 0, unsatBuffer: 0, maxActiveDownloads: 3, currentActiveForGrabber: 2));
}
