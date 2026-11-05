using System.Text.Json;

namespace Fetchshelf.Clients.Mam.Tests;

public class SearchResponseParserTests
{
    // Representative response shape, including the nested-JSON-string-encoded author_info/
    // narrator_info/series_info/categories fields.
    private const string ResponseJson = """
        {
            "perpage": 100,
            "start": 0,
            "total": 1,
            "found": 1,
            "data": [
                {
                    "id": 123456,
                    "title": "Dune",
                    "author_info": "{\"111\":\"Frank Herbert\"}",
                    "narrator_info": "{\"222\":\"Simon Vance\"}",
                    "series_info": "{\"333\":[\"Dune\",\"1\"]}",
                    "isbn": "9780441013593",
                    "description": "A desert planet.",
                    "mediatype": 1,
                    "main_cat": 13,
                    "maincat": 1,
                    "category": 39,
                    "categories": "[13]",
                    "language": 1,
                    "browseflags": 0,
                    "filetype": "m4b",
                    "numfiles": 12,
                    "size": "1.23 GiB",
                    "tags": "sci-fi, classic",
                    "owner": 999,
                    "owner_name": "uploader1",
                    "added": "2020-05-01 12:34:56",
                    "seeders": 10,
                    "leechers": 2,
                    "times_completed": 50,
                    "free": true,
                    "personal_freeleech": false,
                    "fl_vip": false,
                    "vip": false,
                    "vip_expire": 0,
                    "dl": "abcdef0123456789"
                }
            ]
        }
        """;

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Parse_RepresentativeResponse_ExtractsAllDocumentedFields()
    {
        var result = SearchResponseParser.Parse(Parse(ResponseJson));

        Assert.Equal(100, result.PerPage);
        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Found);
        var record = Assert.Single(result.Data);

        Assert.Equal(123456, record.MamId);
        Assert.Equal("Dune", record.Title);
        Assert.Equal("Frank Herbert", record.Authors[111]);
        Assert.Equal("Simon Vance", record.Narrators[222]);
        var series = Assert.Single(record.Series);
        Assert.Equal("Dune", series.Name);
        Assert.Equal("1", series.Number);
        Assert.Equal("9780441013593", record.Isbn);
        Assert.Equal("A desert planet.", record.Description);
        Assert.Equal(1, record.MediaTypeId);
        Assert.Equal(13, record.OldMainCategoryId);
        Assert.Equal(1, record.FictionCategoryId);
        Assert.Equal(39, record.LegacyCategoryId);
        Assert.Equal([13], record.GenreCategoryIds);
        Assert.Equal(1, record.LanguageId);
        Assert.Equal(0, record.FlagsRaw);
        Assert.Equal("m4b", record.FileTypesRaw);
        Assert.Equal(12, record.FileCount);
        Assert.Equal("1.23 GiB", record.SizeRaw);
        Assert.Equal("sci-fi, classic", record.TagsRaw);
        Assert.Equal(999, record.UploaderId);
        Assert.Equal("uploader1", record.UploaderName);
        Assert.Equal(new DateTimeOffset(2020, 5, 1, 12, 34, 56, TimeSpan.Zero), record.UploadedAt);
        Assert.Equal(10, record.Seeders);
        Assert.Equal(2, record.Leechers);
        Assert.Equal(50, record.TimesCompleted);
        Assert.True(record.IsFree);
        Assert.False(record.IsPersonalFreeleech);
        Assert.False(record.IsFreeleechVip);
        Assert.False(record.IsVip);
        Assert.Equal(0, record.VipExpireUnixSeconds);
        Assert.Equal("abcdef0123456789", record.DownloadHash);
    }

    [Fact]
    public void Parse_EmptyResultErrorShape_ReturnsNoResultsWithoutThrowing()
    {
        var result = SearchResponseParser.Parse(Parse("""{"error": "Nothing returned, out of 0"}"""));

        Assert.Empty(result.Data);
        Assert.Equal(0, result.Found);
    }

    [Fact]
    public void Parse_ThumbnailMediaInfoAndEveryRawField_AreKept()
    {
        var json = ResponseJson.Replace(
            "\"dl\": \"abcdef0123456789\"",
            "\"dl\": \"abcdef0123456789\", \"thumbnail\": \"https://cdn.example/t.jpg\", \"lang_code\": \"ENG\", "
            + "\"mediainfo\": \"{\\\"General\\\":{\\\"Duration\\\":\\\"12h 3m\\\"},\\\"Audio1\\\":{\\\"BitRate\\\":\\\"64 kb/s\\\"}}\"",
            StringComparison.Ordinal);

        var record = Assert.Single(SearchResponseParser.Parse(Parse(json)).Data);

        Assert.Equal("https://cdn.example/t.jpg", record.Thumbnail);
        using var mediaInfo = JsonDocument.Parse(record.MediaInfoJson!);
        Assert.Equal("12h 3m", mediaInfo.RootElement.GetProperty("General").GetProperty("Duration").GetString());
        using var raw = JsonDocument.Parse(record.RawJson);
        Assert.Equal("ENG", raw.RootElement.GetProperty("lang_code").GetString());
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"not json\"")]
    [InlineData("null")]
    public void Parse_MediaInfoMissingOrUnreadable_IsNull(string mediaInfo)
    {
        var json = ResponseJson.Replace("\"dl\": \"abcdef0123456789\"", $"\"dl\": \"abcdef0123456789\", \"mediainfo\": {mediaInfo}", StringComparison.Ordinal);

        Assert.Null(Assert.Single(SearchResponseParser.Parse(Parse(json)).Data).MediaInfoJson);
    }
}
