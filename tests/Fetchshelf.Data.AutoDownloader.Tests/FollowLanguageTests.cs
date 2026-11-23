using System.Net;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader.Tests;

/// <summary>The Follow job takes a book in two languages for two books: found, owned and decided per language.</summary>
public sealed class FollowLanguageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-follow-language-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A MAM result: a book of The Expanse (or by James S. A. Corey when <paramref name="number"/> is null), in a language by MAM id.</summary>
    private static string Record(long mamId, string title, string? number, int language, bool ebook = false, bool free = true) => $$"""
        {
            "id": {{mamId}}, "title": "{{title}}", "author_info": "{\"1\":\"James S. A. Corey\"}",
            "narrator_info": "{}", "series_info": "{{(number is null ? "{}" : $"{{\\\"7\\\":[\\\"The Expanse\\\",\\\"{number}\\\"]}}")}}",
            "isbn": null, "description": null,
            "mediatype": {{(ebook ? 2 : 1)}}, "main_cat": {{(ebook ? 14 : 13)}}, "maincat": 1, "category": {{(ebook ? 60 : 39)}}, "categories": "[]",
            "language": {{language}}, "browseflags": 0, "filetype": "{{(ebook ? "epub" : "m4b")}}", "numfiles": 1, "size": "100 MiB",
            "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
            "seeders": 5, "leechers": 1, "times_completed": 1,
            "free": {{(free ? "true" : "false")}}, "personal_freeleech": false, "fl_vip": false, "vip": false,
            "vip_expire": 0, "dl": "dl{{mamId}}"
        }
        """;

    private const int English = 1;
    private const int Hungarian = 28;
    private const int Greek = 26;

    private sealed record Harness(FetchshelfDbContext Context, Func<FollowSettings, FollowRunner> Create)
    {
        public Task<FollowRunSummary> RunAsync(FollowSettings settings) => Create(settings).RunAsync(ignoreTorrents: []);

        public List<FollowFindEntity> Finds() => Context.FollowFinds.AsNoTracking().OrderBy(f => f.MamId).ToList();
    }

    private Harness CreateHarness(params string[] records)
    {
        var context = SelectionTestSupport.CreateContext(DbPath);
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"ratio":5.0,"uploaded_bytes":1000,"downloaded_bytes":100,"wedges":10,"uid":1,"snatch_summary":{"unsat":{"count":0,"limit":20}}}"""),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"perpage":100,"start":0,"total":{{records.Length}},"found":{{records.Length}},"data":[{{string.Join(",", records)}}]}"""),
            };
        });
        var mamClient = new MamClient(new HttpClient(handler), new MamClientOptions { MamId = "cookie" }, delay: (_, _) => Task.CompletedTask);
        return new Harness(context, settings => new FollowRunner(
            context, mamClient, new MamAccessGate(),
            new SelectionRunner(context, new EventStore(context), [], SelectionTestSupport.PreferenceLists),
            new LibraryQueries(context), settings, SelectionTestSupport.PreferenceLists, new Clock(),
            delay: (_, _) => Task.CompletedTask));
    }

    private static FollowSettings Settings(FilterMainType? preferFormat = null, params GrabEntry[] grab) => new()
    {
        Grab = grab.Length == 0 ? [new GrabEntry { Cost = ConfiguredCost.Free }] : [.. grab],
        PreferFormat = preferFormat,
    };

    private static void Follow(Harness harness, FollowKind kind, FollowMode mode)
    {
        harness.Context.Follows.Add(new FollowEntity
        {
            Kind = kind,
            NameKey = kind == FollowKind.Series ? SeriesNameKey.Of("The Expanse") : PersonNameKey.Of("James S. A. Corey"),
            DisplayName = kind == FollowKind.Series ? "The Expanse" : "James S. A. Corey",
            Mode = mode,
            CreatedAt = Now.AddDays(-1),
        });
        harness.Context.SaveChanges();
    }

    private static void AddEnglishCollectionToLibrary(Harness harness)
    {
        var metadata = new TorrentMetadata
        {
            MamId = 900,
            VipStatus = VipStatus.NotVip,
            MediaType = MediaType.Audiobook,
            LegacyCategory = LegacyCategory.AudiobookActionAdventure,
            Language = Language.English,
            Title = "The Expanse Books 1-3",
            Authors = ["James S. A. Corey"],
            Series = [SeriesParser.Parse("The Expanse", "1-3")],
            FileTypes = ["m4b"],
            MetadataSource = MetadataSource.Mam,
            UploadTime = DateTimeOffset.UnixEpoch,
        };
        harness.Context.Torrents.Add(new TorrentEntity
        {
            Id = "hash900",
            MamId = 900,
            Title = metadata.Title,
            NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = Now,
            LibraryPath = "/library/expanse",
        });
        harness.Context.SaveChanges();
    }

    [Fact]
    public async Task OneEntryInTwoLanguages_IsTwoFinds_EachWithItsLanguage()
    {
        var harness = CreateHarness(Record(101, "Leviathan Wakes", "1", English), Record(102, "Leviathan ébred", "1", Hungarian));
        Follow(harness, FollowKind.Series, FollowMode.Watch);

        await harness.RunAsync(Settings());

        Assert.Equal([(101L, "English"), (102L, "Hungarian")], harness.Finds().Select(f => (f.MamId, f.Language!)));
    }

    [Fact]
    public async Task EntryOwnedInEnglish_HidesOnlyTheEnglishEdition()
    {
        var harness = CreateHarness(Record(102, "Caliban's War", "2", English), Record(202, "Kalibán háborúja", "2", Hungarian));
        Follow(harness, FollowKind.Series, FollowMode.Watch);
        AddEnglishCollectionToLibrary(harness);

        await harness.RunAsync(Settings());

        // The English edition is listed as already owned; only the Hungarian one is to look at.
        Assert.Equal([(102L, FollowFindStatus.Owned), (202L, FollowFindStatus.New)], harness.Finds().OrderBy(f => f.MamId).Select(f => (f.MamId, f.Status)));
    }

    [Fact]
    public async Task FreeHungarianBlock_AndRatioEnglishBlock_DecideEachLanguageOnItsOwn()
    {
        var harness = CreateHarness(Record(101, "Leviathan Wakes", "1", English, free: false), Record(102, "Leviathan ébred", "1", Hungarian, free: false));
        Follow(harness, FollowKind.Series, FollowMode.Download);

        await harness.RunAsync(Settings(null,
            new GrabEntry { Cost = ConfiguredCost.Free, Languages = ["Hungarian"] },
            new GrabEntry { Cost = ConfiguredCost.Ratio, Languages = ["English"] }));

        Assert.Equal(101, (await harness.Context.SelectedTorrents.SingleAsync()).MamId);
        var hungarian = Assert.Single(harness.Finds(), f => f.MamId == 102);
        Assert.Equal(FollowFindStatus.Wanted, hungarian.Status);
    }

    [Fact]
    public async Task PreferFormat_KeepsOneFormatPerLanguage()
    {
        var harness = CreateHarness(
            Record(101, "Leviathan Wakes", "1", English),
            Record(111, "Leviathan Wakes", "1", English, ebook: true),
            Record(112, "Leviathan ébred", "1", Hungarian, ebook: true));
        Follow(harness, FollowKind.Series, FollowMode.Watch);

        await harness.RunAsync(Settings(FilterMainType.Audio));

        // The English ebook gives way to the English audiobook; the Hungarian ebook has no audiobook to give way to.
        Assert.Equal([101L, 112L], harness.Finds().Select(f => f.MamId));
    }

    [Fact]
    public async Task AuthorsBookWithTheSameTitleInTwoLanguages_IsTwoFinds()
    {
        var harness = CreateHarness(Record(301, "Gamma", null, English, ebook: true), Record(302, "Gamma", null, Greek, ebook: true));
        Follow(harness, FollowKind.Author, FollowMode.Watch);

        await harness.RunAsync(Settings());

        Assert.Equal([301L, 302L], harness.Finds().Select(f => f.MamId));
    }

    [Fact]
    public async Task FindRecordedBeforeLanguagesWereKept_GetsItsLanguageWhenFoundAgain()
    {
        var harness = CreateHarness(Record(101, "Leviathan Wakes", "1", English));
        Follow(harness, FollowKind.Series, FollowMode.Watch);
        harness.Context.FollowFinds.Add(new FollowFindEntity
        {
            FollowId = harness.Context.Follows.Single().Id,
            MamId = 101,
            Title = "Leviathan Wakes",
            SeriesDisplay = "1",
            MediaSide = "Audio",
            Status = FollowFindStatus.New,
            FoundAt = Now.AddDays(-5),
            StatusAt = Now.AddDays(-5),
        });
        await harness.Context.SaveChangesAsync();

        await harness.RunAsync(Settings());

        Assert.Equal("English", Assert.Single(harness.Finds()).Language);
    }

    [Fact]
    public async Task AudiobookshelfBook_CountsInItsOwnLanguage_OrInEveryOneWithoutOne()
    {
        var harness = CreateHarness();
        harness.Context.AudiobookshelfSeriesBooks.AddRange(
            new AudiobookshelfSeriesBookEntity
            {
                ItemId = "a",
                Name = "The Expanse",
                NameKey = SeriesNameKey.Of("The Expanse"),
                Title = "Leviathan Wakes",
                Display = "1",
                FirstNumber = 1,
                LastNumber = 1,
                MediaType = "Audiobook",
                Language = "English",
                SyncedAt = Now,
            },
            new AudiobookshelfSeriesBookEntity
            {
                ItemId = "b",
                Name = "The Expanse",
                NameKey = SeriesNameKey.Of("The Expanse"),
                Title = "Caliban's War",
                Display = "2",
                FirstNumber = 2,
                LastNumber = 2,
                MediaType = "Audiobook",
                Language = null,
                SyncedAt = Now,
            });
        await harness.Context.SaveChangesAsync();
        var queries = new LibraryQueries(harness.Context);

        var english = await queries.OwnedEntriesAsync(SeriesNameKey.Of("The Expanse"), FilterMainType.Audio, Language.English);
        var hungarian = await queries.OwnedEntriesAsync(SeriesNameKey.Of("The Expanse"), FilterMainType.Audio, Language.Hungarian);

        Assert.Equal(["1", "2"], english.Select(e => e.Display));
        Assert.Equal(["2"], hungarian.Select(e => e.Display));
    }
}
