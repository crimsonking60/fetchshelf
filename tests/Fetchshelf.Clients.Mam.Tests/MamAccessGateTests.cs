namespace Fetchshelf.Clients.Mam.Tests;

public class MamAccessGateTests
{
    [Fact]
    public async Task AcquireAsync_SecondCallWaitsUntilFirstHandleDisposed()
    {
        var gate = new MamAccessGate();
        var firstHandle = await gate.AcquireAsync();

        var secondAcquireTask = gate.AcquireAsync();
        await Task.Delay(50);
        Assert.False(secondAcquireTask.IsCompleted);

        firstHandle.Dispose();
        var secondHandle = await secondAcquireTask;

        Assert.True(secondAcquireTask.IsCompletedSuccessfully);
        secondHandle.Dispose();
    }
}
