using Fetchshelf.Data.Configuration;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Organizer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Search;

/// <summary>
/// Everything MAM returns for one torrent, looked up by its MAM id with its media info, plus where it stands in Fetchshelf
/// and qBittorrent, and with `[search] covers` a cover from the cover sources. Opened from a title on the Search page.
/// </summary>
public class DetailModel(
    MamClient mamClient, MamClientOptions mamOptions, FetchshelfDbContext dbContext, QBittorrentPresenceCache qbittorrentPresence,
    MamPageGate pageGate, FetchshelfSettings settings, ILogger<DetailModel> logger, SearchCoverLookup? coverLookup = null) : PageModel
{
    /// <summary>The search to go back to.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>The Search page's language choice, kept by the links to more by an author, narrator or series.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Lang { get; set; }

    public long MamId { get; private set; }

    public MamTorrentRecord? Record { get; private set; }

    /// <summary>The record read the way Fetchshelf stores it; null when MAM sent reference data Fetchshelf doesn't know.</summary>
    public TorrentMetadata? Metadata { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Set when a job held the MAM gate past <see cref="MamPageGate.Wait"/>, so nothing was looked up; says which job.</summary>
    public string? Busy { get; private set; }

    /// <summary>True when the details are shown in the Search page's dialog rather than on a page of their own.</summary>
    public bool InDialog { get; private set; }

    public string? Description { get; private set; }

    public string? ThumbnailUrl { get; private set; }

    public IReadOnlyList<DetailSection> MediaInfo { get; private set; } = [];

    public IReadOnlyList<(string Name, string Value)> RawFields { get; private set; } = [];

    public string MamLink => $"{mamOptions.BaseUrl.TrimEnd('/')}/t/{MamId}";

    /// <summary>The torrent's page in Fetchshelf, when Fetchshelf has it.</summary>
    public string? TorrentId { get; private set; }

    public bool IsLinked { get; private set; }

    public bool IsSelected { get; private set; }

    /// <summary>True or false when qBittorrent could be asked, else null.</summary>
    public bool? InQBittorrent { get; private set; }

    public string? QBittorrentCategory { get; private set; }

    /// <summary>Whether the result's series and authors get Follow buttons: with `[follow]`, once MAM's answer was read.</summary>
    public bool ShowsFollow => settings.Follow is not null && Metadata is not null;

    /// <summary>Everything followed, for the Follow buttons.</summary>
    public IReadOnlyDictionary<(Data.Entities.FollowKind, string), Following.FollowState> Followed { get; private set; } =
        new Dictionary<(Data.Entities.FollowKind, string), Following.FollowState>();

    /// <summary>
    /// Where following one comes back to: in the Search page's dialog, the page that opened it (htmx names it), so its
    /// results stay; else this page.
    /// </summary>
    public string FollowReturnUrl =>
        InDialog && Uri.TryCreate(Request.Headers["HX-Current-URL"].ToString(), UriKind.Absolute, out var opener)
            && string.Equals(opener.Authority, Request.Host.Value, StringComparison.OrdinalIgnoreCase)
            ? opener.PathAndQuery
            : Url.Page("/Search/Detail", new { mamId = MamId, Q, Lang }) ?? "/";

    public async Task<IActionResult> OnGetAsync(long mamId)
    {
        await LoadAsync(mamId);
        return Page();
    }

    /// <summary>Only the details, for the dialog the Search page opens over its results.</summary>
    public async Task<IActionResult> OnGetPartialAsync(long mamId)
    {
        InDialog = true;
        await LoadAsync(mamId);
        return Partial("_HitDetailsPartial", this);
    }

    private async Task LoadAsync(long mamId)
    {
        MamId = mamId;
        qbittorrentPresence.Warm();
        try
        {
            using var gate = await pageGate.TryEnterAsync("Search page", HttpContext.RequestAborted);
            if (gate is null)
            {
                Busy = pageGate.BusyMessage;
                return;
            }

            Record = (await mamClient.SearchAsync(new MamSearchQuery { Id = mamId, ReturnMediaInfo = true })).Data
                .FirstOrDefault();
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && HttpContext.RequestAborted.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Looking up MAM torrent {MamId} failed", mamId);
            Error = MamErrorDescriber.Describe(ex, mamOptions.Timeout);
            return;
        }

        if (Record is null)
        {
            Error = $"MAM returned no torrent with id {mamId}.";
            return;
        }

        try
        {
            Metadata = MamTorrentMapper.Map(Record);
        }
        catch (UnrecognizedReferenceDataException ex)
        {
            logger.LogInformation("MAM torrent {MamId} could not be read fully: {Reason}", mamId, ex.Message);
        }

        Description = DescriptionText.From(Record.Description);
        // Never a cover from MAM, whose image server refuses Fetchshelf: with `[search] covers`, one from the cover sources,
        // which the browser asks for through the Cover handler, by what they find a book by.
        if (settings.Search.Covers && coverLookup is not null && Metadata is not null
            && CoverQuery.For(mamId, Metadata) is var book && coverLookup.CanFind(book))
        {
            ThumbnailUrl = Url.Page("/Search/Detail", "Cover", new
            {
                mamId,
                title = book.Title,
                authors = string.Join('|', book.Authors.Take(3)),
                isbn = book.Isbn,
                asin = book.AudiobookAsin,
            });
        }

        MediaInfo = MamHitDetails.MediaInfoSections(Record.MediaInfoJson);
        RawFields = MamHitDetails.RawFields(Record.RawJson);

        var known = await dbContext.Torrents.AsNoTracking()
            .Where(t => t.MamId == mamId)
            .Select(t => new { t.Id, t.LibraryPath })
            .ToListAsync();
        TorrentId = known.Select(t => t.Id).FirstOrDefault();
        IsLinked = known.Any(t => t.LibraryPath is not null);
        IsSelected = await dbContext.SelectedTorrents.AnyAsync(s => s.MamId == mamId && s.RemovedAt == null && s.StartedAt == null);

        if (ShowsFollow)
        {
            Followed = await Following.FollowButtonsViewModel.LoadFollowedAsync(dbContext, HttpContext.RequestAborted);
        }

        var presence = await qbittorrentPresence.GetAsync();
        if (presence.Loaded)
        {
            QBittorrentCategory = presence.CategoryOf(mamId, known.Select(t => t.Id));
            InQBittorrent = QBittorrentCategory is not null;
        }
    }

    /// <summary>
    /// The result's cover, from the cover sources (Hardcover, Open Library, Audnexus) by the title, authors, ISBN and ASIN
    /// in the address, with `[search] covers` on. Doesn't take the MAM gate: nothing is asked of MAM.
    /// </summary>
    public async Task<IActionResult> OnGetCoverAsync(long mamId, string? title, string? authors, string? isbn, string? asin)
    {
        if (!settings.Search.Covers || coverLookup is null)
        {
            return NotFound();
        }

        var book = CoverQuery.Of(mamId, title ?? string.Empty, (authors ?? string.Empty).Split('|'), isbn, asin, audiobook: asin is not null);
        if (!coverLookup.CanFind(book) || await coverLookup.GetAsync(book, HttpContext.RequestAborted) is not { } cover)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=86400";
        return File(cover.Bytes, cover.ContentType);
    }

    /// <summary>Whether `genres = true`, which shows the hit's main category and genres by MAM's names.</summary>
    public bool GenresOn => settings.Genres;

    /// <summary>The hit's genres by MAM's names, A-Z, for <see cref="GenresOn"/>.</summary>
    public IReadOnlyList<string> GenreLabelList =>
        Metadata is null ? [] : Metadata.GenreCategories.Select(GenreLabels.Label).Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>MAM's genres for the hit, as names: its category first, then the newer genre categories.</summary>
    public IReadOnlyList<string> Genres => Metadata is null ? [] : LibraryMetadataJsonBuilder.BuildGenres(Metadata);

    public string VipUntil => Record is not { IsVip: true } record
        ? "-"
        : record.VipExpireUnixSeconds == 0 ? "permanent" : DateTimeOffset.FromUnixTimeSeconds(record.VipExpireUnixSeconds).ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
}
