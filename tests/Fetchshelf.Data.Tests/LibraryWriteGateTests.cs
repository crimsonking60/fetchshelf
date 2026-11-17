using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Data.Tests;

public sealed class LibraryWriteGateTests
{
    [Fact]
    public async Task OneHolderAtATime_AndDisposingTwiceReleasesOnce()
    {
        var gate = new LibraryWriteGate();
        var held = await gate.AcquireAsync();

        Assert.Null(gate.TryAcquire());
        var waiting = gate.AcquireAsync();
        Assert.False(waiting.IsCompleted);

        held.Dispose();
        held.Dispose();
        using var next = await waiting;
        Assert.Null(gate.TryAcquire());
    }

    [Fact]
    public async Task AcquireAsync_Cancelled_StopsWaiting()
    {
        var gate = new LibraryWriteGate();
        using var held = await gate.AcquireAsync();
        using var cancel = new CancellationTokenSource();

        var waiting = gate.AcquireAsync(cancel.Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}
