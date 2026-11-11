using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public sealed class SelectionRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-selection-runner-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static SelectionRunner CreateRunner(FetchshelfDbContext context, IReadOnlyList<TagRule>? tagRules = null) =>
        new(context, new EventStore(context), tagRules ?? [], SelectionTestSupport.PreferenceLists);

    private static Task<SelectionOutcome> Process(
        SelectionRunner runner,
        Fetchshelf.Clients.Mam.MamTorrentRecord record,
        ConfiguredCost cost = ConfiguredCost.Free,
        string? category = null,
        string? grabberName = "test-grabber") =>
        runner.ProcessCandidateAsync(record, MamTorrentMapper.Map(record), [], cost, category, grabberName);

    [Fact]
    public async Task ProcessCandidateAsync_IgnoredMamId_ReturnsIgnored()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var record = SelectionTestSupport.BuildRecord(mamId: 7);
        var runner = CreateRunner(context);

        var outcome = await runner.ProcessCandidateAsync(
            record, MamTorrentMapper.Map(record), [7], ConfiguredCost.Free, null, null);

        Assert.Equal(SelectionOutcome.Ignored, outcome);
        Assert.Empty(context.SelectedTorrents);
    }

    [Fact]
    public async Task ProcessCandidateAsync_AlreadySelected_RefreshesInPlace()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var record = SelectionTestSupport.BuildRecord(mamId: 1, isVip: true);
        var metadata = MamTorrentMapper.Map(record);
        context.SelectedTorrents.Add(new SelectedTorrentEntity
        {
            MamId = 1,
            DownloadLink = "old",
            CostDecision = "Ratio",
            MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(metadata),
            NormalizedTitleKey = Fetchshelf.Core.Metadata.TitleNormalizer.Normalize(metadata.Title),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();

        var runner = CreateRunner(context);
        var outcome = await Process(runner, record);

        Assert.Equal(SelectionOutcome.RefreshedSelected, outcome);
        Assert.Single(context.SelectedTorrents);
    }

    [Fact]
    public async Task ProcessCandidateAsync_AlreadyInLibrary_RecordsUpdatedEventOnChange()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var oldRecord = SelectionTestSupport.BuildRecord(mamId: 1, title: "Old Title");
        var oldMetadata = MamTorrentMapper.Map(oldRecord);
        context.Torrents.Add(new TorrentEntity
        {
            Id = "hash1",
            MamId = 1,
            NormalizedTitleKey = Fetchshelf.Core.Metadata.TitleNormalizer.Normalize(oldMetadata.Title),
            Title = oldMetadata.Title,
            MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(oldMetadata),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();

        var newRecord = SelectionTestSupport.BuildRecord(mamId: 1, title: "New Title");
        var runner = CreateRunner(context);
        var outcome = await Process(runner, newRecord);

        Assert.Equal(SelectionOutcome.RefreshedLibrary, outcome);
        Assert.Single(context.Events);
        Assert.Equal("New Title", context.Torrents.Single().Title);
    }

    [Fact]
    public async Task ProcessCandidateAsync_MetadataOnlyAdd_NotInLibrary_CreatesEntry()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var record = SelectionTestSupport.BuildRecord(mamId: 1);
        var runner = CreateRunner(context);

        var outcome = await Process(runner, record, cost: ConfiguredCost.MetadataOnlyAdd);

        Assert.Equal(SelectionOutcome.MetadataOnlyCreated, outcome);
        var entry = Assert.Single(context.Torrents);
        Assert.Equal("mam:1", entry.Id);
    }

    [Fact]
    public async Task ProcessCandidateAsync_MetadataOnly_NotInLibrary_StopsWithoutCreating()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var record = SelectionTestSupport.BuildRecord(mamId: 1);
        var runner = CreateRunner(context);

        var outcome = await Process(runner, record, cost: ConfiguredCost.MetadataOnly);

        Assert.Equal(SelectionOutcome.MetadataOnlyStop, outcome);
        Assert.Empty(context.Torrents);
        Assert.Empty(context.SelectedTorrents);
    }

    [Fact]
    public async Task ProcessCandidateAsync_MetadataOnlyAdd_IgnoresBadFormat()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var record = SelectionTestSupport.BuildRecord(mamId: 1, fileTypes: ["exe"]);
        var runner = CreateRunner(context);

        var outcome = await Process(runner, record, cost: ConfiguredCost.MetadataOnlyAdd);

        Assert.Equal(SelectionOutcome.MetadataOnlyCreated, outcome);
    }

    [Fact]
    public async Task ProcessCandidateAsync_NoPreferredFormat_IsSkipped()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var record = SelectionTestSupport.BuildRecord(mamId: 1, fileTypes: ["exe"]);
        var runner = CreateRunner(context);

        var outcome = await Process(runner, record, cost: ConfiguredCost.Ratio);

        Assert.Equal(SelectionOutcome.NoPreferredFormat, outcome);
        Assert.Empty(context.SelectedTorrents);
    }

    [Fact]
    public async Task ProcessCandidateAsync_WorseDuplicateOfSelected_IsSkipped()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var existingRecord = SelectionTestSupport.BuildRecord(mamId: 1, fileTypes: ["m4b"]);
        var existingMetadata = MamTorrentMapper.Map(existingRecord);
        context.SelectedTorrents.Add(new SelectedTorrentEntity
        {
            MamId = 1,
            DownloadLink = "x",
            CostDecision = "Ratio",
            MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(existingMetadata),
            NormalizedTitleKey = Fetchshelf.Core.Metadata.TitleNormalizer.Normalize(existingMetadata.Title),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();

        // A second author/narrator with worse-ranked format (mp3 ranks worse than m4b).
        var candidateRecord = SelectionTestSupport.BuildRecord(mamId: 2, fileTypes: ["mp3"]);
        var runner = CreateRunner(context);

        var outcome = await Process(runner, candidateRecord, cost: ConfiguredCost.Ratio);

        Assert.Equal(SelectionOutcome.DuplicateOfSelected, outcome);
        Assert.Single(context.DuplicateTorrents);
        Assert.Single(context.SelectedTorrents.Where(s => s.RemovedAt == null));
    }

    [Fact]
    public async Task ProcessCandidateAsync_BetterCandidate_ReplacesSelectedAndProceeds()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var existingRecord = SelectionTestSupport.BuildRecord(mamId: 1, fileTypes: ["mp3"]);
        var existingMetadata = MamTorrentMapper.Map(existingRecord);
        context.SelectedTorrents.Add(new SelectedTorrentEntity
        {
            MamId = 1,
            DownloadLink = "x",
            CostDecision = "Ratio",
            MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(existingMetadata),
            NormalizedTitleKey = Fetchshelf.Core.Metadata.TitleNormalizer.Normalize(existingMetadata.Title),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();

        var candidateRecord = SelectionTestSupport.BuildRecord(mamId: 2, fileTypes: ["m4b"], isFree: true);
        var runner = CreateRunner(context);

        var outcome = await Process(runner, candidateRecord, cost: ConfiguredCost.Ratio);

        Assert.Equal(SelectionOutcome.Selected, outcome);
        Assert.Single(context.DuplicateTorrents);
        var activeSelections = context.SelectedTorrents.Where(s => s.RemovedAt == null).ToList();
        Assert.Single(activeSelections);
        Assert.Equal(2, activeSelections[0].MamId);
    }

    [Fact]
    public async Task ProcessCandidateAsync_DifferentRussianBookBySameAuthorInLibrary_IsSelected()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var libraryRecord = SelectionTestSupport.BuildRecord(mamId: 1, title: "Война и мир", authorName: "Лев Толстой", fileTypes: ["m4b"]);
        var libraryMetadata = MamTorrentMapper.Map(libraryRecord);
        context.Torrents.Add(new TorrentEntity
        {
            Id = "hash1",
            MamId = 1,
            NormalizedTitleKey = Fetchshelf.Core.Metadata.TitleNormalizer.Normalize(libraryMetadata.Title),
            Title = libraryMetadata.Title,
            MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(libraryMetadata),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();

        var candidateRecord = SelectionTestSupport.BuildRecord(mamId: 2, title: "Анна Каренина", authorName: "Лев Толстой", fileTypes: ["m4b"]);
        var outcome = await Process(CreateRunner(context), candidateRecord, cost: ConfiguredCost.Ratio);

        Assert.Equal(SelectionOutcome.Selected, outcome);
        Assert.Empty(context.DuplicateTorrents);
    }

    [Theory]
    [InlineData("linked", SelectionOutcome.SelectedAsUpgrade)]
    [InlineData("replaced", SelectionOutcome.Selected)]
    [InlineData("unlinked", SelectionOutcome.Selected)]
    [InlineData("not linked", SelectionOutcome.Selected)]
    public async Task ProcessCandidateAsync_BetterThanTheLibrarysCopy_IsSelectedAsAnUpgrade_OnlyOfACopyInTheLibrary(string state, SelectionOutcome expected)
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var libraryMetadata = MamTorrentMapper.Map(SelectionTestSupport.BuildRecord(mamId: 1, fileTypes: ["mp3"]));
        context.Torrents.Add(new TorrentEntity
        {
            Id = "hash1",
            MamId = 1,
            NormalizedTitleKey = Fetchshelf.Core.Metadata.TitleNormalizer.Normalize(libraryMetadata.Title),
            Title = libraryMetadata.Title,
            MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(libraryMetadata),
            CreatedAt = DateTimeOffset.UtcNow,
            LibraryPath = state is "linked" or "replaced" ? "/library/The Way of Kings" : null,
            UnlinkedAt = state == "unlinked" ? DateTimeOffset.UtcNow : null,
            ReplacedWithTorrentId = state == "replaced" ? "hash0" : null,
        });
        await context.SaveChangesAsync();

        var outcome = await Process(CreateRunner(context), SelectionTestSupport.BuildRecord(mamId: 2, fileTypes: ["m4b"]), cost: ConfiguredCost.Ratio);

        Assert.Equal(expected, outcome);
        Assert.True(outcome.IsSelected());
        Assert.Equal(2, Assert.Single(context.SelectedTorrents).MamId);
    }

    [Fact]
    public async Task ProcessCandidateAsync_DuplicateOfLibrary_IsSkipped()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var libraryRecord = SelectionTestSupport.BuildRecord(mamId: 1, fileTypes: ["m4b"]);
        var libraryMetadata = MamTorrentMapper.Map(libraryRecord);
        context.Torrents.Add(new TorrentEntity
        {
            Id = "hash1",
            MamId = 1,
            NormalizedTitleKey = Fetchshelf.Core.Metadata.TitleNormalizer.Normalize(libraryMetadata.Title),
            Title = libraryMetadata.Title,
            MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(libraryMetadata),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();

        var candidateRecord = SelectionTestSupport.BuildRecord(mamId: 2, fileTypes: ["mp3"]);
        var runner = CreateRunner(context);

        var outcome = await Process(runner, candidateRecord, cost: ConfiguredCost.Ratio);

        Assert.Equal(SelectionOutcome.DuplicateOfLibrary, outcome);
        Assert.Single(context.DuplicateTorrents);
        Assert.Empty(context.SelectedTorrents);
    }

    [Fact]
    public async Task ProcessCandidateAsync_CategoryAndTags_ResolvedFromTagRules()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var record = SelectionTestSupport.BuildRecord(mamId: 1, isFree: true);
        var tagRule = new TagRule { Filter = new TorrentFilter(), Category = "rule-category", Tags = ["fiction"] };
        var runner = CreateRunner(context, [tagRule]);

        var outcome = await Process(runner, record, cost: ConfiguredCost.Ratio);

        Assert.Equal(SelectionOutcome.Selected, outcome);
        var selected = context.SelectedTorrents.Single();
        Assert.Equal("rule-category", selected.Category);
        Assert.Contains("fiction", selected.TagsJson!);
    }

    [Fact]
    public async Task ProcessCandidateAsync_ConfiguredFreeAndNonFreeTorrent_RejectsIt()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var record = SelectionTestSupport.BuildRecord(mamId: 1, isVip: false, isPersonalFreeleech: false, isFree: false);
        var runner = CreateRunner(context);

        var outcome = await Process(runner, record, cost: ConfiguredCost.Free);

        Assert.Equal(SelectionOutcome.CostNotFree, outcome);
        Assert.Empty(context.SelectedTorrents);
    }

    private static Task<SelectionOutcome> DryRun(SelectionRunner runner, MamTorrentRecord record, ConfiguredCost cost = ConfiguredCost.Ratio) =>
        runner.ProcessCandidateAsync(record, MamTorrentMapper.Map(record), [], cost, null, "test-grabber", dryRun: true);

    private static async Task AddSelectedAsync(FetchshelfDbContext context, MamTorrentRecord record)
    {
        var metadata = MamTorrentMapper.Map(record);
        context.SelectedTorrents.Add(new SelectedTorrentEntity
        {
            MamId = record.MamId,
            DownloadLink = "x",
            CostDecision = "Ratio",
            MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(metadata),
            NormalizedTitleKey = Fetchshelf.Core.Metadata.TitleNormalizer.Normalize(metadata.Title),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task ProcessCandidateAsync_DryRunOfASelectableTorrent_ReturnsSelectedAndWritesNothing()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var runner = CreateRunner(context);

        var outcome = await DryRun(runner, SelectionTestSupport.BuildRecord(mamId: 1));

        Assert.Equal(SelectionOutcome.Selected, outcome);
        Assert.Empty(context.SelectedTorrents);
        Assert.Empty(context.DuplicateTorrents);
    }

    [Fact]
    public async Task ProcessCandidateAsync_DryRunOfANonFreeTorrentWithFreeCost_StillReportsCostNotFree()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var runner = CreateRunner(context);

        var outcome = await DryRun(runner, SelectionTestSupport.BuildRecord(mamId: 1), cost: ConfiguredCost.Free);

        Assert.Equal(SelectionOutcome.CostNotFree, outcome);
    }

    [Fact]
    public async Task ProcessCandidateAsync_DryRunOfAWorseDuplicate_RecordsNoDuplicate()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        await AddSelectedAsync(context, SelectionTestSupport.BuildRecord(mamId: 1, fileTypes: ["m4b"]));
        var runner = CreateRunner(context);

        var outcome = await DryRun(runner, SelectionTestSupport.BuildRecord(mamId: 2, fileTypes: ["mp3"]));

        Assert.Equal(SelectionOutcome.DuplicateOfSelected, outcome);
        Assert.Empty(context.DuplicateTorrents);
    }

    [Fact]
    public async Task ProcessCandidateAsync_DryRunOfABetterCandidate_LeavesTheExistingSelectionInPlace()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        await AddSelectedAsync(context, SelectionTestSupport.BuildRecord(mamId: 1, fileTypes: ["mp3"]));
        var runner = CreateRunner(context);

        var outcome = await DryRun(runner, SelectionTestSupport.BuildRecord(mamId: 2, fileTypes: ["m4b"]));

        Assert.Equal(SelectionOutcome.Selected, outcome);
        var selection = Assert.Single(context.SelectedTorrents.AsNoTracking());
        Assert.Equal(1, selection.MamId);
        Assert.Null(selection.RemovedAt);
        Assert.Empty(context.DuplicateTorrents);
    }

    [Fact]
    public async Task ProcessCandidateAsync_DryRunFindingTheSameBookTwiceInOneRun_ReportsTheSecondAsADuplicate()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var runner = CreateRunner(context);

        var first = await DryRun(runner, SelectionTestSupport.BuildRecord(mamId: 1, fileTypes: ["m4b"]));
        var second = await DryRun(runner, SelectionTestSupport.BuildRecord(mamId: 2, fileTypes: ["mp3"]));
        var sameIdAgain = await DryRun(runner, SelectionTestSupport.BuildRecord(mamId: 1, fileTypes: ["m4b"]));

        Assert.Equal(SelectionOutcome.Selected, first);
        Assert.Equal(SelectionOutcome.DuplicateOfSelected, second);
        Assert.Equal(SelectionOutcome.RefreshedSelected, sameIdAgain);
        Assert.Empty(context.SelectedTorrents);
    }

    [Fact]
    public async Task ProcessCandidateAsync_DryRunOfALibraryTorrentWithChangedMetadata_ChangesNothing()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var oldMetadata = MamTorrentMapper.Map(SelectionTestSupport.BuildRecord(mamId: 1, title: "Old Title"));
        context.Torrents.Add(new TorrentEntity
        {
            Id = "hash1",
            MamId = 1,
            NormalizedTitleKey = Fetchshelf.Core.Metadata.TitleNormalizer.Normalize(oldMetadata.Title),
            Title = oldMetadata.Title,
            MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(oldMetadata),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
        var runner = CreateRunner(context);

        var outcome = await DryRun(runner, SelectionTestSupport.BuildRecord(mamId: 1, title: "New Title"));

        Assert.Equal(SelectionOutcome.RefreshedLibrary, outcome);
        Assert.Equal("Old Title", context.Torrents.AsNoTracking().Single().Title);
        Assert.Empty(context.Events);
    }

    [Fact]
    public async Task ProcessCandidateAsync_DryRunWithMetadataOnlyAdd_CreatesNoEntry()
    {
        using var context = SelectionTestSupport.CreateContext(DbPath);
        var runner = CreateRunner(context);

        var outcome = await DryRun(runner, SelectionTestSupport.BuildRecord(mamId: 1), cost: ConfiguredCost.MetadataOnlyAdd);

        Assert.Equal(SelectionOutcome.MetadataOnlyCreated, outcome);
        Assert.Empty(context.Torrents);
    }
}
