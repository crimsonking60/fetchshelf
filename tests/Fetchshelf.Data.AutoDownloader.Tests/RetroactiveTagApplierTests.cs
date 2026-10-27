using System.Net;
using System.Net.Http.Json;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public sealed class RetroactiveTagApplierTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-retroactive-tag-applier-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static TorrentMetadata BuildMetadata(long mamId = 1) =>
        MamTorrentMapper.Map(SelectionTestSupport.BuildRecord(mamId: mamId));

    private static async Task<TorrentEntity> SeedTorrentAsync(FetchshelfDbContext context, string id, TorrentMetadata metadata)
    {
        var entity = new TorrentEntity
        {
            Id = id,
            MamId = metadata.MamId,
            NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
            Title = metadata.Title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        context.Torrents.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    private static MamClient CreateMamClient(Func<HttpRequestMessage, HttpResponseMessage?> searchRouter, List<int> searchCallCounter)
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("loadSearchJSONbasic.php"))
            {
                searchCallCounter.Add(1);
                return searchRouter(request) ?? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"perpage":100,"start":0,"total":0,"found":0,"data":[]}"""),
                };
            }

            if (path.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(DownloadTestSupport.DefaultAccountStateJson) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });

        return new MamClient(new HttpClient(handler), new MamClientOptions { MamId = "cookie" });
    }

    private static string SearchResultJson(long mamId, long seeders) => $$"""
        {
            "perpage": 100, "start": 0, "total": 1, "found": 1,
            "data": [
                {
                    "id": {{mamId}}, "title": "T", "author_info": "{}",
                    "narrator_info": "{}", "series_info": "{}", "isbn": null, "description": null,
                    "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[13]",
                    "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1, "size": "100 MiB",
                    "tags": "", "owner": 1, "owner_name": "live-uploader", "added": "2026-01-01 00:00:00",
                    "seeders": {{seeders}}, "leechers": 1, "times_completed": 1,
                    "free": false, "personal_freeleech": false, "fl_vip": false, "vip": false,
                    "vip_expire": 0, "dl": "dl"
                }
            ]
        }
        """;

    private static QBittorrentClient CreateQBittorrentClient(
        string? knownHash, List<(string Hash, string Category)> categoryCalls, List<(string Hash, string Tags)> tagCalls)
    {
        var options = new QBittorrentClientOptions("http://localhost:8080", "admin", "adminadmin");
        var handler = new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("auth/login"))
            {
                var loginResponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                loginResponse.Headers.Add("Set-Cookie", "SID=abc123; path=/; HttpOnly");
                return loginResponse;
            }

            if (path.Contains("torrents/info"))
            {
                var torrents = knownHash is not null
                    ? new List<QBittorrentTorrent> { new() { Hash = knownHash, Name = "Book", SavePath = "/downloads", State = "uploading" } }
                    : [];
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(torrents) };
            }

            if (path.Contains("setCategory"))
            {
                var form = ParseForm(request);
                categoryCalls.Add((form["hashes"], form["category"]));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
            }

            if (path.Contains("addTags"))
            {
                var form = ParseForm(request);
                tagCalls.Add((form["hashes"], form["tags"]));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
        });

        return new QBittorrentClient(new HttpClient(handler), options);
    }

    private static Dictionary<string, string> ParseForm(HttpRequestMessage request)
    {
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        var result = new Dictionary<string, string>();
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            result[Uri.UnescapeDataString(parts[0])] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
        }

        return result;
    }

    private static TagRule MatchAllRule(string? category = "audiobooks-tagged", IReadOnlyList<string>? tags = null) => new()
    {
        Filter = new TorrentFilter(),
        Category = category,
        Tags = tags ?? ["retro"],
    };

    [Fact]
    public async Task ApplyAsync_MetadataOnlyRecord_IsNeverEvaluatedOrModified()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var metadata = BuildMetadata();
        await SeedTorrentAsync(context, MetadataOnlyTorrentId.For(metadata.MamId), metadata);

        var searchCalls = new List<int>();
        var mamClient = CreateMamClient(_ => null, searchCalls);
        var categoryCalls = new List<(string, string)>();
        var tagCalls = new List<(string, string)>();
        var qbClient = CreateQBittorrentClient(knownHash: null, categoryCalls, tagCalls);

        // Even a filter that would require a live MaM fetch must never be evaluated for a metadata-only record.
        var rule = MatchAllRule() with { Filter = new TorrentFilter { MinSeeders = 1 } };
        var applier = new RetroactiveTagApplier(context, mamClient, [qbClient]);

        await applier.ApplyAsync(rule);

        Assert.Empty(searchCalls);
        Assert.Empty(categoryCalls);
        Assert.Empty(tagCalls);
    }

    [Fact]
    public async Task ApplyAsync_FilterNeedsOnlyStoredFields_MatchesWithoutAnyMamCall()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var metadata = BuildMetadata();
        await SeedTorrentAsync(context, "hash1", metadata);

        var searchCalls = new List<int>();
        var mamClient = CreateMamClient(_ => throw new InvalidOperationException("Should not be called"), searchCalls);
        var categoryCalls = new List<(string, string)>();
        var tagCalls = new List<(string, string)>();
        var qbClient = CreateQBittorrentClient("hash1", categoryCalls, tagCalls);

        var rule = MatchAllRule();
        var applier = new RetroactiveTagApplier(context, mamClient, [qbClient]);

        await applier.ApplyAsync(rule);

        Assert.Empty(searchCalls);
        Assert.Single(categoryCalls);
        Assert.Single(tagCalls);
    }

    [Fact]
    public async Task ApplyAsync_FilterSetsMinSeeders_TriggersExactlyOneMamCallAndMatchesOnFetchedValue()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var metadata = BuildMetadata(mamId: 42);
        await SeedTorrentAsync(context, "hash1", metadata);

        var searchCalls = new List<int>();
        var mamClient = CreateMamClient(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SearchResultJson(42, seeders: 20)) },
            searchCalls);
        var categoryCalls = new List<(string, string)>();
        var tagCalls = new List<(string, string)>();
        var qbClient = CreateQBittorrentClient("hash1", categoryCalls, tagCalls);

        var rule = MatchAllRule() with { Filter = new TorrentFilter { MinSeeders = 10 } };
        var applier = new RetroactiveTagApplier(context, mamClient, [qbClient]);

        await applier.ApplyAsync(rule);

        Assert.Single(searchCalls);
        Assert.Single(categoryCalls);
        Assert.Single(tagCalls);
    }

    [Fact]
    public async Task ApplyAsync_FilterSetsMinSeeders_RejectsWhenFetchedValueBelowBound()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var metadata = BuildMetadata(mamId: 42);
        await SeedTorrentAsync(context, "hash1", metadata);

        var searchCalls = new List<int>();
        var mamClient = CreateMamClient(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SearchResultJson(42, seeders: 2)) },
            searchCalls);
        var categoryCalls = new List<(string, string)>();
        var tagCalls = new List<(string, string)>();
        var qbClient = CreateQBittorrentClient("hash1", categoryCalls, tagCalls);

        var rule = MatchAllRule() with { Filter = new TorrentFilter { MinSeeders = 10 } };
        var applier = new RetroactiveTagApplier(context, mamClient, [qbClient]);

        await applier.ApplyAsync(rule);

        Assert.Single(searchCalls);
        Assert.Empty(categoryCalls);
        Assert.Empty(tagCalls);
    }

    [Fact]
    public async Task ApplyAsync_RuleWithCategoryAndTags_UpdatesBoth()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var metadata = BuildMetadata();
        await SeedTorrentAsync(context, "hash1", metadata);

        var searchCalls = new List<int>();
        var mamClient = CreateMamClient(_ => null, searchCalls);
        List<(string Hash, string Category)> categoryCalls = [];
        List<(string Hash, string Tags)> tagCalls = [];
        var qbClient = CreateQBittorrentClient("hash1", categoryCalls, tagCalls);

        var rule = MatchAllRule(category: "audiobooks-tagged", tags: ["retro", "applied"]);
        var applier = new RetroactiveTagApplier(context, mamClient, [qbClient]);

        await applier.ApplyAsync(rule);

        var categoryCall = Assert.Single(categoryCalls);
        Assert.Equal("hash1", categoryCall.Hash);
        Assert.Equal("audiobooks-tagged", categoryCall.Category);
        var tagCall = Assert.Single(tagCalls);
        Assert.Equal("hash1", tagCall.Hash);
        Assert.Equal("retro,applied", tagCall.Tags);
    }

    [Fact]
    public async Task ApplyAsync_RuleWithOnlyTags_LeavesQBittorrentCategoryUntouched()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var metadata = BuildMetadata();
        await SeedTorrentAsync(context, "hash1", metadata);

        var searchCalls = new List<int>();
        var mamClient = CreateMamClient(_ => null, searchCalls);
        var categoryCalls = new List<(string, string)>();
        var tagCalls = new List<(string, string)>();
        var qbClient = CreateQBittorrentClient("hash1", categoryCalls, tagCalls);

        var rule = MatchAllRule(category: null, tags: ["retro"]);
        var applier = new RetroactiveTagApplier(context, mamClient, [qbClient]);

        await applier.ApplyAsync(rule);

        Assert.Empty(categoryCalls);
        Assert.Single(tagCalls);
    }

    [Fact]
    public async Task ApplyAsync_OnlyOwningConnectionIsCalled()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var metadata = BuildMetadata();
        await SeedTorrentAsync(context, "hash1", metadata);

        var searchCalls = new List<int>();
        var mamClient = CreateMamClient(_ => null, searchCalls);
        var firstCategoryCalls = new List<(string, string)>();
        var firstTagCalls = new List<(string, string)>();
        var firstClient = CreateQBittorrentClient(knownHash: null, firstCategoryCalls, firstTagCalls);
        var secondCategoryCalls = new List<(string, string)>();
        var secondTagCalls = new List<(string, string)>();
        var secondClient = CreateQBittorrentClient(knownHash: "hash1", secondCategoryCalls, secondTagCalls);

        var rule = MatchAllRule();
        var applier = new RetroactiveTagApplier(context, mamClient, [firstClient, secondClient]);

        await applier.ApplyAsync(rule);

        Assert.Empty(firstCategoryCalls);
        Assert.Empty(firstTagCalls);
        Assert.Single(secondCategoryCalls);
        Assert.Single(secondTagCalls);
    }
}
