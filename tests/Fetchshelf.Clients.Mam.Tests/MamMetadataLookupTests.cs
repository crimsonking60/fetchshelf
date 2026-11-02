using System.Net;

namespace Fetchshelf.Clients.Mam.Tests;

public class MamMetadataLookupTests
{
    [Fact]
    public async Task TryLookupByInfoHashAsync_GateHeldByAnotherJob_WaitsBeforeAskingMam()
    {
        var mamCalls = 0;
        var client = new MamClient(
            new HttpClient(new FakeHttpMessageHandler(_ =>
            {
                mamCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"error":"Nothing returned, out of 0"}""") };
            })),
            new MamClientOptions { MamId = "configured-mam-id" });
        var gate = new MamAccessGate();
        var lookup = new MamMetadataLookup(client, gate);
        var otherJob = await gate.AcquireAsync();

        var lookupTask = lookup.TryLookupByInfoHashAsync("hash1");

        Assert.False(lookupTask.IsCompleted);
        Assert.Equal(0, mamCalls);

        otherJob.Dispose();
        Assert.Null(await lookupTask);
        Assert.Equal(1, mamCalls);
    }
}
