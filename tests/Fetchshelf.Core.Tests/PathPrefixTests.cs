using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class PathPrefixTests
{
    [Theory]
    [InlineData("/data/audio", "/data/audio")]
    [InlineData("/data/audio/book", "/data/audio")]
    [InlineData("/data/audio/author/book", "/data/audio")]
    [InlineData("/data/audio/book", "/data/audio/")]
    [InlineData("/data/audio", "/data/audio/")]
    [InlineData("/data/audio/book", "/data/audio\\")]
    [InlineData("/data/audio\\book", "/data/audio")]
    [InlineData("/data/audio/", "/data/audio")]
    [InlineData("/data/audio", "/")]
    public void IsSameOrUnder_PathInTheFolder_IsTrue(string path, string folder)
    {
        Assert.True(PathPrefix.IsSameOrUnder(path, folder));
    }

    [Theory]
    [InlineData("/data/audiobooks2/book", "/data/audio")]
    [InlineData("/data/audiobooks2", "/data/audio")]
    [InlineData("/data/audiobooks2", "/data/audio/")]
    [InlineData("/data", "/data/audio")]
    [InlineData("/Data/audio/book", "/data/audio")]
    [InlineData("/data/audio/book", "")]
    public void IsSameOrUnder_PathOutsideTheFolder_IsFalse(string path, string folder)
    {
        Assert.False(PathPrefix.IsSameOrUnder(path, folder));
    }
}
