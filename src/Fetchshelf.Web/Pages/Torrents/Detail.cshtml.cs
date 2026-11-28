using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Torrents;

public class DetailModel(
    FetchshelfDbContext dbContext, LinkRunner linkRunner, Cleaner cleaner, LibraryWriteGate writeGate,
    FetchshelfSettings settings, QBittorrentClientFactory qbittorrentFactory, MamClientOptions mamOptions,
    Core.AutoDownloader.FormatPreferenceLists formatPreferenceLists, BulkOperationQueue bulkOperations, ILogger<DetailModel> logger) : PageModel
{
    /// <summary>"mp3; m4b preferred" when a better format of this copy is possible; null otherwise.</summary>
    public string? UpgradeNote { get; private set; }

    public TorrentDetailViewModel? Torrent { get; private set; }

    /// <summary>
    /// How the torrent looks against MAM's seeding requirement: from qBittorrent's seeding time now when qBittorrent has
    /// it, else as the Seeding job last found it. Null without a `[seeding]` section, or when it was never estimated.
    /// </summary>
    public SeedingEstimate? Seeding { get; private set; }

    /// <summary>A torrent with this many files or fewer shows them open; more, and the list starts folded.</summary>
    public const int ClientFilesShownOpen = 20;

    /// <summary>
    /// The torrent's files as qBittorrent has them, a collection book's being its collection's; null when qBittorrent
    /// doesn't have the torrent or can't be reached.
    /// </summary>
    public IReadOnlyList<QBittorrentFile>? ClientFiles { get; private set; }

    public SeedingRequirement? SeedingRequirement => settings.Seeding?.Requirement;

    /// <summary>Whether `genres = true`, which shows the book's main category and genres.</summary>
    public bool GenresOn => settings.Genres;

    /// <summary>
    /// The Unlink and Remove confirmations start with this when the torrent isn't satisfied yet and `warn_before_removal`
    /// is on: removing it from qBittorrent before then can make it a hit-and-run.
    /// </summary>
    public string? RemovalWarning =>
        settings.Seeding is { WarnBeforeRemoval: true } seeding && Seeding is { State: not SeedingState.Satisfied } estimate
            ? $"It has seeded {Math.Floor(estimate.Seeded.TotalHours)} of the {seeding.SeedHours} hours MAM requires; removing it from qBittorrent before then can make it a hit-and-run. "
            : null;

    /// <summary>Whether the details were edited by hand, which Refresh would replace with MAM's.</summary>
    public bool EditedByHand => Torrent is { } torrent && HandEdits.AreIn(torrent.Metadata);

    /// <summary>What is followed, by kind and key, for the follow buttons; empty without a `[follow]` section.</summary>
    public IReadOnlyDictionary<(Data.Entities.FollowKind, string), Following.FollowState> Followed { get; private set; } =
        new Dictionary<(Data.Entities.FollowKind, string), Following.FollowState>();

    /// <summary>For a book of a split collection: the collection's record, which qBittorrent and MAM know; null otherwise.</summary>
    public (string Id, string Title)? Collection { get; private set; }

    /// <summary>For a collection linked as its books: the books, in their order; empty otherwise.</summary>
    public IReadOnlyList<CollectionBookRow> Books { get; private set; } = [];

    /// <summary>
    /// Whether the page offers <b>Split into its books</b>: for an ebook-side torrent, whatever the configuration says,
    /// but not for one of a collection's books, whose relink is its collection's.
    /// </summary>
    public bool OffersSplit { get; private set; }

    /// <summary>Whether <b>Split into its books</b> starts ticked: the torrent's own choice, else its library's `split_collections`.</summary>
    public bool Splits { get; private set; }

    /// <summary>Whether this is a book of a split collection, whose qBittorrent actions are its collection's.</summary>
    public bool IsBook => Torrent is { } torrent && CollectionBookId.IsBook(torrent.Id);

    /// <summary>
    /// This torrent's <b>Refresh and relink</b> running, waiting or ended, for the panel above the actions: queued for
    /// <see cref="BulkOperationWorker"/>, so a collection of many books doesn't keep the request waiting.
    /// </summary>
    public BulkOperationsViewModel BulkOperations => new(bulkOperations.ShownForTorrent(Id), "/Torrents/Detail", Id);

    /// <summary>Whether this torrent's Refresh and relink is waiting or running, so the button is disabled.</summary>
    public bool RefreshAndRelinkQueued => bulkOperations.ShownForTorrent(Id).Any(o => !o.HasEnded);

    private string Id => (string)RouteData.Values["id"]!;

    /// <summary>What the last action did, shown once after the redirect back to this page.</summary>
    [TempData]
    public string? ActionMessage { get; set; }

    [TempData]
    public bool ActionFailed { get; set; }

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var loaded = await LoadAsync(id);
        if (loaded is null)
        {
            return NotFound();
        }

        Torrent = loaded;
        if (!CollectionBookId.IsBook(id) && CollectionSplitter.IsEbookSide(loaded.Metadata.MediaType)
            && await dbContext.Torrents.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id) is { } record)
        {
            OffersSplit = true;
            Splits = record.SplitCollection ?? await LibrarySplitsAsync(record);
        }

        if (CollectionBookId.IsBook(id))
        {
            var collectionId = CollectionBookId.HashOf(id);
            Collection = await dbContext.Torrents.AsNoTracking().Where(t => t.Id == collectionId)
                .Select(t => new ValueTuple<string, string>(t.Id, t.Title)).FirstOrDefaultAsync();
        }
        else
        {
            Books = (await dbContext.Torrents.AsNoTracking().Where(t => t.CollectionTorrentId == id)
                    .Select(t => new CollectionBookRow(t.Id, t.Title, t.LibraryPath)).ToListAsync())
                .OrderBy(b => int.Parse(b.Id[(b.Id.LastIndexOf('-') + 1)..], System.Globalization.CultureInfo.InvariantCulture))
                .ToList();
        }

        if (settings.Follow is not null)
        {
            Followed = await Following.FollowButtonsViewModel.LoadFollowedAsync(dbContext, HttpContext.RequestAborted);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCleanAsync(string id)
    {
        // Like every action that places or removes a torrent's files: never alongside the Link or Clean job, or a bulk action.
        using var gate = await writeGate.AcquireAsync(HttpContext.RequestAborted);
        await cleaner.CleanOneAsync(id, QBittorrentConnections.Build(settings, qbittorrentFactory));
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRefreshAsync(string id)
    {
        var editedByHand = await dbContext.Torrents.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => t.MetadataJson)
            .FirstOrDefaultAsync() is { } json && HandEdits.AreIn(TorrentMetadataSerializer.Deserialize(json));
        try
        {
            var outcome = await linkRunner.RefreshAsync(id);
            ActionFailed = outcome == RefreshOutcome.NotFoundOnMam;
            ActionMessage = (outcome, editedByHand) switch
            {
                (RefreshOutcome.NotFoundOnMam, _) => "MAM did not find this torrent, so nothing was refreshed.",
                (_, true) => "Replaced the details you edited by hand with MAM's.",
                (RefreshOutcome.Changed, false) => "Refreshed from MAM; the changes are listed in the history below.",
                _ => "Refreshed from MAM; nothing had changed.",
            };
        }
        catch (Exception ex) when (!IsRequestAborted(ex))
        {
            logger.LogWarning(ex, "Refresh of {TorrentId} failed", id);
            ActionFailed = true;
            ActionMessage = $"Refresh failed: {MamErrorDescriber.DescribeAmong(ex, "MAM or Audiobookshelf")}";
        }

        return RedirectToPage(new { id });
    }

    /// <param name="split">Whether <b>Split into its books</b> was ticked.</param>
    /// <param name="splitShown">Whether the form offered it, so an unticked box (which sends nothing) means "don't split".</param>
    public async Task<IActionResult> OnPostRefreshAndRelinkAsync(string id, bool split = false, bool splitShown = false)
    {
        // The one waiting or running reads the choice it was queued with: nothing changes under it.
        if (RefreshAndRelinkQueued)
        {
            ActionFailed = true;
            ActionMessage = "Refresh and relink is already waiting or running; its progress is shown above.";
            return RedirectToPage(new { id });
        }

        if (splitShown && await dbContext.Torrents.FirstOrDefaultAsync(t => t.Id == id) is { } record)
        {
            // Stored only as an exception to the library, so a later change of the library's setting still reaches it.
            record.SplitCollection = split == await LibrarySplitsAsync(record) ? null : split;
            await dbContext.SaveChangesAsync();
        }

        // Run by BulkOperationWorker, under the write gate, while this page shows how far it has got. A book of a split
        // collection is relinked with its collection, the torrent qBittorrent has.
        bulkOperations.Enqueue(BulkAction.RefreshAndRelink, [CollectionBookId.HashOf(id)]);
        return RedirectToPage(new { id });
    }

    /// <summary>The panel of this torrent's Refresh and relink, which polls itself while it waits or runs.</summary>
    public PartialViewResult OnGetBulkOperations(string id) => Partial("_BulkOperationsPartial", BulkOperations);

    /// <summary><b>Cancel</b>: a Refresh and relink still waiting never starts; one running finishes the torrent.</summary>
    public IActionResult OnPostCancelBulk(string id, int operationId)
    {
        bulkOperations.Cancel(operationId);
        return BulkPanelOrPage(id);
    }

    /// <summary><b>Dismiss</b> on an ended Refresh and relink: how it went is no longer shown.</summary>
    public IActionResult OnPostDismissBulk(string id, int operationId)
    {
        bulkOperations.Dismiss(operationId);
        return BulkPanelOrPage(id);
    }

    private IActionResult BulkPanelOrPage(string id) =>
        Request.Headers.ContainsKey("HX-Request") ? OnGetBulkOperations(id) : RedirectToPage(new { id });

    public async Task<IActionResult> OnPostUnlinkAsync(string id)
    {
        using var gate = await writeGate.AcquireAsync(HttpContext.RequestAborted);
        try
        {
            var result = await cleaner.UnlinkAsync(id, QBittorrentConnections.Build(settings, qbittorrentFactory));
            ActionMessage = result.Message;
            ActionFailed = !result.Succeeded;
        }
        catch (Exception ex) when (!IsRequestAborted(ex))
        {
            logger.LogWarning(ex, "Unlink of {TorrentId} failed", id);
            ActionFailed = true;
            ActionMessage = $"Unlinking failed: {MamErrorDescriber.DescribeAmong(ex, "qBittorrent or Audiobookshelf")}";
        }

        return RedirectToPage(new { id });
    }

    /// <summary>True when the browser gave up on the request: nobody is left to show an error to, so let it propagate.</summary>
    private bool IsRequestAborted(Exception exception) =>
        exception is OperationCanceledException && HttpContext.RequestAborted.IsCancellationRequested;

    public async Task<IActionResult> OnPostRemoveRecordAsync(string id)
    {
        // A collection's books go with it: they are derived from it.
        var records = await dbContext.Torrents.Where(t => t.Id == id || t.CollectionTorrentId == id).ToListAsync();
        if (records.Count > 0)
        {
            dbContext.Torrents.RemoveRange(records);
            await dbContext.SaveChangesAsync();
        }

        return RedirectToPage("/Torrents/Index");
    }

    public async Task<IActionResult> OnPostClearReplacedAsync(string id)
    {
        var entity = await dbContext.Torrents.FindAsync(id);
        if (entity is not null)
        {
            entity.ReplacedWithTorrentId = null;
            entity.ReplacedAt = null;
            await dbContext.SaveChangesAsync();
        }

        return RedirectToPage(new { id });
    }

    /// <summary>
    /// Whether the torrent's library splits collections (its `split_collections`, the global one unless it sets its own):
    /// the library that linked it, by name or by its folder or its books', else the library whose category it has. Else
    /// the global `split_collections`.
    /// </summary>
    private async Task<bool> LibrarySplitsAsync(Data.Entities.TorrentEntity record)
    {
        var path = record.LibraryPath
            ?? await dbContext.Torrents.AsNoTracking().Where(t => t.CollectionTorrentId == record.Id && t.LibraryPath != null)
                .Select(t => t.LibraryPath).FirstOrDefaultAsync();
        var library = settings.Library.FirstOrDefault(l => l.Name is not null && l.Name == record.LinkerName)
            ?? (path is not null && LibraryFolders.IndexContaining(settings.Library, path) is { } index ? settings.Library[index] : null)
            ?? settings.Library.FirstOrDefault(l => l.Category is not null && l.Category == record.QBittorrentCategory);
        return library?.SplitCollections ?? settings.SplitCollections;
    }

    /// <summary>
    /// A book of a split collection is in qBittorrent as its collection, so what is done to the torrent there is done on
    /// the collection's page, where it is clear that every book of it follows.
    /// </summary>
    private IActionResult? RefuseForBook(string id)
    {
        if (!CollectionBookId.IsBook(id))
        {
            return null;
        }

        ActionMessage = "This book is in qBittorrent as its collection's torrent, shared with the collection's other books. Do this on the collection's page.";
        ActionFailed = true;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostStartAsync(string id)
    {
        if (RefuseForBook(id) is { } refused)
        {
            return refused;
        }

        await WithOwningClientAsync(id, client => client.StartAsync(id));
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostStopAsync(string id)
    {
        if (RefuseForBook(id) is { } refused)
        {
            return refused;
        }

        await WithOwningClientAsync(id, client => client.StopAsync(id));
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveWithDataAsync(string id)
    {
        if (RefuseForBook(id) is { } refused)
        {
            return refused;
        }

        await WithOwningClientAsync(id, client => client.DeleteWithDataAsync(id));
        return RedirectToPage("/Torrents/Index");
    }

    /// <summary>
    /// Sets the torrent's qBittorrent category and tags: <paramref name="tags"/> are the current tags left ticked, and
    /// <paramref name="newTags"/> is a comma-separated list of tags to add.
    /// </summary>
    public async Task<IActionResult> OnPostSetCategoryAndTagsAsync(string id, string? category, List<string> tags, string? newTags)
    {
        if (RefuseForBook(id) is { } refused)
        {
            return refused;
        }

        category ??= string.Empty;
        // Only a library's category, one of extra_categories, or the one the torrent already has, which the dropdown also offers.
        var currentCategory = await dbContext.Torrents.Where(t => t.Id == id).Select(t => t.QBittorrentCategory).FirstOrDefaultAsync();
        if (!LibraryCategories.IsOffered(settings, category) && category != (currentCategory ?? string.Empty)
            && !await HasCategoryInClientAsync(id, category))
        {
            return BadRequest();
        }

        tags = tags
            .Concat((newTags ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        await WithOwningClientAsync(id, async client =>
        {
            await client.SetCategoryAsync(id, category);
            var current = await client.GetTorrentAsync(id);
            if (current is not null)
            {
                var toRemove = current.TagList.Except(tags, StringComparer.OrdinalIgnoreCase).ToList();
                var toAdd = tags.Except(current.TagList, StringComparer.OrdinalIgnoreCase).ToList();
                if (toRemove.Count > 0)
                {
                    await client.RemoveTagsAsync(id, toRemove);
                }

                if (toAdd.Count > 0)
                {
                    await client.AddTagsAsync(id, toAdd);
                }
            }
        });
        return RedirectToPage(new { id });
    }

    /// <summary>The categories the category dropdown offers: the libraries', then `extra_categories`.</summary>
    public IReadOnlyList<LibraryCategory> CategoryOptions => LibraryCategories.For(settings);

    private async Task<bool> HasCategoryInClientAsync(string hash, string category)
    {
        var found = false;
        await WithOwningClientAsync(hash, async client =>
            found = (await client.GetTorrentAsync(hash))?.Category == category);
        return found;
    }

    private async Task WithOwningClientAsync(string hash, Func<QBittorrentClient, Task> action)
    {
        var connections = QBittorrentConnections.Build(settings, qbittorrentFactory);
        var client = await QBittorrentConnectionResolver.ResolveAsync(connections.Select(c => c.Client).ToList(), hash);
        if (client is not null)
        {
            await action(client);
        }
    }

    private async Task<TorrentDetailViewModel?> LoadAsync(string id)
    {
        var entity = await dbContext.Torrents.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (entity is null)
        {
            return null;
        }

        UpgradeNote = Torrents.UpgradeNote.For(entity, formatPreferenceLists);

        // Sorted in memory: the SQLite EF Core provider cannot translate ORDER BY over a
        // DateTimeOffset column (see the same note in Pages/Torrents/Index.cshtml.cs).
        var matchedEvents = await dbContext.Events.AsNoTracking().Where(e => e.TorrentId == id).ToListAsync();
        var events = matchedEvents.OrderByDescending(e => e.OccurredAt).ToList();

        QBittorrentTorrent? clientState = null;
        try
        {
            var connections = QBittorrentConnections.Build(settings, qbittorrentFactory);
            var client = await QBittorrentConnectionResolver.ResolveAsync(connections.Select(c => c.Client).ToList(), id);
            if (client is not null)
            {
                clientState = await client.GetTorrentAsync(CollectionBookId.HashOf(id)); // a book shows its collection's torrent
                if (clientState is not null)
                {
                    ClientFiles = await client.ListFilesAsync(CollectionBookId.HashOf(id));
                }
            }
        }
        catch (HttpRequestException)
        {
            // qBittorrent unreachable: the detail page still shows the stored state.
        }

        Seeding = EstimateSeeding(entity, clientState);
        return TorrentDetailViewModel.From(entity, events, clientState, settings.Audiobookshelf?.Url, mamOptions.BaseUrl);
    }

    private SeedingEstimate? EstimateSeeding(TorrentEntity entity, QBittorrentTorrent? clientState)
    {
        if (settings.Seeding is not { } seeding)
        {
            return null;
        }

        var previous = Enum.TryParse<SeedingState>(entity.SeedingState, out var parsed) ? parsed : (SeedingState?)null;
        if (clientState is { IsFinished: true } live)
        {
            return SeedingEstimator.Estimate(
                TimeSpan.FromSeconds(live.SeedingTime), DateTimeOffset.FromUnixTimeSeconds(live.AddedOn), SeedingEstimator.IsSeeding(live.State),
                previous, DateTimeOffset.UtcNow, seeding.Requirement);
        }

        return previous is not null && entity.SeedingSince is { } since
            ? SeedingEstimator.Estimate(TimeSpan.FromSeconds(entity.SeedingSeconds ?? 0), since, seedingNow: false, previous, DateTimeOffset.UtcNow, seeding.Requirement)
            : null;
    }
}

/// <summary>One book of a split collection, as the collection's page lists it.</summary>
public sealed record CollectionBookRow(string Id, string Title, string? LibraryPath);
