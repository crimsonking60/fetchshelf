using System.Text;

namespace Fetchshelf.Core.AutoDownloader.Tests;

public class TorrentFileInfoHashTests
{
    [Fact]
    public void Compute_KnownBencodedFile_MatchesExpectedSha1()
    {
        var bytes = Encoding.ASCII.GetBytes("d8:announce10:tracker://4:infod6:lengthi10e4:name4:testee");

        var hash = TorrentFileInfoHash.Compute(bytes);

        Assert.Equal("e17c544bb145b9ec529225aeb904f53b22eedbf0", hash);
    }

    [Fact]
    public void Compute_InfoKeyNotFirst_StillFound()
    {
        var bytes = Encoding.ASCII.GetBytes("d4:infod6:lengthi10e4:name4:teste8:announce10:tracker://e");

        var hash = TorrentFileInfoHash.Compute(bytes);

        Assert.Equal("e17c544bb145b9ec529225aeb904f53b22eedbf0", hash);
    }

    [Fact]
    public void Compute_MissingInfoKey_Throws()
    {
        var bytes = Encoding.ASCII.GetBytes("d8:announce10:tracker://e");

        Assert.Throws<FormatException>(() => TorrentFileInfoHash.Compute(bytes));
    }

    [Fact]
    public void TryCompute_TorrentFile_GivesTheSameHashAsCompute()
    {
        var bytes = Encoding.ASCII.GetBytes("d8:announce10:tracker://4:infod6:lengthi10e4:name4:testee");

        Assert.True(TorrentFileInfoHash.TryCompute(bytes, out var hash));
        Assert.Equal("e17c544bb145b9ec529225aeb904f53b22eedbf0", hash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>Torrent not found</html>")]
    [InlineData("d8:announce")]
    [InlineData("d8:announce10:tracker://e")]
    [InlineData("d4:infod6:lengthi10e")]
    [InlineData("d99999999999:x")]
    public void TryCompute_NotATorrentFile_IsFalseRatherThanThrowing(string content)
    {
        Assert.False(TorrentFileInfoHash.TryCompute(Encoding.ASCII.GetBytes(content), out _));
    }
}
