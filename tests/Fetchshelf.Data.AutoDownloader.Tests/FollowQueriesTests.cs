using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public class FollowQueriesTests
{
    private static GrabEntry Block(bool audio = true, bool ebook = true) => new()
    {
        Categories = new CategoriesConfig
        {
            Audio = audio ? CategoryListOrBool.All : CategoryListOrBool.None,
            Ebook = ebook ? CategoryListOrBool.All : CategoryListOrBool.None,
        },
    };

    private static FollowFindEntity Find(string side, FollowFindStatus status) => new()
    {
        FollowId = 1,
        MamId = 1,
        Title = "T",
        MediaSide = side,
        Status = status,
        FoundAt = DateTimeOffset.UnixEpoch,
        StatusAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void WantedSides_AreTheFormatsAnyBlockTakes()
    {
        Assert.Equal(["Ebook"], FollowQueries.WantedSides([Block(audio: false)]).Order());
        Assert.Equal(["Audio", "Ebook"], FollowQueries.WantedSides([Block(audio: false), Block(ebook: false)]).Order());
        Assert.Equal(["Audio"], FollowQueries.WantedSides([new GrabEntry { Categories = new CategoriesConfig { Audio = CategoryListOrBool.Of(["Fantasy"]), Ebook = CategoryListOrBool.None } }]).Order());
    }

    [Theory]
    [InlineData("Audio", FollowFindStatus.New, false)]
    [InlineData("Audio", FollowFindStatus.Wanted, false)]
    [InlineData("Audio", FollowFindStatus.Selected, true)]
    [InlineData("Audio", FollowFindStatus.Owned, true)]
    [InlineData("Ebook", FollowFindStatus.New, true)]
    public void IsListed_HidesOnlyWaitingFindsInAFormatNoBlockTakes(string side, FollowFindStatus status, bool listed) =>
        Assert.Equal(listed, FollowQueries.IsListed(Find(side, status), FollowQueries.WantedSides([Block(audio: false)])));
}
