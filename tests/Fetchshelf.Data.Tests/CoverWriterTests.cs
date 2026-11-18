using Fetchshelf.Clients.Mam;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

public sealed class CoverWriterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-cover-writer-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string DbPath => Path.Combine(_root, "test.db");

    private string LibraryDir => Path.Combine(_root, "library");

    private Library BookLibrary => new() { LibraryDir = LibraryDir, Category = "audiobooks", Method = LibraryLinkMethod.Hardlink };

    /// <summary>MAM's covers, by MAM id; an id without one has none. Records every id asked for.</summary>
    private sealed class FakeCoverSource(Func<long, MamImage?> cover) : IMamCoverSource
    {
        public List<long> Asked { get; } = [];

        public Task<MamImage?> FetchAsync(long mamId, CancellationToken cancellationToken = default)
        {
            Asked.Add(mamId);
            return Task.FromResult(cover(mamId));
        }
    }

    private static MamImage Jpeg => new([0xFF, 0xD8, 0xFF], "image/jpeg");

    /// <summary>A linked book with MAM id <paramref name="mamId"/> whose folder holds <paramref name="files"/>.</summary>
    private async Task<string> SeedLinkedAsync(FetchshelfDbContext context, long mamId, params string[] files)
    {
        var folder = Path.Combine(LibraryDir, "Author", $"Book {mamId}");
        Directory.CreateDirectory(folder);
        foreach (var file in files.DefaultIfEmpty("01.mp3"))
        {
            await File.WriteAllTextAsync(Path.Combine(folder, file), "x");
        }

        var torrent = await OrganizerTestSupport.SeedTorrentAsync(context, $"hash{mamId}", mamId);
        torrent.LibraryPath = folder;
        await context.SaveChangesAsync();
        return folder;
    }

    private static DateTimeOffset? CheckedAt(FetchshelfDbContext context, long mamId) =>
        context.Torrents.AsNoTracking().Single(t => t.MamId == mamId).CoverCheckedAt;

    [Fact]
    public async Task RunAsync_BookWithoutAnImage_GetsMamsCoverAndIsNotLookedAtAgain()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var folder = await SeedLinkedAsync(context, 1);
        var source = new FakeCoverSource(_ => Jpeg);

        var summary = await new CoverWriter(context, source, [BookLibrary]).RunAsync();

        Assert.Equal(new CoverRunSummary(1, 0), summary);
        Assert.Equal([0xFF, 0xD8, 0xFF], await File.ReadAllBytesAsync(Path.Combine(folder, "cover.jpg")));
        Assert.Equal(["01.mp3", "cover.jpg"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
        Assert.NotNull(CheckedAt(context, 1));

        await new CoverWriter(context, source, [BookLibrary]).RunAsync();
        Assert.Equal([1L], source.Asked);
    }

    [Fact]
    public async Task RunAsync_PngCover_IsWrittenAsCoverPng()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var folder = await SeedLinkedAsync(context, 1);

        await new CoverWriter(context, new FakeCoverSource(_ => new MamImage([1], "image/png")), [BookLibrary]).RunAsync();

        Assert.True(File.Exists(Path.Combine(folder, "cover.png")));
    }

    [Fact]
    public async Task RunAsync_FolderWithAnImage_AsksMamNothing()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var folder = await SeedLinkedAsync(context, 1, "01.mp3", "folder.png");
        var source = new FakeCoverSource(_ => Jpeg);

        var summary = await new CoverWriter(context, source, [BookLibrary]).RunAsync();

        Assert.Equal(new CoverRunSummary(0, 0), summary);
        Assert.Empty(source.Asked);
        Assert.False(File.Exists(Path.Combine(folder, "cover.jpg")));
        Assert.NotNull(CheckedAt(context, 1));
    }

    [Fact]
    public async Task RunAsync_MamHasNoCover_WritesNothingAndDoesNotAskAgain()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var folder = await SeedLinkedAsync(context, 1);
        var source = new FakeCoverSource(_ => null);

        Assert.Equal(new CoverRunSummary(0, 0), await new CoverWriter(context, source, [BookLibrary]).RunAsync());
        await new CoverWriter(context, source, [BookLibrary]).RunAsync();

        Assert.Equal(["01.mp3"], Directory.GetFiles(folder).Select(Path.GetFileName));
        Assert.Equal([1L], source.Asked);
    }

    [Fact]
    public async Task RunAsync_ManyBooks_FetchesAtMostTheRunsLimitAndTheRestNextRun()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        for (var mamId = 1; mamId <= CoverWriter.MaxFetchesPerRun + 5; mamId++)
        {
            await SeedLinkedAsync(context, mamId);
        }

        var source = new FakeCoverSource(_ => Jpeg);

        var first = await new CoverWriter(context, source, [BookLibrary]).RunAsync();
        var second = await new CoverWriter(context, source, [BookLibrary]).RunAsync();

        Assert.Equal(new CoverRunSummary(CoverWriter.MaxFetchesPerRun, 5), first);
        Assert.Equal(new CoverRunSummary(5, 0), second);
        Assert.Equal(Enumerable.Range(1, CoverWriter.MaxFetchesPerRun + 5).Select(i => (long)i), source.Asked);
    }

    [Fact]
    public async Task RunAsync_MamRefusesTheAccount_StopsAndLeavesTheBookForLater()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedLinkedAsync(context, 1);
        await SeedLinkedAsync(context, 2);
        var source = new FakeCoverSource(_ => throw new MamRateLimitedException("MAM says too many requests."));

        var summary = await new CoverWriter(context, source, [BookLibrary]).RunAsync();

        Assert.Equal(new CoverRunSummary(0, 2), summary);
        Assert.Equal([1L], source.Asked);
        Assert.Null(CheckedAt(context, 1));
    }

    [Fact]
    public async Task RunAsync_FailedRequest_IsLoggedAndTheNextBookStillGetsItsCover()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedLinkedAsync(context, 1);
        var second = await SeedLinkedAsync(context, 2);
        var source = new FakeCoverSource(mamId => mamId == 1 ? throw new HttpRequestException("timed out") : Jpeg);

        var summary = await new CoverWriter(context, source, [BookLibrary]).RunAsync();

        Assert.Equal(new CoverRunSummary(1, 0), summary);
        Assert.True(File.Exists(Path.Combine(second, "cover.jpg")));
        Assert.NotNull(CheckedAt(context, 1));
    }

    [Theory]
    [InlineData(LibraryLayout.Series, LibraryLinkMethod.Hardlink)]
    [InlineData(LibraryLayout.Book, LibraryLinkMethod.NoLink)]
    public async Task RunAsync_SeriesOrNoLinkLibrary_IsLeftAlone(LibraryLayout layout, LibraryLinkMethod method)
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedLinkedAsync(context, 1);
        var source = new FakeCoverSource(_ => Jpeg);
        var library = new Library { LibraryDir = LibraryDir, Category = "comics", Method = method, Layout = layout };

        await new CoverWriter(context, source, [library]).RunAsync();

        Assert.Empty(source.Asked);
        Assert.NotNull(CheckedAt(context, 1));
    }

    [Fact]
    public async Task RunAsync_BookOutsideEveryLibrary_IsLeftAlone()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedLinkedAsync(context, 1);
        var source = new FakeCoverSource(_ => Jpeg);
        var other = new Library { LibraryDir = Path.Combine(_root, "other"), Category = "ebooks", Method = LibraryLinkMethod.Hardlink };

        await new CoverWriter(context, source, [other]).RunAsync();

        Assert.Empty(source.Asked);
    }
}
