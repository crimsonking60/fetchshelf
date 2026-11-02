using Fetchshelf.Clients.Mam;

namespace Fetchshelf.Clients.Mam.Tests;

public class MamClientOptionsTests
{
    [Fact]
    public void Constructor_NothingSet_UsesDocumentedDefaults()
    {
        var options = new MamClientOptions { MamId = "session-cookie-value" };

        Assert.Equal("https://www.myanonamouse.net", options.BaseUrl);
        Assert.Equal("https://cdn.myanonamouse.net", options.CdnBaseUrl);
        Assert.Equal("Fetchshelf/0.1.0", options.UserAgent);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
    }
}
