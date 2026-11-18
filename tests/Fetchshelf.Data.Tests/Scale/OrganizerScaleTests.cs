using System.Diagnostics;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Tests.Scale;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Fetchshelf.Data.Tests.Scale;

/// <summary>Times the Link and Clean jobs over a large library in steady state: everything already linked and cleaned.</summary>
[Trait("Category", "Scale")]
public sealed class OrganizerScaleTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-scale-tests-").FullName;

    private static readonly FormatPreferenceLists Formats = new()
    {
        AudioTypes = ["m4b", "m4a", "mp3"],
        EbookTypes = ["epub", "pdf"],
        MusicTypes = ["flac", "mp3", "pdf"],
        RadioTypes = ["mp3"],
    };

    private string DbPath => Path.Combine(_root, "scale.db");
    private string DownloadsDir => Path.Combine(_root, "downloads");
    private string AudiobookLibraryDir => Path.Combine(_root, "library", "audiobooks");
    private string EbookLibraryDir => Path.Combine(_root, "library", "ebooks");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private async Task<ScaleData> SeedAsync()
    {
        var data = ScaleSeeder.Build(ScaleFactAttribute.TorrentCount!.Value, DownloadsDir, AudiobookLibraryDir, EbookLibraryDir);
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await ScaleSeeder.SaveAsync(context, data);
        output.WriteLine(
            $"Seeded {data.Torrents.Count} torrents ({data.LinkedCount} linked), {data.Events.Count} events, "
            + $"{data.Errors.Count} linker errors, {data.ClientTorrents.Count} torrents in qBittorrent");
        return data;
    }

    private List<Library> Libraries() =>
    [
        new() { Name = ScaleSeeder.AudiobookLibraryName, LibraryDir = AudiobookLibraryDir, Category = ScaleSeeder.AudiobookCategory, Method = LibraryLinkMethod.Hardlink },
        new() { Name = ScaleSeeder.EbookLibraryName, LibraryDir = EbookLibraryDir, Category = ScaleSeeder.EbookCategory, Method = LibraryLinkMethod.Hardlink },
    ];

    [ScaleFact]
    public async Task LinkRun_EverythingAlreadyLinked()
    {
        var data = await SeedAsync();
        var client = OrganizerTestSupport.CreateQBittorrentClient(data.ClientTorrents, new Dictionary<string, List<QBittorrentFile>>());
        var lookup = new CountingMetadataLookup();
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var runner = new LinkRunner(context, new EventStore(context), lookup, Libraries(), Formats, new LinkRunnerOptions(ExcludeNarratorInLibraryDir: false));

        var stopwatch = Stopwatch.StartNew();
        var summary = await runner.RunAsync(new QBittorrentConnection(), client);
        stopwatch.Stop();

        output.WriteLine($"Link run: {stopwatch.Elapsed.TotalSeconds:F2} s, {lookup.Calls} MAM lookups, {summary.Linked} linked, {summary.Failed} failed");
        Assert.Equal(0, summary.Linked);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, lookup.Calls); // the seeded failed lookups are all less than a day old
        Assert.Equal(0, await context.Torrents.CountAsync(t => t.LibraryMismatchState != null));
    }

    /// <summary>A third of the torrents stopped since the last run, so their stage changed and must be written.</summary>
    [ScaleFact]
    public async Task LinkRun_AThirdStopped()
    {
        var data = await SeedAsync();
        var clientTorrents = data.ClientTorrents.Select((t, i) => i % 3 == 0 ? t with { State = "stoppedUP" } : t).ToList();
        var changed = clientTorrents.Count(t => t.State == "stoppedUP" && data.Torrents.Any(d => d.Id == t.Hash));
        var client = OrganizerTestSupport.CreateQBittorrentClient(clientTorrents, new Dictionary<string, List<QBittorrentFile>>());
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var runner = new LinkRunner(context, new EventStore(context), new CountingMetadataLookup(), Libraries(), Formats, new LinkRunnerOptions(ExcludeNarratorInLibraryDir: false));

        var stopwatch = Stopwatch.StartNew();
        await runner.RunAsync(new QBittorrentConnection(), client);
        stopwatch.Stop();

        output.WriteLine($"Link run with {changed} torrents stopped: {stopwatch.Elapsed.TotalSeconds:F2} s");
        Assert.Equal(changed, await context.Torrents.CountAsync(t => t.ClientStatus == "stoppedUP"));
    }

    [ScaleFact]
    public async Task CleanRun_NoNewDuplicates()
    {
        await SeedAsync();
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var cleaner = new Cleaner(context, new EventStore(context), Formats, []);

        var stopwatch = Stopwatch.StartNew();
        var summary = await cleaner.RunAsync([]);
        stopwatch.Stop();

        output.WriteLine($"Clean run: {stopwatch.Elapsed.TotalSeconds:F2} s, {summary.Retired} retired, {summary.Failed} failed");
        Assert.Equal(0, summary.Retired);
        Assert.Equal(0, summary.Failed);
    }

    [ScaleFact]
    public async Task LibraryCheck_EveryLinkedBookOnDisk_AndAFewUnknownFolders()
    {
        var data = await SeedAsync();
        // The seeded library laid out on disk: each linked book's folder with its files (empty, as only names count).
        foreach (var torrent in data.Torrents.Where(t => t.LibraryPath is not null))
        {
            Directory.CreateDirectory(torrent.LibraryPath!);
            foreach (var file in System.Text.Json.JsonSerializer.Deserialize<List<LinkedFileRecord>>(torrent.LinkedFilesJson!)!)
            {
                await File.WriteAllBytesAsync(Path.Combine(torrent.LibraryPath!, file.RelativePath), []);
            }
        }

        for (var i = 0; i < 5; i++)
        {
            var unknown = Path.Combine(AudiobookLibraryDir, "Left Behind", $"Old {i}");
            Directory.CreateDirectory(unknown);
            await File.WriteAllBytesAsync(Path.Combine(unknown, "book.m4b"), []);
        }

        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var checker = new LibraryChecker(context, Libraries(), new LibraryCheckResults());

        var stopwatch = Stopwatch.StartNew();
        var summary = await checker.RunAsync();
        stopwatch.Stop();

        output.WriteLine($"Library check: {stopwatch.Elapsed.TotalSeconds:F2} s, {summary}");
        Assert.Equal(new LibraryCheckSummary(UnknownFolders: 5, UnknownFiles: 0, FoldersMissing: 0, FilesMissing: 0), summary);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMinutes(1), $"The check took {stopwatch.Elapsed}.");
    }

    private sealed class CountingMetadataLookup : IMetadataLookup
    {
        private int _calls;

        public int Calls => _calls;

        public Task<TorrentMetadata?> TryLookupByInfoHashAsync(string infoHash, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<TorrentMetadata?>(null);
        }
    }
}
