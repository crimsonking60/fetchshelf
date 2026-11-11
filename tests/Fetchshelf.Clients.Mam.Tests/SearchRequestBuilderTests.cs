using System.Text.Json;

namespace Fetchshelf.Clients.Mam.Tests;

public class SearchRequestBuilderTests
{
    [Fact]
    public void BuildSearchRequestBody_RepresentativeFilterSet_ProducesExpectedParameters()
    {
        var query = new MamSearchQuery
        {
            Text = "dune",
            SearchFields = [MamSearchField.Title, MamSearchField.Author],
            Target = MamSearchTarget.New,
            StartDate = new DateOnly(2024, 1, 15),
            EndDate = new DateOnly(2024, 6, 30),
        };

        var dto = MamClient.BuildSearchRequestBody(query);
        var json = JsonSerializer.SerializeToElement(dto);
        var tor = json.GetProperty("tor");

        Assert.Equal("dune", tor.GetProperty("text").GetString());
        Assert.Equal(["title", "author"], tor.GetProperty("srchIn").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("torrents", tor.GetProperty("searchIn").GetString());
        Assert.Equal("2024-01-15", tor.GetProperty("startDate").GetString());
        Assert.Equal("2024-06-30", tor.GetProperty("endDate").GetString());
    }

    [Fact]
    public void BuildSearchRequestBody_UnsetFields_AreOmitted()
    {
        var query = new MamSearchQuery();

        var dto = MamClient.BuildSearchRequestBody(query);
        var json = JsonSerializer.SerializeToElement(dto, MamClient.SearchRequestSerializerOptions);
        var tor = json.GetProperty("tor");

        Assert.False(tor.TryGetProperty("searchIn", out _));
        Assert.False(tor.TryGetProperty("text", out _));
        Assert.False(tor.TryGetProperty("srchIn", out _));
        Assert.False(tor.TryGetProperty("sortType", out _));
        Assert.False(tor.TryGetProperty("startNumber", out _));
    }

    [Theory]
    [InlineData(MamSortOrder.OldestFirst, "dateAsc")]
    [InlineData(MamSortOrder.NewestByDate, "dateDesc")]
    [InlineData(MamSortOrder.MostSeeders, "seedersDesc")]
    [InlineData(MamSortOrder.MostSnatches, "snatchedDesc")]
    [InlineData(MamSortOrder.LargestFirst, "sizeDesc")]
    [InlineData(MamSortOrder.TitleAsc, "titleAsc")]
    public void BuildSearchRequestBody_SortOrder_SendsItsSortType(MamSortOrder sortOrder, string sortType)
    {
        var dto = MamClient.BuildSearchRequestBody(new MamSearchQuery { SortOrder = sortOrder });
        var tor = JsonSerializer.SerializeToElement(dto, MamClient.SearchRequestSerializerOptions).GetProperty("tor");

        Assert.Equal(sortType, tor.GetProperty("sortType").GetString());
    }

    [Fact]
    public void BuildSearchRequestBody_LookupById_SetsIdOnTor()
    {
        var query = new MamSearchQuery { Id = 12345 };

        var dto = MamClient.BuildSearchRequestBody(query);
        var json = JsonSerializer.SerializeToElement(dto);

        Assert.Equal(12345, json.GetProperty("tor").GetProperty("id").GetInt64());
    }

    [Fact]
    public void BuildSearchRequestBody_LookupByHash_SetsHashOnTor()
    {
        var query = new MamSearchQuery { Hash = "abc123" };

        var dto = MamClient.BuildSearchRequestBody(query);
        var json = JsonSerializer.SerializeToElement(dto);

        Assert.Equal("abc123", json.GetProperty("tor").GetProperty("hash").GetString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void BuildSearchRequestBody_ThumbnailAndMediaInfo_AreOnlyAskedForWhenWanted(bool thumbnail, bool mediaInfo)
    {
        var dto = MamClient.BuildSearchRequestBody(new MamSearchQuery { Id = 1, ReturnThumbnail = thumbnail, ReturnMediaInfo = mediaInfo });
        var json = JsonSerializer.SerializeToElement(dto);

        Assert.Equal(thumbnail, json.GetProperty("thumbnail").GetBoolean());
        Assert.Equal(mediaInfo, json.GetProperty("mediaInfo").GetBoolean());
    }
}
