using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>
/// The author, narrator and series rows and the language and media type columns, kept in step with the metadata JSON
/// by every save, and <see cref="LibraryQueries"/> over them. Against SQLite, so the cascade and the transaction are real.
/// </summary>
public sealed class TorrentProjectionSyncTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fetchshelf-projection-{Guid.NewGuid():N}.db");

    public TorrentProjectionSyncTests()
    {
        using var context = CreateContext();
        DatabaseMigrator.Migrate(context);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private FetchshelfDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FetchshelfDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private static TorrentMetadata Metadata(
        string title, IReadOnlyList<string> authors, string? series = null, string? entries = null,
        MediaType mediaType = MediaType.Audiobook, IReadOnlyList<string>? narrators = null) => new()
        {
            MamId = 1,
            VipStatus = VipStatus.NotVip,
            MediaType = mediaType,
            LegacyCategory = LegacyCategory.AudiobookActionAdventure,
            Language = Language.English,
            Title = title,
            Authors = authors,
            Narrators = narrators ?? [],
            Series = series is null ? [] : [SeriesParser.Parse(series, entries)],
            MetadataSource = MetadataSource.Mam,
            UploadTime = DateTimeOffset.UnixEpoch,
        };

    private static TorrentEntity Torrent(string id, TorrentMetadata metadata) => new()
    {
        Id = id,
        MamId = id.GetHashCode(),
        Title = metadata.Title,
        NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
        MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private async Task AddAsync(params TorrentEntity[] torrents)
    {
        await using var context = CreateContext();
        context.Torrents.AddRange(torrents);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Adding_ATorrent_WritesItsRowsAndColumns()
    {
        await AddAsync(Torrent("h1", Metadata("Leviathan Wakes", ["James S. A. Corey"], "The Expanse", "1", narrators: ["Jefferson Mays"])));

        await using var context = CreateContext();
        Assert.Equal(
            [(PersonRole.Author, "James S. A. Corey"), (PersonRole.Narrator, "Jefferson Mays")],
            context.TorrentPeople.OrderBy(p => p.Role).AsEnumerable().Select(p => (p.Role, p.Name)));
        var series = Assert.Single(context.TorrentSeries);
        Assert.Equal(("expanse", 1.0, "1"), (series.NameKey, series.FirstNumber, series.Display));
        var torrent = context.Torrents.Single();
        Assert.Equal(("English", "Audiobook"), (torrent.Language, torrent.MediaType));
    }

    [Fact]
    public void Adding_WithTheSynchronousSave_WritesTheRowsToo()
    {
        using (var context = CreateContext())
        {
            context.Torrents.Add(Torrent("h1", Metadata("Dune", ["Frank Herbert"])));
            context.SaveChanges();
        }

        using var reopened = CreateContext();
        Assert.Equal("Frank Herbert", reopened.TorrentPeople.Single().Name);
    }

    [Fact]
    public async Task ChangingTheMetadata_ReplacesTheRows()
    {
        await AddAsync(Torrent("h1", Metadata("Dune", ["Frank Herbert"], "Dune", "1")));

        await using (var context = CreateContext())
        {
            var torrent = context.Torrents.Single();
            torrent.MetadataJson = TorrentMetadataSerializer.Serialize(
                Metadata("Dune", ["Frank Herbert", "Brian Herbert"], "Dune Chronicles", "1", MediaType.Ebook));
            await context.SaveChangesAsync();
        }

        await using var reopened = CreateContext();
        Assert.Equal(["Frank Herbert", "Brian Herbert"], reopened.TorrentPeople.OrderBy(p => p.Position).Select(p => p.Name));
        Assert.Equal("Dune Chronicles", reopened.TorrentSeries.Single().Name);
        Assert.Equal("Ebook", reopened.Torrents.Single().MediaType);
    }

    [Fact]
    public async Task ChangingAnotherColumn_LeavesTheRowsAlone()
    {
        await AddAsync(Torrent("h1", Metadata("Dune", ["Frank Herbert"])));
        await using (var context = CreateContext())
        {
            context.Database.ExecuteSqlRaw("UPDATE TorrentPeople SET Name = 'set by hand';");
            context.Torrents.Single().LibraryPath = "/library/Dune";
            await context.SaveChangesAsync();
        }

        await using var reopened = CreateContext();
        Assert.Equal("set by hand", reopened.TorrentPeople.Single().Name);
    }

    [Fact]
    public async Task DeletingATorrent_DeletesItsRows()
    {
        await AddAsync(Torrent("h1", Metadata("Dune", ["Frank Herbert"], "Dune", "1")), Torrent("h2", Metadata("Emma", ["Jane Austen"])));

        await using (var context = CreateContext())
        {
            context.Torrents.Remove(context.Torrents.Single(t => t.Id == "h1"));
            await context.SaveChangesAsync();
        }

        await using var reopened = CreateContext();
        Assert.Equal("Jane Austen", reopened.TorrentPeople.Single().Name);
        Assert.Empty(reopened.TorrentSeries);
    }

    [Fact]
    public async Task AFailedSave_LeavesTheOldRows_AndARetryMakesNoDuplicates()
    {
        await AddAsync(Torrent("h1", Metadata("Dune", ["Frank Herbert"])));

        await using (var context = CreateContext())
        {
            context.Torrents.Single().MetadataJson = TorrentMetadataSerializer.Serialize(Metadata("Dune", ["Someone Else"]));
            // Two errors with the same stage and key break a unique index, so the whole save fails.
            var errors = Enumerable.Range(0, 2)
                .Select(_ => new ErroredTorrentEntity { Stage = ErrorStage.Linker, Key = "k", ErrorText = "e", OccurredAt = DateTimeOffset.UnixEpoch })
                .ToList();
            context.ErroredTorrents.AddRange(errors);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

            await using (var check = CreateContext())
            {
                Assert.Equal("Frank Herbert", check.TorrentPeople.Single().Name);
            }

            errors.ForEach(e => context.Entry(e).State = EntityState.Detached);
            await context.SaveChangesAsync();
        }

        await using var reopened = CreateContext();
        Assert.Equal("Someone Else", reopened.TorrentPeople.Single().Name);
    }

    [Fact]
    public async Task UnreadableMetadata_SavesTheTorrentWithoutRows()
    {
        var torrent = Torrent("h1", Metadata("Dune", ["Frank Herbert"]));
        torrent.MetadataJson = "{ not json";

        await AddAsync(torrent);

        await using var context = CreateContext();
        Assert.Equal("h1", context.Torrents.Single().Id);
        Assert.Null(context.Torrents.Single().Language);
        Assert.Empty(context.TorrentPeople);
    }

    [Fact]
    public async Task People_CountsBooksPerAuthor_WithTheSpellingUsedMost_AndLeavesOutReplacedCopies()
    {
        var replaced = Torrent("h4", Metadata("Emma", ["Jane Austen"]));
        replaced.ReplacedWithTorrentId = "h3";
        await AddAsync(
            Torrent("h1", Metadata("Persuasion", ["Jane Austen"])),
            Torrent("h2", Metadata("Sense and Sensibility", ["JANE AUSTEN"])),
            Torrent("h3", Metadata("Emma", ["Jane Austen"])),
            replaced,
            Torrent("h5", Metadata("Dune", ["Frank Herbert"])));

        await using var context = CreateContext();
        var people = await new LibraryQueries(context).PeopleAsync(PersonRole.Author);

        Assert.Equal([new PersonSummary("frankherbert", "Frank Herbert", 1), new PersonSummary("janeausten", "Jane Austen", 3)], people);
    }

    [Fact]
    public async Task Series_ListsOwnedEntries_AndOwnedEntries_SplitsAudiobooksFromEbooks()
    {
        await AddAsync(
            Torrent("h1", Metadata("Leviathan Wakes", ["Corey"], "The Expanse", "1")),
            Torrent("h2", Metadata("Caliban's War", ["Corey"], "Expanse", "2-3")),
            Torrent("h3", Metadata("Leviathan Wakes", ["Corey"], "The Expanse", "1", MediaType.Ebook)),
            Torrent("h4", Metadata("Memory Called Empire", ["Martine"], "Teixcalaan", null)));

        await using var context = CreateContext();
        var queries = new LibraryQueries(context);
        var all = await queries.SeriesAsync();
        var audio = await queries.OwnedEntriesAsync("expanse", FilterMainType.Audio);
        var ebook = await queries.OwnedEntriesAsync("expanse", FilterMainType.Ebook);

        Assert.Equal(["Teixcalaan", "The Expanse"], all.Select(s => s.Name));
        Assert.Empty(all[0].Entries);
        Assert.Equal(3, all[1].Books);
        Assert.Equal(["Audiobook", "Ebook"], all[1].MediaTypes);
        Assert.Equal([("1", 1.0, 1.0), ("2-3", 2.0, 3.0)], audio.Select(e => (e.Display!, e.FirstNumber!.Value, e.LastNumber!.Value)));
        Assert.Equal(["1"], ebook.Select(e => e.Display));
    }

    [Fact]
    public async Task TorrentIds_FindByAuthorNarratorAndSeriesKey()
    {
        await AddAsync(
            Torrent("h1", Metadata("Dune", ["Frank Herbert"], "Dune", "1", narrators: ["Scott Brick"])),
            Torrent("h2", Metadata("Emma", ["Jane Austen"])));

        await using var context = CreateContext();
        var queries = new LibraryQueries(context);

        Assert.Equal(["h1"], await queries.TorrentIdsWithPerson(PersonRole.Author, "frankherbert").ToListAsync());
        Assert.Equal(["h1"], await queries.TorrentIdsWithPerson(PersonRole.Narrator, "scottbrick").ToListAsync());
        Assert.Empty(await queries.TorrentIdsWithPerson(PersonRole.Narrator, "frankherbert").ToListAsync());
        Assert.Equal(["h1"], await queries.TorrentIdsInSeries("dune").ToListAsync());
        Assert.Equal("Frank Herbert", await queries.PersonNameAsync(PersonRole.Author, "frankherbert"));
        Assert.Equal("Dune", await queries.SeriesNameAsync("dune"));
        Assert.Null(await queries.SeriesNameAsync("nothing"));
    }
}
