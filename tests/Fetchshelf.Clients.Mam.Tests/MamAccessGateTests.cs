namespace Fetchshelf.Clients.Mam.Tests;

public class MamAccessGateTests
{
    [Fact]
    public async Task AcquireAsync_GateAlreadyHeld_WaitsUntilFirstHandleDisposed()
    {
        var gate = new MamAccessGate();
        var firstHandle = await gate.AcquireAsync("another job");

        var secondAcquireTask = gate.AcquireAsync("another job");
        await Task.Delay(50);
        Assert.False(secondAcquireTask.IsCompleted);

        firstHandle.Dispose();
        var secondHandle = await secondAcquireTask;

        Assert.True(secondAcquireTask.IsCompletedSuccessfully);
        secondHandle.Dispose();
    }

    [Fact]
    public async Task TryAcquireAsync_HeldByAnotherOwner_TimesOutAndSaysWhoHoldsIt()
    {
        var gate = new MamAccessGate();
        var held = await gate.AcquireAsync("Autograb Freeleech");

        var acquired = await gate.TryAcquireAsync("Account page", TimeSpan.FromMilliseconds(50));

        Assert.Null(acquired);
        Assert.Equal("Autograb Freeleech", gate.Owner);
        held.Dispose();
    }

    [Fact]
    public async Task TryAcquireAsync_FreeGate_TakesItAndReleasingClearsTheOwner()
    {
        var gate = new MamAccessGate();

        var acquired = await gate.TryAcquireAsync("Account page", TimeSpan.FromMilliseconds(50));

        Assert.NotNull(acquired);
        Assert.Equal("Account page", gate.Owner);
        acquired.Dispose();
        Assert.Null(gate.Owner);
        acquired.Dispose(); // a second release must not free the gate for two holders
        using var next = await gate.AcquireAsync("Download");
        Assert.Null(await gate.TryAcquireAsync("Account page", TimeSpan.FromMilliseconds(20)));
    }
}
