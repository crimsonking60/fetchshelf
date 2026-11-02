using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class NullMetadataLookupTests
{
    [Fact]
    public async Task TryLookupByInfoHashAsync_AnyHash_ReturnsNull()
    {
        var lookup = new NullMetadataLookup();

        var result = await lookup.TryLookupByInfoHashAsync("abc123");

        Assert.Null(result);
    }
}
