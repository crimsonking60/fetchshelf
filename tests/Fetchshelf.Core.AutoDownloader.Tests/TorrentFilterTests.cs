namespace Fetchshelf.Core.AutoDownloader.Tests;

public class TorrentFilterTests
{
    [Fact]
    public void NeedsLiveData_OnlyStoredMetadataFields_ReturnsFalse() =>
        Assert.False(new TorrentFilter { MinSizeBytes = 1, MaxAuthors = 3, ExcludeUploader = [] }.NeedsLiveData);

    public static TheoryData<TorrentFilter> LiveDataFilters =>
    [
        new TorrentFilter { ExcludeUploader = ["someone"] },
        new TorrentFilter { MinSeeders = 1 },
        new TorrentFilter { MaxSeeders = 1 },
        new TorrentFilter { MinLeechers = 1 },
        new TorrentFilter { MaxLeechers = 1 },
        new TorrentFilter { MinSnatched = 1 },
        new TorrentFilter { MaxSnatched = 1 },
    ];

    [Theory]
    [MemberData(nameof(LiveDataFilters))]
    public void NeedsLiveData_UploaderSeedersLeechersOrSnatches_ReturnsTrue(TorrentFilter filter) =>
        Assert.True(filter.NeedsLiveData);
}
