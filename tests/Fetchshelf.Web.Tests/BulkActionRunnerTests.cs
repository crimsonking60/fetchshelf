using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Web.Tests;

/// <summary>The Torrents page's bulk actions as <see cref="BulkActionRunner"/> works through them.</summary>
public sealed class BulkActionRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-bulk-tests-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private sealed class CountingLookup(TorrentMetadata metadata) : IMetadataLookup
    {
        public List<string> Hashes { get; } = [];

        public Task<TorrentMetadata?> TryLookupByInfoHashAsync(string infoHash, CancellationToken cancellationToken = default)
        {
            Hashes.Add(infoHash);
            return Task.FromResult<TorrentMetadata?>(metadata);
        }
    }

    private sealed class NoHttpClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No qBittorrent in this test.");
    }

    [Fact]
    public async Task Refresh_SkipsATorrentEditedByHand_AndRefreshesTheRest()
    {
        using var context = new FetchshelfDbContext(new DbContextOptionsBuilder<FetchshelfDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "test.db")}").Options);
        DatabaseMigrator.Migrate(context);
        var edited = OrganizerTestSupport.BuildMetadata("Edited") with { MetadataSource = MetadataSource.Manual };
        foreach (var (id, metadata) in new[] { ("edited", edited), ("plain", OrganizerTestSupport.BuildMetadata("Plain")) })
        {
            context.Torrents.Add(new TorrentEntity
            {
                Id = id,
                MamId = 1,
                Title = metadata.Title,
                NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
                MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await context.SaveChangesAsync();
        var lookup = new CountingLookup(OrganizerTestSupport.BuildMetadata("From MAM"));
        var eventStore = new EventStore(context);
        var formats = new FormatPreferenceLists { AudioTypes = ["m4b"], EbookTypes = ["epub"], MusicTypes = [], RadioTypes = [] };
        var runner = new BulkActionRunner(
            context,
            new LinkRunner(context, eventStore, lookup, [], formats, new LinkRunnerOptions(ExcludeNarratorInLibraryDir: false)),
            new Cleaner(context, eventStore, formats, []),
            new LibraryWriteGate(),
            new FetchshelfSettings(),
            new QBittorrentClientFactory(new NoHttpClients()),
            NullLogger<BulkActionRunner>.Instance);
        var operation = new BulkOperation(1, BulkAction.Refresh, ["edited", "plain"], category: null, DateTimeOffset.UtcNow);

        await runner.RunAsync(operation, CancellationToken.None);

        Assert.Equal(["plain"], lookup.Hashes);
        Assert.Equal($"Edited: {BulkActionRunner.EditedByHandSkipped}", Assert.Single(operation.Problems));
        Assert.Equal("Refreshed 1 of 2 torrents.", operation.Summary);
        context.ChangeTracker.Clear();
        Assert.Equal("Edited", (await context.Torrents.SingleAsync(t => t.Id == "edited")).Title);
    }
}
