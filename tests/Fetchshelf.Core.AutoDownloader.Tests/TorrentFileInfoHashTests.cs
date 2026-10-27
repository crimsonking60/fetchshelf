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
}
