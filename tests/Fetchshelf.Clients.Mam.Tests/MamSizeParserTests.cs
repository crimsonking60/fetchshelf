namespace Fetchshelf.Clients.Mam.Tests;

public class MamSizeParserTests
{
    [Theory]
    [InlineData("512 B", 512L)]
    [InlineData("1 KiB", 1024L)]
    [InlineData("2.0 MiB", 2L * 1024 * 1024)]
    [InlineData("1.5 GiB", 1610612736L)]
    [InlineData("3 TB", 3L * 1024 * 1024 * 1024 * 1024)]
    [InlineData("1,020.5 KiB", 1044992L)]
    [InlineData("1,016.2 KiB", 1040588L)]
    [InlineData("12,345 MiB", 12345L * 1024 * 1024)]
    public void ParseBytes_ValidSize_ReturnsBytes(string text, long expected) =>
        Assert.Equal(expected, MamSizeParser.ParseBytes(text));

    [Theory]
    [InlineData("")]
    [InlineData("big")]
    [InlineData("1.5 parsecs")]
    public void ParseBytes_InvalidSize_ThrowsFormatException(string text) =>
        Assert.Throws<FormatException>(() => MamSizeParser.ParseBytes(text));
}
