using System.Net;
using Fetchshelf.Clients.Covers;
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

    private static TorrentEntity Book(MediaType mediaType, string? isbn = null, string? asin = null)
    {
        var metadata = OrganizerTestSupport.BuildMetadata() with { MediaType = mediaType, Isbn = isbn, Asin = asin };
        return new TorrentEntity
        {
            Id = "hash",
            MamId = 1,
            Title = metadata.Title,
            NormalizedTitleKey = "way of kings",
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
        };
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

    private sealed class Fake(string name, Func<TorrentEntity, bool> canFind, Func<CoverImage?> fetch) : ICoverSource
    {
        public int Asked { get; private set; }

        public bool CanFind(TorrentEntity torrent) => canFind(torrent);

        public Task<CoverImage?> FetchAsync(TorrentEntity torrent, CancellationToken cancellationToken = default)
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
        var both = Book(MediaType.Ebook);
        both.MamId = 2;

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
}
