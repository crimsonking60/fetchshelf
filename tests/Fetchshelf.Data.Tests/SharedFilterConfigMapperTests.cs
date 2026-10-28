using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Tests;

public class SharedFilterConfigMapperTests
{
    [Fact]
    public void ToTorrentFilter_MapsEveryField()
    {
        var config = new SharedFilterConfig
        {
            MediaType = ["Audiobook"],
            Categories = new CategoriesConfig
            {
                Audio = CategoryListOrBool.Of(["Fantasy"]),
                Musicology = CategoryListOrBool.All,
            },
            Languages = ["English"],
            Flags = new Dictionary<string, bool> { ["explicit"] = true },
            MinSize = "1 MiB",
            MaxSize = "1 GiB",
            ExcludeUploader = ["bad-uploader"],
            UploadedAfter = "2026-01-01",
            UploadedBefore = "2026-12-31",
            MinSeeders = 1,
            MaxSeeders = 100,
            MinLeechers = 0,
            MaxLeechers = 10,
            MinSnatched = 2,
            MaxSnatched = 50,
            MaxAuthors = 3,
        };

        var filter = SharedFilterConfigMapper.ToTorrentFilter(config);

        Assert.Equal([MediaType.Audiobook], filter.MediaTypes);
        Assert.Equal(new HashSet<LegacyCategory> { LegacyCategory.AudiobookFantasy }, filter.Categories.Audio.Allowed);
        Assert.Null(filter.Categories.Musicology.Allowed);
        Assert.Equal([Language.English], filter.Languages);
        Assert.True(filter.Flags![BookFlags.Explicit]);
        Assert.Equal(1024L * 1024, filter.MinSizeBytes);
        Assert.Equal(1024L * 1024 * 1024, filter.MaxSizeBytes);
        Assert.Equal(["bad-uploader"], filter.ExcludeUploader);
        Assert.Equal(new DateOnly(2026, 1, 1), filter.UploadedAfter);
        Assert.Equal(new DateOnly(2026, 12, 31), filter.UploadedBefore);
        Assert.Equal(1, filter.MinSeeders);
        Assert.Equal(100, filter.MaxSeeders);
        Assert.Equal(0, filter.MinLeechers);
        Assert.Equal(10, filter.MaxLeechers);
        Assert.Equal(2, filter.MinSnatched);
        Assert.Equal(50, filter.MaxSnatched);
        Assert.Equal(3, filter.MaxAuthors);
    }
}
