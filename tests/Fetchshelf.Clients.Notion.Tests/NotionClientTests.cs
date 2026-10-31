using System.Net;
using System.Text;

namespace Fetchshelf.Clients.Notion.Tests;

public sealed class NotionClientTests
{
    private const string SampleResponse = """
        {
          "results": [
            {
              "id": "page-1",
              "properties": {
                "MAM": { "id": "abc", "type": "url", "url": "https://www.myanonamouse.net/t/123456" },
                "Name": { "id": "title", "type": "title", "title": [] },
                "Status": { "id": "status", "type": "status", "status": { "name": "Reading" } },
                "Tags": { "id": "tags", "type": "multi_select", "multi_select": [] },
                "Due": { "id": "due", "type": "date", "date": null },
                "Priority": { "id": "priority", "type": "number", "number": 1 },
                "Related": { "id": "related", "type": "relation", "relation": [] },
                "Category": { "id": "category", "type": "select", "select": null },
                "Notes": { "id": "notes", "type": "rich_text", "rich_text": [] },
                "Attachments": { "id": "files", "type": "files", "files": [] }
              }
            }
          ],
          "next_cursor": null,
          "has_more": false
        }
        """;

    [Fact]
    public async Task QueryAllRowsAsync_DeserializesEveryPropertyTypeWithoutError()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleResponse, Encoding.UTF8, "application/json"),
        });
        var client = new NotionClient(new HttpClient(handler));

        var rows = await client.QueryAllRowsAsync("data-source-id", "secret-token");

        var row = Assert.Single(rows);
        Assert.Equal("page-1", row.Id);
        var mamProperty = Assert.IsType<NotionUrlProperty>(row.Properties["MAM"]);
        Assert.Equal("https://www.myanonamouse.net/t/123456", mamProperty.Url);
        Assert.IsType<NotionTitleProperty>(row.Properties["Name"]);
        Assert.IsType<NotionStatusProperty>(row.Properties["Status"]);
        Assert.IsType<NotionMultiSelectProperty>(row.Properties["Tags"]);
        Assert.IsType<NotionDateProperty>(row.Properties["Due"]);
        Assert.IsType<NotionNumberProperty>(row.Properties["Priority"]);
        Assert.IsType<NotionRelationProperty>(row.Properties["Related"]);
        Assert.IsType<NotionSelectProperty>(row.Properties["Category"]);
        Assert.IsType<NotionRichTextProperty>(row.Properties["Notes"]);
        Assert.IsType<NotionFilesProperty>(row.Properties["Attachments"]);
    }

    [Fact]
    public async Task QueryAllRowsAsync_SendsBearerTokenAndNotionVersionHeader()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"results":[]}""", Encoding.UTF8, "application/json"),
            };
        });
        var client = new NotionClient(new HttpClient(handler));

        await client.QueryAllRowsAsync("data-source-id", "secret-token");

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Contains("data-source-id", capturedRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
        Assert.Equal("secret-token", capturedRequest.Headers.Authorization?.Parameter);
        Assert.Equal("2025-09-03", capturedRequest.Headers.GetValues("Notion-Version").Single());
    }

    [Fact]
    public async Task QueryAllRowsAsync_MoreThanOnePage_FollowsTheCursorUntilTheLastPage()
    {
        var requestBodies = new List<string>();
        var handler = new FakeHttpMessageHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            requestBodies.Add(body);
            var json = requestBodies.Count switch
            {
                1 => """{"results":[{"id":"row-1","properties":{}},{"id":"row-2","properties":{}}],"next_cursor":"cursor-a","has_more":true}""",
                2 => """{"results":[{"id":"row-3","properties":{}}],"next_cursor":"cursor-b","has_more":true}""",
                _ => """{"results":[{"id":"row-4","properties":{}}],"next_cursor":null,"has_more":false}""",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });
        var client = new NotionClient(new HttpClient(handler));

        var rows = await client.QueryAllRowsAsync("data-source-id", "secret-token");

        Assert.Equal(["row-1", "row-2", "row-3", "row-4"], rows.Select(r => r.Id));
        Assert.Equal(3, requestBodies.Count);
        Assert.Equal("""{"page_size":100}""", requestBodies[0]);
        Assert.Equal("""{"page_size":100,"start_cursor":"cursor-a"}""", requestBodies[1]);
        Assert.Equal("""{"page_size":100,"start_cursor":"cursor-b"}""", requestBodies[2]);
    }

    [Fact]
    public async Task QueryAllRowsAsync_HasMoreWithoutACursor_StopsInsteadOfLooping()
    {
        var requests = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"results":[{"id":"row-1","properties":{}}],"next_cursor":null,"has_more":true}""", Encoding.UTF8, "application/json"),
            };
        });
        var client = new NotionClient(new HttpClient(handler));

        var rows = await client.QueryAllRowsAsync("data-source-id", "secret-token");

        Assert.Single(rows);
        Assert.Equal(1, requests);
    }
}
