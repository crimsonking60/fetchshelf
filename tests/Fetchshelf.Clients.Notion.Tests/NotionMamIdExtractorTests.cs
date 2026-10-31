namespace Fetchshelf.Clients.Notion.Tests;

public class NotionMamIdExtractorTests
{
    private static NotionRow BuildRow(IReadOnlyDictionary<string, NotionProperty> properties) =>
        new() { Id = "row-1", Properties = properties };

    [Fact]
    public void ExtractMamIds_ValidUrlProperty_ReturnsParsedId()
    {
        var row = BuildRow(new Dictionary<string, NotionProperty>
        {
            ["MAM"] = new NotionUrlProperty { Url = "https://www.myanonamouse.net/t/123456" },
        });

        var ids = NotionMamIdExtractor.ExtractMamIds(row, ["MAM"]);

        Assert.Equal([123456L], ids);
    }

    [Fact]
    public void ExtractMamIds_NonUrlPropertyType_IsIgnored()
    {
        var row = BuildRow(new Dictionary<string, NotionProperty>
        {
            ["MAM"] = new NotionTitleProperty(),
        });

        var ids = NotionMamIdExtractor.ExtractMamIds(row, ["MAM"]);

        Assert.Empty(ids);
    }

    [Fact]
    public void ExtractMamIds_UnparseableTrailingSegment_IsIgnored()
    {
        var row = BuildRow(new Dictionary<string, NotionProperty>
        {
            ["MAM"] = new NotionUrlProperty { Url = "https://www.myanonamouse.net/t/not-a-number" },
        });

        var ids = NotionMamIdExtractor.ExtractMamIds(row, ["MAM"]);

        Assert.Empty(ids);
    }

    [Fact]
    public void ExtractMamIds_FieldNotPresentOnRow_IsIgnored()
    {
        var row = BuildRow(new Dictionary<string, NotionProperty>());

        var ids = NotionMamIdExtractor.ExtractMamIds(row, ["MAM"]);

        Assert.Empty(ids);
    }

    [Fact]
    public void ExtractMamIds_MultipleConfiguredFields_ReturnsEachValidId()
    {
        var row = BuildRow(new Dictionary<string, NotionProperty>
        {
            ["MAM"] = new NotionUrlProperty { Url = "https://www.myanonamouse.net/t/111" },
            ["Alt"] = new NotionUrlProperty { Url = "https://www.myanonamouse.net/t/222" },
        });

        var ids = NotionMamIdExtractor.ExtractMamIds(row, ["MAM", "Alt"]);

        Assert.Equal([111L, 222L], ids);
    }
}
