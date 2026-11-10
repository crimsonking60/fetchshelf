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

    [Fact]
    public void Translate_KeyThatIsOnlyAStringPrefixOfASiblingFolder_DoesNotTranslateIt()
    {
        var result = PathMapper.Translate(new Dictionary<string, string> { ["/downloads"] = "/data/downloads" }, "/downloads2/book");

        Assert.Equal("/downloads2/book", result);
    }

    [Theory]
    [InlineData("/downloads", "/data/downloads")]
    [InlineData("/downloads/", "/data/downloads/")]
    [InlineData("/downloads/", "/data/downloads")]
    [InlineData("/downloads", "/data/downloads/")]
    public void Translate_TrailingSlashOnKeyOrValue_GivesOneSeparator(string key, string value)
    {
        var mapping = new Dictionary<string, string> { [key] = value };

        Assert.Equal("/data/downloads/book", PathMapper.Translate(mapping, "/downloads/book"));
    }

    [Theory]
    [InlineData("/downloads")]
    [InlineData("/downloads/")]
    public void Translate_PathThatIsTheKeyItself_BecomesTheValue(string key)
    {
        var mapping = new Dictionary<string, string> { [key] = "/data/downloads" };

        Assert.Equal("/data/downloads", PathMapper.Translate(mapping, "/downloads"));
    }

    [Fact]
    public void Translate_NestedKeys_LongestWins()
    {
        var mapping = new Dictionary<string, string> { ["/downloads"] = "/a", ["/downloads/audio"] = "/b" };

        Assert.Equal("/b/book", PathMapper.Translate(mapping, "/downloads/audio/book"));
        Assert.Equal("/a/audiobooks/book", PathMapper.Translate(mapping, "/downloads/audiobooks/book"));
    }
}
