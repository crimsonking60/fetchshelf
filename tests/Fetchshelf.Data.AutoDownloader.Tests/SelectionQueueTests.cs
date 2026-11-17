using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data.AutoDownloader.Tests;

/// <summary>The queries over selections that the Download job, the runners and the Account page share.</summary>
public sealed class SelectionQueueTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-selection-queue-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static SelectedTorrentEntity Selection(
        long mamId, SelectedTorrentCost cost, long sizeBytes = 100, bool started = true, bool withWedge = false, bool removed = false,
        string? grabberName = "grabber") => new()
        {
            MamId = mamId,
            DownloadLink = $"dl{mamId}",
            CostDecision = cost.ToString(),
            MetadataJson = TorrentMetadataSerializer.Serialize(MamTorrentMapper.Map(SelectionTestSupport.BuildRecord(mamId: mamId, title: $"Book {mamId}", authorName: "Someone")) with { TotalSizeBytes = sizeBytes }),
            NormalizedTitleKey = $"book {mamId}",
            GrabberName = grabberName,
            CreatedAt = DateTimeOffset.UtcNow,
            StartedAt = started ? DateTimeOffset.UtcNow : null,
            StartedWithWedge = withWedge,
            RemovedAt = removed ? DateTimeOffset.UtcNow : null,
        };

    [Fact]
    public async Task InFlightBytes_CountsOnlyWhatDownloadsOnRatio()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.AddRange(
            Selection(1, SelectedTorrentCost.Ratio, 100),
            Selection(2, SelectedTorrentCost.TryWedge, 800),
            Selection(3, SelectedTorrentCost.UseWedge, 200, withWedge: true),
            Selection(4, SelectedTorrentCost.TryWedge, 400, withWedge: true),
            Selection(5, SelectedTorrentCost.GlobalFreeleech, 1_600),
            Selection(6, SelectedTorrentCost.Ratio, 3_200, removed: true),
            Selection(7, SelectedTorrentCost.Ratio, 6_400, started: false),
            // Started before StartedWithWedge was kept, so marked false: a wedge cost never downloads on ratio anyway.
            Selection(8, SelectedTorrentCost.UseWedge, 12_800));
        await context.SaveChangesAsync();

        Assert.Equal(900, await SelectionQueue.InFlightBytesAsync(context));
    }

    [Fact]
    public async Task ActiveForGrabber_CountsWaitingSelections_ByNameOrPrefix()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        context.SelectedTorrents.AddRange(
            Selection(1, SelectedTorrentCost.Ratio, started: false, grabberName: "Want to Read"),
            Selection(2, SelectedTorrentCost.Ratio, started: false, grabberName: "Want to Read"),
            Selection(3, SelectedTorrentCost.Ratio, started: true, grabberName: "Want to Read"),
            Selection(4, SelectedTorrentCost.Ratio, started: false, removed: true, grabberName: "Want to Read"),
            Selection(5, SelectedTorrentCost.Ratio, started: false, grabberName: "Follow: Discworld"),
            Selection(6, SelectedTorrentCost.Ratio, started: false, grabberName: "Follow: The Expanse"),
            Selection(7, SelectedTorrentCost.Ratio, started: false, grabberName: null));
        await context.SaveChangesAsync();

        Assert.Equal(2, await SelectionQueue.ActiveForGrabberAsync(context, "Want to Read"));
        Assert.Equal(2, await SelectionQueue.ActiveForGrabberAsync(context, FollowRunner.GrabberPrefix, isPrefix: true));
        Assert.Equal(0, await SelectionQueue.ActiveForGrabberAsync(context, "Follow: "));
        Assert.Equal(0, await SelectionQueue.ActiveForGrabberAsync(context, "Upgrade"));
    }
}
