using Fetchshelf.Web.Jobs;

namespace Fetchshelf.Web.Tests;

public sealed class BulkOperationQueueTests
{
    private static BulkOperationQueue Queue() => new(TimeProvider.System);

    [Fact]
    public void Enqueue_GivesEachItsOwnId_AndQueuesThemInOrder()
    {
        var queue = Queue();

        var first = queue.Enqueue(BulkAction.Relink, ["a", "b"]);
        var second = queue.Enqueue(BulkAction.SetCategory, ["c"], "seed-only");

        Assert.True(queue.Pending.TryRead(out var read1) && read1 == first);
        Assert.True(queue.Pending.TryRead(out var read2) && read2 == second);
        Assert.Equal([first, second], queue.Active());
        Assert.Equal("Relink of 2 torrents: waits for the bulk action before it.", first.Status());
        Assert.Equal("Setting the category to seed-only for 1 torrent", second.Describe());
    }

    [Fact]
    public void Cancel_AWaitingOperation_EndsItBeforeItStarts()
    {
        var queue = Queue();
        var operation = queue.Enqueue(BulkAction.Unlink, ["a"]);

        Assert.True(queue.Cancel(operation.Id));

        Assert.Equal(BulkOperationState.Cancelled, operation.State);
        Assert.Equal("Unlink of 1 torrent: cancelled before it started.", operation.Status());
        Assert.False(operation.TryStart());
        Assert.Empty(queue.Active());
        Assert.False(queue.Cancel(operation.Id));
    }

    [Fact]
    public void Cancel_ARunningOperation_SignalsIt()
    {
        var queue = Queue();
        var operation = queue.Enqueue(BulkAction.Relink, ["a"]);
        Assert.True(operation.TryStart());

        Assert.True(queue.Cancel(operation.Id));

        Assert.True(operation.Cancellation.IsCancellationRequested);
        Assert.Equal(BulkOperationState.Running, operation.State);
    }

    [Fact]
    public void Dismiss_OnlyAnEndedOperation_AndHidesIt()
    {
        var queue = Queue();
        var operation = queue.Enqueue(BulkAction.Clean, ["a"]);
        Assert.False(queue.Dismiss(operation.Id));

        operation.End(BulkOperationState.Finished, "Cleaned 1 torrent.");

        Assert.True(queue.Dismiss(operation.Id));
        Assert.Empty(queue.Shown());
    }

    [Fact]
    public void Ended_KeepsOnlyTheLastTen()
    {
        var queue = Queue();
        var operations = Enumerable.Range(0, 12).Select(_ => queue.Enqueue(BulkAction.Refresh, ["a"])).ToList();
        foreach (var operation in operations)
        {
            operation.End(BulkOperationState.Finished, $"Refreshed {operation.Id}.");
            queue.Ended(operation);
        }

        Assert.Equal(operations.Skip(2), queue.Shown());
    }

    [Fact]
    public void Status_WhileRunning_CountsFromTheOneBeingHandled()
    {
        var operation = Queue().Enqueue(BulkAction.Relink, ["a", "b", "c"]);
        operation.TryStart();

        operation.Report(1, "The Way of Kings");

        Assert.Equal("Relinking 2 of 3: The Way of Kings", operation.Status());
    }

    [Theory]
    [InlineData(BulkOperationState.Finished, 3, 3, 0, "Relinked 3 torrents.")]
    [InlineData(BulkOperationState.Finished, 3, 3, 1, "Relinked 2 of 3 torrents.")]
    [InlineData(BulkOperationState.Cancelled, 5, 2, 0, "Relinked 2 of 5 torrents. Cancelled after 2; the other 3 were left as they were.")]
    [InlineData(BulkOperationState.Interrupted, 5, 1, 0, "Relinked 1 of 5 torrents. Fetchshelf stopped after 1; the other 4 were left as they were.")]
    public void Summary_SaysHowItWent(BulkOperationState state, int total, int handled, int problems, string expected)
    {
        var operation = new BulkOperation(1, BulkAction.Relink, [], null, DateTimeOffset.UnixEpoch);
        Assert.Equal(expected, BulkActionRunner.Summary(operation, BulkOperation.Verbs.For(BulkAction.Relink, null), total, handled, handled - problems, problems, state));
    }

    [Theory]
    [InlineData(BulkAction.DeleteFindings, 1, "Deleting 1 finding", "Deleted 1 finding.")]
    [InlineData(BulkAction.DeleteFindings, 2800, "Deleting 2800 findings", "Deleted 2800 findings.")]
    [InlineData(BulkAction.RelinkFindings, 4, "Relinking 4 books", "Relinked 4 books.")]
    public void LibraryCheckActions_SpeakOfFindingsAndBooks(BulkAction action, int count, string described, string summary)
    {
        var operation = new BulkOperation(1, action, Enumerable.Range(0, count).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList(), null, DateTimeOffset.UnixEpoch);

        Assert.True(operation.OnLibraryCheck);
        Assert.Equal(described, operation.Describe());
        Assert.Equal(summary, BulkActionRunner.Summary(operation, BulkOperation.Verbs.For(action, null), count, count, count, 0, BulkOperationState.Finished));
    }

    [Fact]
    public void Shown_ByPage_KeepsTheTorrentsPagesAndTheLibraryChecksApart()
    {
        var queue = new BulkOperationQueue(TimeProvider.System);
        var relink = queue.Enqueue(BulkAction.Relink, ["hash1"]);
        var delete = queue.Enqueue(BulkAction.DeleteFindings, ["7"]);

        Assert.Equal([relink], queue.Shown(libraryCheck: false));
        Assert.Equal([delete], queue.Shown(libraryCheck: true));
        Assert.Equal([relink, delete], queue.Active());
    }
}
