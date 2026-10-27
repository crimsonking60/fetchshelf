using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class PathMapperTests
{
    [Fact]
    public void Translate_LongestPrefixMatches_ReplacesIt()
    {
        var mapping = new Dictionary<string, string> { ["/data"] = "/downloads", ["/data/audio"] = "/downloads/audiobooks" };

        var result = PathMapper.Translate(mapping, "/data/audio/book1");

        Assert.Equal("/downloads/audiobooks/book1", result);
    }

    [Fact]
    public void Translate_NoPrefixMatches_ReturnsPathUnchanged()
    {
        var result = PathMapper.Translate(new Dictionary<string, string> { ["/other"] = "/mapped" }, "/data/book1");

        Assert.Equal("/data/book1", result);
    }
}
