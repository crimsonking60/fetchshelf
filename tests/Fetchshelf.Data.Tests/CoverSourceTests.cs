using System.Net;
using Fetchshelf.Clients.Covers;
using Fetchshelf.Clients.Hardcover;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Data.Tests;

/// <summary>Which books Open Library and Audnexus are asked about, by what, and the chain that tries them in order.</summary>
public sealed class CoverSourceTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0];

    private static readonly CoverClientOptions Options = new() { UserAgent = "Fetchshelf/test", OpenLibrarySpacing = TimeSpan.Zero, AudnexusSpacing = TimeSpan.Zero };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private static CoverQuery Book(MediaType mediaType, string? isbn = null, string? asin = null)
    {
        var metadata = OrganizerTestSupport.BuildMetadata() with { MediaType = mediaType, Isbn = isbn, Asin = asin };
        return CoverQuery.For(1, metadata);
    }

    [Fact]
    public async Task OpenLibrary_AskedByTheIsbn13_EvenWhenTheBookGivesItsIsbn10()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Jpeg) });
        var source = new OpenLibraryCoverSource(new OpenLibraryClient(new HttpClient(handler), Options));

        var cover = await source.FetchAsync(Book(MediaType.Ebook, isbn: "0-7653-2635-3"));

        Assert.Equal(new[] { "https://covers.openlibrary.org/b/isbn/9780765326355-L.jpg?default=false" }, handler.Asked);
        Assert.Equal(("image/jpeg", "Open Library"), (cover!.ContentType, cover.Source));
    }

    [Fact]
    public async Task OpenLibrary_SomethingThatIsNoImage_IsNoCover()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>") });
        var source = new OpenLibraryCoverSource(new OpenLibraryClient(new HttpClient(handler), Options));

        Assert.Null(await source.FetchAsync(Book(MediaType.Ebook, isbn: "9780765326355")));
    }

    [Fact]
    public void WhoCanFindWhat_OpenLibraryAnIsbn_AudnexusAnAudiobooksAsin()
    {
        var openLibrary = new OpenLibraryCoverSource(new OpenLibraryClient(new HttpClient(), Options));
        var audnexus = new AudnexusCoverSource(new AudnexusClient(new HttpClient(), Options));

        Assert.True(openLibrary.CanFind(Book(MediaType.Ebook, isbn: "9780765326355")));
        Assert.False(openLibrary.CanFind(Book(MediaType.Ebook, isbn: "12345")));
        // MAM's ISBN field often holds an audiobook's ASIN: that is no ISBN, but Audnexus finds it.
        Assert.False(openLibrary.CanFind(Book(MediaType.Audiobook, isbn: "ASIN: B00ABCDEFG")));
        Assert.True(audnexus.CanFind(Book(MediaType.Audiobook, isbn: "ASIN: B00ABCDEFG")));
        Assert.True(audnexus.CanFind(Book(MediaType.Audiobook, asin: "B00ABCDEFG")));
        Assert.False(audnexus.CanFind(Book(MediaType.Ebook, asin: "B00ABCDEFG")));
    }

    [Fact]
    public async Task Audnexus_RefusedBecomesTheSourceRefusing()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var source = new AudnexusCoverSource(new AudnexusClient(new HttpClient(handler), Options));

        await Assert.ThrowsAsync<CoverSourceRefusedException>(() => source.FetchAsync(Book(MediaType.Audiobook, asin: "B00ABCDEFG")));
    }

    private sealed class Fake(string name, Func<CoverQuery, bool> canFind, Func<CoverImage?> fetch) : ICoverSource
    {
        public int Asked { get; private set; }

        public bool CanFind(CoverQuery book) => canFind(book);

        public Task<CoverImage?> FetchAsync(CoverQuery book, CancellationToken cancellationToken = default)
        {
            Asked++;
            return Task.FromResult(fetch());
        }

        public override string ToString() => name;
    }

    private static CoverImage Cover(string source) => new(Jpeg, "image/jpeg", source);

    [Fact]
    public async Task Chain_TheFirstSourceThatCanFindTheBookAndHasACoverWins()
    {
        var cannot = new Fake("cannot", _ => false, () => Cover("cannot"));
        var none = new Fake("none", _ => true, () => null);
        var has = new Fake("has", _ => true, () => Cover("has"));
        var after = new Fake("after", _ => true, () => Cover("after"));

        var cover = await new ChainedCoverSource([cannot, none, has, after]).FetchAsync(Book(MediaType.Ebook));

        Assert.Equal("has", cover!.Source);
        Assert.Equal((0, 1, 1, 0), (cannot.Asked, none.Asked, has.Asked, after.Asked));
    }

    [Fact]
    public async Task Chain_ARefusingSource_IsLeftOutForTheRun_BooksOnlyItCouldFindWait_AndTheOthersGoOn()
    {
        var refusing = new Fake("refusing", _ => true, () => throw new CoverSourceRefusedException("Slow down."));
        var other = new Fake("other", torrent => torrent.MamId == 2, () => Cover("other"));
        var chain = new ChainedCoverSource([refusing, other]);
        var onlyRefusing = Book(MediaType.Ebook);
        var both = Book(MediaType.Ebook) with { MamId = 2 };

        await Assert.ThrowsAsync<CoverSourceDeferredException>(() => chain.FetchAsync(onlyRefusing));
        var cover = await chain.FetchAsync(both);
        await Assert.ThrowsAsync<CoverSourceDeferredException>(() => chain.FetchAsync(onlyRefusing));

        Assert.Equal("other", cover!.Source);
        Assert.Equal(1, refusing.Asked);
    }

    [Fact]
    public async Task Chain_EverySourceRefusing_EndsFetchingForTheRun()
    {
        var refusing = new Fake("refusing", _ => true, () => throw new CoverSourceRefusedException("Slow down."));

        await Assert.ThrowsAsync<CoverSourceRefusedException>(() => new ChainedCoverSource([refusing]).FetchAsync(Book(MediaType.Ebook)));
    }

    /// <summary>A Hardcover source against fake Hardcover and image servers, which records what was asked.</summary>
    private static (HardcoverCoverSource Source, List<string> Asked) Hardcover(Func<string, string> graphql, Func<Uri, HttpResponseMessage>? image = null)
    {
        var asked = new List<string>();
        var api = new Handler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            asked.Add(body.Contains("EditionCover", StringComparison.Ordinal) ? "edition" : "search");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(graphql(body)) };
        });
        var images = new Handler(request =>
        {
            asked.Add(request.RequestUri!.ToString());
            return image?.Invoke(request.RequestUri) ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Jpeg) };
        });
        var client = new HardcoverClient(new HttpClient(api), new HardcoverClientOptions { Token = "token", UserAgent = "Fetchshelf/test" });
        return (new HardcoverCoverSource(client, new ImageClient(new HttpClient(images), Options), new RequestSpacer(TimeSpan.Zero)), asked);
    }

    private const string NoEdition = """{"data":{"editions":[]}}""";

    private static string Hits(params (string Title, string Author, string Image)[] hits) =>
        "{\"data\":{\"search\":{\"results\":{\"hits\":["
        + string.Join(",", hits.Select(h => $"{{\"document\":{{\"title\":\"{h.Title}\",\"author_names\":[\"{h.Author}\"],\"image\":{{\"url\":\"{h.Image}\"}}}}}}"))
        + "]}}}}";

    [Fact]
    public async Task Hardcover_ByIsbn_TakesTheEditionsCover_WithoutSearching()
    {
        var (source, asked) = Hardcover(_ => """{"data":{"editions":[{"image":{"url":"https://assets.hardcover.app/e.jpg"}}]}}""");

        var cover = await source.FetchAsync(Book(MediaType.Ebook, isbn: "9780765326355"));

        Assert.Equal(("Hardcover", "image/jpeg"), (cover!.Source, cover.ContentType));
        Assert.Equal(["edition", "https://assets.hardcover.app/e.jpg"], asked);
    }

    [Fact]
    public async Task Hardcover_NoEdition_SearchesByTitleAndAuthor_TakingOnlyAnExactMatch()
    {
        var (source, asked) = Hardcover(body => body.Contains("EditionCover", StringComparison.Ordinal) ? NoEdition : Hits(
            ("Brandon Sanderson Sampler: The Way of Kings and Mistborn", "Brandon Sanderson", "https://assets.hardcover.app/sampler.jpg"),
            ("The Way of Kings", "Someone Else", "https://assets.hardcover.app/namesake.jpg"),
            ("The Way of Kings", "Brandon Sanderson", "https://assets.hardcover.app/right.jpg")));

        var cover = await source.FetchAsync(Book(MediaType.Ebook, isbn: "9780765326355"));

        Assert.NotNull(cover);
        Assert.Equal(["edition", "search", "https://assets.hardcover.app/right.jpg"], asked);
    }

    [Fact]
    public async Task Hardcover_NoMatch_OrAnImageElsewhere_IsNoCover()
    {
        var (noMatch, _) = Hardcover(_ => Hits(("Words of Radiance", "Brandon Sanderson", "https://assets.hardcover.app/w.jpg")));
        var (elsewhere, asked) = Hardcover(_ => Hits(("The Way of Kings", "Brandon Sanderson", "https://example.com/w.jpg")));

        Assert.Null(await noMatch.FetchAsync(Book(MediaType.Ebook)));
        Assert.Null(await elsewhere.FetchAsync(Book(MediaType.Ebook)));
        Assert.Equal(["search"], asked);
    }

    [Fact]
    public async Task Hardcover_AnError_RefusesForTheRun()
    {
        var (source, _) = Hardcover(_ => """{"errors":[{"message":"invalid token"}]}""");

        await Assert.ThrowsAsync<CoverSourceRefusedException>(() => source.FetchAsync(Book(MediaType.Ebook)));
    }

    [Fact]
    public void CoverQuery_FromAnAddress_KeepsOnlyAValidIsbn_AndAnAsinOnlyForAnAudiobook()
    {
        var ebook = CoverQuery.Of(1, " The Way of Kings ", ["Brandon Sanderson", " "], "0-7653-2635-3", "B00ABCDEFG", audiobook: false);
        var audiobook = CoverQuery.Of(1, "The Way of Kings", ["Brandon Sanderson"], "not an isbn", "B00ABCDEFG", audiobook: true);

        Assert.Equal(("The Way of Kings", "9780765326355", (string?)null), (ebook.Title, ebook.Isbn, ebook.AudiobookAsin));
        Assert.Equal(["Brandon Sanderson"], ebook.Authors);
        Assert.Equal(((string?)null, "B00ABCDEFG"), (audiobook.Isbn, audiobook.AudiobookAsin));
    }
}
