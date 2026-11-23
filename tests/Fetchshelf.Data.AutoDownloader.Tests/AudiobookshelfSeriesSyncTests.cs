using System.Net;
using Fetchshelf.Clients.Abs;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader.Tests;

/// <summary>`[follow] audiobookshelf_series`: the series in Audiobookshelf followed, and their books counted as owned.</summary>
public sealed class AudiobookshelfSeriesSyncTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-abs-series-sync-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A book of a series: its id, title, <c>seriesName</c>, and whether it has audio and an ebook.</summary>
    private sealed record Book(string Id, string Title, string? SeriesName, bool Audio = true, bool Ebook = false);

    /// <summary>What a fake Audiobookshelf holds: libraries by name, each with its series.</summary>
    private sealed class FakeAbs
    {
        public Dictionary<string, Dictionary<string, Book[]>> Libraries { get; } = new()
        {
            ["Audiobooks"] = new()
            {
                ["The Expanse"] = [new("e1", "Leviathan Wakes", "The Expanse #1"), new("e2", "Caliban's War", "The Expanse #2")],
                ["Earthseed #1, 2"] = [new("s1", "Parable of the Sower", "Earthseed #1, 2")],
            },
        };

        public bool Fail { get; set; }

        public int Calls { get; private set; }

        public HttpResponseMessage Answer(HttpRequestMessage request)
        {
            Calls++;
            if (Fail)
            {
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
            }

            var path = request.RequestUri!.AbsolutePath;
            var names = Libraries.Keys.ToList();
            if (path == "/api/libraries")
            {
                var libraries = names.Select((name, i) => $$"""{"id":"lib{{i}}","name":"{{name}}","mediaType":"book","folders":[]}""")
                    .Append("""{"id":"pod","name":"Podcasts","mediaType":"podcast","folders":[]}""");
                return Json($$"""{"libraries":[{{string.Join(",", libraries)}}]}""");
            }

            var library = Libraries[names[int.Parse(path.Split('/')[3][3..], System.Globalization.CultureInfo.InvariantCulture)]];
            var series = library.Select(s => $$"""
                {"id":"ser_{{s.Key.Length}}","name":"{{s.Key}}","books":[{{string.Join(",", s.Value.Select(BookJson))}}]}
                """);
            return Json($$"""{"results":[{{string.Join(",", series)}}],"total":{{library.Count}}}""");
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

        private static string BookJson(Book book)
        {
            var ebook = book.Ebook ? ",\"ebookFormat\":\"epub\"" : string.Empty;
            var metadata = $$"""{"title":"{{book.Title}}","seriesName":"{{book.SeriesName}}"}""";
            return $$$"""{"id":"{{{book.Id}}}","media":{"numTracks":{{{(book.Audio ? 1 : 0)}}}{{{ebook}}},"metadata":{{{metadata}}}}}""";
        }
    }

    private sealed class Harness(FetchshelfDbContext context, FakeAbs abs, List<string> searches, Func<FollowSettings, FollowRunner> create)
    {
        public FetchshelfDbContext Context => context;

        public FakeAbs Abs => abs;

        public List<string> Searches => searches;

        public Task<FollowRunSummary> RunAsync(FollowSettings settings, bool? dryRun = null) =>
            create(settings).RunAsync(ignoreTorrents: [], dryRun: dryRun);
    }

    private Harness CreateHarness(Func<string, string>? searchResults = null)
    {
        var context = SelectionTestSupport.CreateContext(DbPath);
        var abs = new FakeAbs();
        var searches = new List<string>();
        var mamHandler = new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"ratio":5.0,"uploaded_bytes":1000,"downloaded_bytes":100,"wedges":10,"uid":1,"snatch_summary":{"unsat":{"count":0,"limit":20}}}"""),
                };
            }

            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            searches.Add(body);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(searchResults?.Invoke(body) ?? """{"perpage":100,"start":0,"total":0,"found":0,"data":[]}"""),
            };
        });
        var mamClient = new MamClient(new HttpClient(mamHandler), new MamClientOptions { MamId = "cookie" }, delay: (_, _) => Task.CompletedTask);
        var absClient = new AbsClient(new HttpClient(new FakeHttpMessageHandler(abs.Answer)), new AbsClientOptions { BaseUrl = "http://abs.local", Token = "t" });
        return new Harness(context, abs, searches, settings => new FollowRunner(
            context, mamClient, new MamAccessGate(),
            new SelectionRunner(context, new EventStore(context), [], SelectionTestSupport.PreferenceLists),
            new LibraryQueries(context), settings, SelectionTestSupport.PreferenceLists, new Clock(),
            delay: (_, _) => Task.CompletedTask,
            audiobookshelfSeries: new AudiobookshelfSeriesSync(context, absClient, settings, new Clock())));
    }

    private static FollowSettings Settings(AudiobookshelfSeriesMode mode = AudiobookshelfSeriesMode.Follow, params string[] libraries) => new()
    {
        Grab = [new GrabEntry { Cost = ConfiguredCost.Free }],
        AudiobookshelfSeries = mode,
        AudiobookshelfLibraries = [.. libraries],
        SearchesPerRun = 10,
    };

    private static FollowEntity Follow(string name, FollowMode mode) => new()
    {
        Kind = FollowKind.Series,
        NameKey = SeriesNameKey.Of(name),
        DisplayName = name,
        Mode = mode,
        CreatedAt = Now.AddDays(-1),
        LastSearchedAt = Now,
    };

    [Fact]
    public async Task Run_FollowsEachSeries_InTheConfiguredMode_FromAudiobookshelf()
    {
        var harness = CreateHarness();

        var summary = await harness.RunAsync(Settings(AudiobookshelfSeriesMode.Auto));

        var follows = await harness.Context.Follows.OrderBy(f => f.DisplayName).ToListAsync();
        Assert.Equal(["Earthseed", "The Expanse"], follows.Select(f => f.DisplayName));
        Assert.All(follows, f => Assert.Equal((FollowKind.Series, FollowMode.Download, FollowSource.Audiobookshelf), (f.Kind, f.Mode, f.Source)));
        Assert.Equal(2, summary.AddedFromAudiobookshelf);
        Assert.Equal(2, summary.Searched);
    }

    [Fact]
    public async Task Run_FollowMode_FollowsAsWatched()
    {
        var harness = CreateHarness();

        await harness.RunAsync(Settings(AudiobookshelfSeriesMode.Follow));

        Assert.All(await harness.Context.Follows.ToListAsync(), f => Assert.Equal(FollowMode.Watch, f.Mode));
    }

    [Fact]
    public async Task Run_IgnoredAndFollowedSeries_KeepTheirMode()
    {
        var harness = CreateHarness();
        harness.Context.Follows.AddRange(Follow("The Expanse", FollowMode.Ignore), Follow("Earthseed", FollowMode.Download));
        await harness.Context.SaveChangesAsync();

        var summary = await harness.RunAsync(Settings(AudiobookshelfSeriesMode.Follow));

        var follows = await harness.Context.Follows.AsNoTracking().OrderBy(f => f.DisplayName).ToListAsync();
        Assert.Equal([FollowMode.Download, FollowMode.Ignore], follows.Select(f => f.Mode));
        Assert.All(follows, f => Assert.Equal(FollowSource.Manual, f.Source));
        Assert.Equal(0, summary.AddedFromAudiobookshelf);
    }

    [Fact]
    public async Task Run_SeriesThatLeavesAudiobookshelf_StaysFollowed_AndItsBooksNoLongerCountAsOwned()
    {
        var harness = CreateHarness();
        await harness.RunAsync(Settings());

        harness.Abs.Libraries["Audiobooks"].Remove("The Expanse");
        await harness.RunAsync(Settings());

        Assert.Equal(2, await harness.Context.Follows.CountAsync());
        Assert.DoesNotContain(await harness.Context.AudiobookshelfSeriesBooks.ToListAsync(), b => b.Name == "The Expanse");
    }

    [Fact]
    public async Task Run_KeepsEachBookAndFormat_WithItsPlace()
    {
        var harness = CreateHarness();
        harness.Abs.Libraries["Audiobooks"]["The Expanse"] =
        [
            new("e1", "Leviathan Wakes", "The Expanse #1", Audio: true, Ebook: true),
            new("e2", "Caliban's War", "The Expanse #2", Audio: false, Ebook: true),
            new("e3", "Drive", "The Expanse #2.5-3", Audio: false, Ebook: false),
        ];

        await harness.RunAsync(Settings());

        var books = await harness.Context.AudiobookshelfSeriesBooks.Where(b => b.Name == "The Expanse").OrderBy(b => b.ItemId).ThenBy(b => b.MediaType).ToListAsync();
        Assert.Equal(
            ["e1 Audiobook 1", "e1 Ebook 1", "e2 Ebook 2", "e3 Audiobook 2.5-3", "e3 Ebook 2.5-3"],
            books.Select(b => $"{b.ItemId} {b.MediaType} {b.Display}"));
        Assert.Equal((2.5, 3.0), (books[3].FirstNumber!.Value, books[3].LastNumber!.Value));
        Assert.Equal("Earthseed", (await harness.Context.AudiobookshelfSeriesBooks.SingleAsync(b => b.ItemId == "s1")).Name);
    }

    [Fact]
    public async Task Run_BookFetchshelfLinked_IsNotKeptAsAudiobookshelfs()
    {
        var harness = CreateHarness();
        var metadata = new TorrentMetadata
        {
            MamId = 9,
            VipStatus = VipStatus.NotVip,
            MediaType = MediaType.Audiobook,
            LegacyCategory = LegacyCategory.AudiobookActionAdventure,
            Language = Language.English,
            Title = "Leviathan Wakes",
            Authors = ["James S. A. Corey"],
            MetadataSource = MetadataSource.Mam,
            UploadTime = DateTimeOffset.UnixEpoch,
        };
        harness.Context.Torrents.Add(new TorrentEntity
        {
            Id = "hash9",
            MamId = 9,
            Title = "Leviathan Wakes",
            NormalizedTitleKey = "leviathan wakes",
            MetadataJson = Core.Organizer.TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = Now,
            AbsId = "e1",
        });
        await harness.Context.SaveChangesAsync();

        await harness.RunAsync(Settings());

        Assert.DoesNotContain(await harness.Context.AudiobookshelfSeriesBooks.ToListAsync(), b => b.ItemId == "e1");
    }

    [Fact]
    public async Task Run_BookAudiobookshelfHas_IsOwned_SoTheFollowDoesntTakeIt()
    {
        // MAM has books 1 and 3 of The Expanse as audiobooks; Audiobookshelf has 1 and 2.
        var harness = CreateHarness(body => body.Contains("Expanse", StringComparison.Ordinal)
            ? $$"""{"perpage":100,"start":0,"total":2,"found":2,"data":[{{Record(101, "Leviathan Wakes", "1")}},{{Record(103, "Abaddon's Gate", "3")}}]}"""
            : """{"perpage":100,"start":0,"total":0,"found":0,"data":[]}""");

        var summary = await harness.RunAsync(Settings(AudiobookshelfSeriesMode.Auto));

        Assert.Equal(1, summary.Selected);
        Assert.Equal(103, (await harness.Context.SelectedTorrents.SingleAsync()).MamId);
    }

    [Fact]
    public async Task Run_SeriesOnlyInAudiobookshelf_IsOnTheLibrarysSeriesList()
    {
        var harness = CreateHarness();

        await harness.RunAsync(Settings());

        var series = await new LibraryQueries(harness.Context).SeriesAsync();
        var expanse = Assert.Single(series, s => s.Name == "The Expanse");
        Assert.Equal(["1", "2"], expanse.Entries.Select(e => e.Display));
        Assert.Equal(2, expanse.Books);
    }

    [Fact]
    public async Task Run_OnlyTheNamedLibrariesAreRead()
    {
        var harness = CreateHarness();
        harness.Abs.Libraries["Comics"] = new() { ["Saga"] = [new("c1", "Saga, Volume 1", "Saga #1", Audio: false, Ebook: true)] };

        await harness.RunAsync(Settings(AudiobookshelfSeriesMode.Follow, "comics", "Nonexistent"));

        Assert.Equal(["Saga"], await harness.Context.Follows.Select(f => f.DisplayName).ToListAsync());
    }

    [Fact]
    public async Task Run_DryRun_FollowsAndKeepsNothing_ButSaysWhatItWouldAdd()
    {
        var harness = CreateHarness();

        var summary = await harness.RunAsync(Settings(), dryRun: true);

        Assert.Equal(2, summary.AddedFromAudiobookshelf);
        Assert.Empty(harness.Context.Follows);
        Assert.Empty(harness.Context.AudiobookshelfSeriesBooks);
    }

    [Fact]
    public async Task Run_AudiobookshelfFailing_StillSearchesTheFollowsThereAre()
    {
        var harness = CreateHarness();
        var discworld = Follow("Discworld", FollowMode.Watch);
        discworld.LastSearchedAt = null;
        harness.Context.Follows.Add(discworld);
        await harness.Context.SaveChangesAsync();
        harness.Abs.Fail = true;

        var summary = await harness.RunAsync(Settings());

        Assert.Equal(1, summary.Searched);
        Assert.Single(harness.Searches);
        Assert.Equal(0, summary.AddedFromAudiobookshelf);
    }

    private static FollowService Service(Harness harness) => new(
        harness.Context,
        new MamClient(new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))), new MamClientOptions { MamId = "c" }),
        new MamAccessGate(),
        new SelectionRunner(harness.Context, new EventStore(harness.Context), [], SelectionTestSupport.PreferenceLists),
        Settings(),
        new Clock());

    [Fact]
    public async Task Run_SeriesStoppedFollowing_IsNotFollowedAgain()
    {
        var harness = CreateHarness();
        await harness.RunAsync(Settings());
        var expanse = await harness.Context.Follows.AsNoTracking().SingleAsync(f => f.DisplayName == "The Expanse");

        await Service(harness).StopAsync(expanse.Id);
        harness.Context.ChangeTracker.Clear();
        var summary = await harness.RunAsync(Settings());

        Assert.Equal(0, summary.AddedFromAudiobookshelf);
        Assert.Equal(["Earthseed"], await harness.Context.Follows.Select(f => f.DisplayName).ToListAsync());
        // Its books still count as owned: they are in Audiobookshelf.
        Assert.Contains(await harness.Context.AudiobookshelfSeriesBooks.ToListAsync(), b => b.Name == "The Expanse");
    }

    [Fact]
    public async Task Run_SeriesFollowedAgainByHand_AndStoppedAgain_StaysStopped()
    {
        var harness = CreateHarness();
        await harness.RunAsync(Settings());
        var service = Service(harness);
        await service.StopAsync((await harness.Context.Follows.AsNoTracking().SingleAsync(f => f.DisplayName == "The Expanse")).Id);

        var again = await service.SetAsync(FollowKind.Series, SeriesNameKey.Of("The Expanse"), "The Expanse", FollowMode.Watch);
        Assert.Empty(harness.Context.StoppedSeries);

        await service.StopAsync(again.Id);
        harness.Context.ChangeTracker.Clear();
        await harness.RunAsync(Settings());

        Assert.DoesNotContain(await harness.Context.Follows.Select(f => f.DisplayName).ToListAsync(), name => name == "The Expanse");
    }

    private static string Record(long mamId, string title, string number) => $$"""
        {
            "id": {{mamId}}, "title": "{{title}}", "author_info": "{\"1\":\"James S. A. Corey\"}",
            "narrator_info": "{}", "series_info": "{\"7\":[\"The Expanse\",\"{{number}}\"]}",
            "isbn": null, "description": null, "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[]",
            "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1, "size": "100 MiB",
            "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
            "seeders": 5, "leechers": 1, "times_completed": 1,
            "free": true, "personal_freeleech": false, "fl_vip": false, "vip": false, "vip_expire": 0, "dl": "dl{{mamId}}"
        }
        """;
}
