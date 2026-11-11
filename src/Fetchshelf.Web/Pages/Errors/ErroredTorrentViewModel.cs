using System.Text.Json;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Pages.Errors;

/// <summary>Display model for one row on the Errors page.</summary>
/// <param name="ListId">For a Hardcover list row, the list the book is on, for a link to it.</param>
public sealed record ErroredTorrentViewModel(
    int Id,
    string Job,
    string? Title,
    string KeyLabel,
    string Key,
    string? TorrentDetailId,
    string ErrorText,
    DateTimeOffset OccurredAt,
    bool CanRestore,
    bool CanEdit,
    string? ListId = null)
{
    /// <param name="entity">The error row.</param>
    /// <param name="trackedTitles">Title per info-hash for the errors keyed by info-hash whose torrent has a Torrent record.</param>
    /// <param name="torrentIdsByMamId">The stored torrent per MAM id, for the snatchlist errors of torrents Fetchshelf has.</param>
    public static ErroredTorrentViewModel From(
        ErroredTorrentEntity entity, IReadOnlyDictionary<string, string> trackedTitles, IReadOnlyDictionary<string, string> torrentIdsByMamId)
    {
        switch (entity.Stage)
        {
            case ErrorStage.Snatchlist:
                return Plain(entity, "MAM id", entity.Key, torrentIdsByMamId.GetValueOrDefault(entity.Key));
            case ErrorStage.HardcoverList:
                // The key is the list's id and the book's Hardcover id, joined with a space; list ids hold no spaces.
                var split = entity.Key.LastIndexOf(' ');
                return Plain(entity, "Hardcover id", split < 0 ? entity.Key : entity.Key[(split + 1)..], null) with
                {
                    ListId = split < 0 ? null : entity.Key[..split],
                };
            case ErrorStage.Follow:
                return Plain(entity, "Follow", entity.Title ?? entity.Key, null);
            case ErrorStage.Audiobookshelf:
                var title = trackedTitles.GetValueOrDefault(entity.Key);
                return Plain(entity, "Info-hash", entity.Key, title is null ? null : entity.Key) with { Title = entity.Title ?? title };
        }

        var isDownload = entity.Stage == ErrorStage.Grabber;
        var trackedTitle = isDownload ? null : trackedTitles.GetValueOrDefault(entity.Key);
        var restorePayload = isDownload && entity.DisplayMetadataJson is not null
            ? JsonSerializer.Deserialize<GrabberErrorRestorePayload>(entity.DisplayMetadataJson)
            : null;

        return new ErroredTorrentViewModel(
            entity.Id,
            JobName(entity.Stage),
            entity.Title ?? trackedTitle ?? (restorePayload is null ? null : TorrentMetadataSerializer.Deserialize(restorePayload.MetadataJson).Title),
            isDownload ? "MAM id" : "Info-hash",
            entity.Key,
            trackedTitle is null ? null : entity.Key,
            entity.ErrorText,
            entity.OccurredAt,
            CanRestore: restorePayload is not null,
            // A Link error that kept MAM's metadata is one for a torrent without a record: Edit creates the record.
            CanEdit: entity.Stage == ErrorStage.Linker && trackedTitle is null && entity.DisplayMetadataJson is not null);
    }

    /// <summary>A row of a job that tries every failed item again on its next run: no retry, no edit.</summary>
    private static ErroredTorrentViewModel Plain(ErroredTorrentEntity entity, string keyLabel, string key, string? torrentDetailId) =>
        new(entity.Id, JobName(entity.Stage), entity.Title, keyLabel, key, torrentDetailId, entity.ErrorText, entity.OccurredAt,
            CanRestore: false, CanEdit: false);

    /// <summary>What the Errors page calls a stage: the Jobs page name of the job, or its kind.</summary>
    public static string JobName(ErrorStage stage) => stage switch
    {
        ErrorStage.Grabber => "Download",
        ErrorStage.Linker => "Link",
        ErrorStage.Cleaner => "Clean",
        ErrorStage.Snatchlist => "Snatchlist",
        ErrorStage.HardcoverList => "Hardcover list",
        ErrorStage.Audiobookshelf => "Audiobookshelf",
        ErrorStage.Follow => "Follow",
        _ => stage.ToString(),
    };
}
