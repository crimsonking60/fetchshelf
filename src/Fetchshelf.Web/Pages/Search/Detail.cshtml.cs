using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Search;

/// <summary>
/// Everything MAM returns for one torrent, looked up by its MAM id with the cover image and media info, plus where it
/// stands in Fetchshelf and qBittorrent. Opened from a title on the Search page.
/// </summary>
public class DetailModel(
    MamClient mamClient, MamClientOptions mamOptions, FetchshelfDbContext dbContext, QBittorrentPresenceCache qbittorrentPresence,
    ILogger<DetailModel> logger) : PageModel
{
    /// <summary>The search to go back to.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public long MamId { get; private set; }

    public MamTorrentRecord? Record { get; private set; }

    /// <summary>The record read the way Fetchshelf stores it; null when MAM sent reference data Fetchshelf doesn't know.</summary>
    public TorrentMetadata? Metadata { get; private set; }

    public string? Error { get; private set; }

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

    public async Task<IActionResult> OnGetAsync(long mamId)
    {
        await LoadAsync(mamId);
        return Page();
    }

    /// <summary>Only the details, for the dialog the Search page opens over its results.</summary>
    public async Task<IActionResult> OnGetPartialAsync(long mamId)
    {
        await LoadAsync(mamId);
        return Partial("_HitDetailsPartial", this);
    }

    private async Task LoadAsync(long mamId)
    {
        MamId = mamId;
        qbittorrentPresence.Warm();
        try
        {
            Record = (await mamClient.SearchAsync(new MamSearchQuery { Id = mamId, ReturnThumbnail = true, ReturnMediaInfo = true })).Data
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

        Description = MamHitDetails.DescriptionText(Record.Description);
        // Fetched by Fetchshelf with the session, not by the browser, which may not be logged in to MAM.
        var cover = MamHitDetails.ThumbnailUrl(Record.Thumbnail, mamOptions.BaseUrl);
        ThumbnailUrl = mamClient.IsMamAddress(cover) ? Url.Page("/Search/Detail", "Cover", new { mamId, url = cover }) : null;
        MediaInfo = MamHitDetails.MediaInfoSections(Record.MediaInfoJson);
        RawFields = MamHitDetails.RawFields(Record.RawJson);

        var known = await dbContext.Torrents.AsNoTracking()
            .Where(t => t.MamId == mamId)
            .Select(t => new { t.Id, t.LibraryPath })
            .ToListAsync();
        TorrentId = known.Select(t => t.Id).FirstOrDefault();
        IsLinked = known.Any(t => t.LibraryPath is not null);
        IsSelected = await dbContext.SelectedTorrents.AnyAsync(s => s.MamId == mamId && s.RemovedAt == null && s.StartedAt == null);

        var presence = await qbittorrentPresence.GetAsync();
        if (presence.Loaded)
        {
            QBittorrentCategory = presence.CategoryOf(mamId, known.Select(t => t.Id));
            InQBittorrent = QBittorrentCategory is not null;
        }

    }

    /// <summary>The cover image, fetched from MAM. Only MAM's own addresses are fetched; see <see cref="MamClient.IsMamAddress"/>.</summary>
    public async Task<IActionResult> OnGetCoverAsync(string? url)
    {
        if (!mamClient.IsMamAddress(url))
        {
            return NotFound();
        }

        try
        {
            if (await mamClient.DownloadImageAsync(url!, HttpContext.RequestAborted) is { } image)
            {
                Response.Headers.CacheControl = "private, max-age=86400";
                return File(image.Bytes, image.ContentType);
            }
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && HttpContext.RequestAborted.IsCancellationRequested))
        {
            logger.LogInformation("Could not fetch the cover {Url} from MAM: {Error}", url, ex.Message);
        }

        return NotFound();
    }

    /// <summary>MAM's genres for the hit, as names: its category first, then the newer genre categories.</summary>
    public IReadOnlyList<string> Genres => Metadata is null ? [] : LibraryMetadataJsonBuilder.BuildGenres(Metadata);

    public string VipUntil => Record is not { IsVip: true } record
        ? "-"
        : record.VipExpireUnixSeconds == 0 ? "permanent" : DateTimeOffset.FromUnixTimeSeconds(record.VipExpireUnixSeconds).ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
}
