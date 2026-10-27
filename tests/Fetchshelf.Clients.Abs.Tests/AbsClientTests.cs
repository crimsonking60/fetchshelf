using System.Net;
using System.Net.Http.Json;
using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Clients.Abs.Tests;

public sealed class AbsClientTests
{
    private const string Token = "secret-token";

    private static AbsClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(new HttpClient(new FakeHttpMessageHandler(handler)), new AbsClientOptions { BaseUrl = "http://abs.local", Token = Token });

    private static TorrentMetadata BuildMetadata() => new()
    {
        MamId = 1,
        VipStatus = VipStatus.NotVip,
        MediaType = MediaType.Audiobook,
        LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
        Language = Language.English,
        Title = "The Way of Kings: Book One",
        Authors = ["Brandon Sanderson"],
        Narrators = ["Michael Kramer"],
        Series = [new Series { Name = "The Stormlight Archive", Entries = [SeriesEntry.ForNumber(1, "1")] }],
        MetadataSource = MetadataSource.Mam,
        UploadTime = DateTimeOffset.UtcNow,
        Description = "A great book.",
        Isbn = "1234567890",
    };

    [Fact]
    public async Task ListLibrariesAsync_ParsesLibrariesAndFolders_AndSendsBearerToken()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = CreateClient(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    libraries = new[]
                    {
                        new { id = "lib1", name = "Audiobooks", folders = new[] { new { id = "f1", fullPath = "/data/audiobooks" } } },
                    },
                }),
            };
        });

        var libraries = await client.ListLibrariesAsync();

        var library = Assert.Single(libraries);
        Assert.Equal("lib1", library.Id);
        Assert.Equal("Audiobooks", library.Name);
        var folder = Assert.Single(library.Folders);
        Assert.Equal("/data/audiobooks", folder.FullPath);
        Assert.Equal("Bearer", capturedRequest!.Headers.Authorization!.Scheme);
        Assert.Equal(Token, capturedRequest.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task SearchLibraryAsync_SendsLibraryIdAndQueryParam_AndParsesAuthors()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = CreateClient(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { authors = new[] { new { id = "a1", name = "Brandon Sanderson" } } }),
            };
        });

        var authors = await client.SearchLibraryAsync("lib1", "Brandon Sanderson");

        var author = Assert.Single(authors);
        Assert.Equal("a1", author.Id);
        Assert.Equal("Brandon Sanderson", author.Name);
        Assert.Equal("/api/libraries/lib1/search", capturedRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("?q=Brandon%20Sanderson", capturedRequest.RequestUri.Query);
    }

    [Fact]
    public async Task GetAuthorItemsAsync_SendsIncludeItems_AndParsesLibraryItems()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = CreateClient(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    id = "a1",
                    libraryItems = new[] { new { id = "item1", path = "/data/audiobooks/Sanderson/Book" } },
                }),
            };
        });

        var items = await client.GetAuthorItemsAsync("a1");

        var item = Assert.Single(items);
        Assert.Equal("item1", item.Id);
        Assert.Equal("/data/audiobooks/Sanderson/Book", item.Path);
        Assert.Equal("/api/authors/a1", capturedRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("?include=items", capturedRequest.RequestUri.Query);
    }

    [Fact]
    public async Task UpdateItemMetadataAsync_SendsPatchWithSplitTitleAuthorsSeriesGenres()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var client = CreateClient(request =>
        {
            capturedRequest = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await client.UpdateItemMetadataAsync("item1", BuildMetadata());

        Assert.Equal(HttpMethod.Patch, capturedRequest!.Method);
        Assert.Equal("/api/items/item1/media", capturedRequest.RequestUri!.AbsolutePath);
        var payload = System.Text.Json.JsonDocument.Parse(capturedBody!).RootElement.GetProperty("metadata");
        Assert.Equal("The Way of Kings", payload.GetProperty("title").GetString());
        Assert.Equal("Book One", payload.GetProperty("subtitle").GetString());
        Assert.Equal("Brandon Sanderson", payload.GetProperty("authors")[0].GetProperty("name").GetString());
        Assert.Equal("Michael Kramer", payload.GetProperty("narrators")[0].GetString());
        Assert.Equal("The Stormlight Archive", payload.GetProperty("series")[0].GetProperty("name").GetString());
        Assert.Equal("1", payload.GetProperty("series")[0].GetProperty("sequence").GetString());
        Assert.Equal("A great book.", payload.GetProperty("description").GetString());
        Assert.Equal("1234567890", payload.GetProperty("isbn").GetString());
        Assert.Contains("General Fiction", payload.GetProperty("genres").EnumerateArray().Select(g => g.GetString()));
    }

    [Fact]
    public async Task DeleteItemAsync_TargetsGivenItemId()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = CreateClient(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await client.DeleteItemAsync("item1");

        Assert.Equal(HttpMethod.Delete, capturedRequest!.Method);
        Assert.Equal("/api/items/item1", capturedRequest.RequestUri!.AbsolutePath);
    }
}
