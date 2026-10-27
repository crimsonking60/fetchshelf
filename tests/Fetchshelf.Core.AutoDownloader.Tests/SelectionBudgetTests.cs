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
}
