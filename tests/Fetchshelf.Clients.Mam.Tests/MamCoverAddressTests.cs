namespace Fetchshelf.Clients.Mam.Tests;

public class MamCoverAddressTests
{
    [Theory]
    [InlineData("https://cdn.example/a.jpg", "https://cdn.example/a.jpg")]
    [InlineData("/covers/a.jpg", "https://mam.example/covers/a.jpg")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("a.jpg", null)]
    [InlineData("", null)]
    public void ThumbnailUrl_OnlyWebAddresses(string thumbnail, string? expected) =>
        Assert.Equal(expected, MamCoverAddress.ThumbnailUrl(thumbnail, "https://mam.example/"));

    [Theory]
    [InlineData("image/jpeg", "https://cdn.myanonamouse.net/t/p/1793943776/large/1281386.jpeg")]
    [InlineData("image/png", "https://cdn.myanonamouse.net/t/p/1793943776/large/1281386.png")]
    [InlineData(null, null)]
    [InlineData("application/pdf", null)]
    public void CoverUrl_FollowsMamsCdnLayout(string? posterType, string? expected) =>
        Assert.Equal(expected, MamCoverAddress.CoverUrl(
            1281386, posterType, new DateTimeOffset(2026, 11, 6, 5, 42, 56, TimeSpan.Zero), "https://cdn.myanonamouse.net/"));
}
