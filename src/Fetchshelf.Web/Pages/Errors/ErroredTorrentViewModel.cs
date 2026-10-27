using System.Text.Json;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Pages.Errors;

/// <summary>Display model for one row on the Errors page, Fetchshelf-spec.md §10.4.</summary>
public sealed record ErroredTorrentViewModel(
    int Id,
    string Job,
    string? Title,
    string KeyLabel,
    string Key,
    string? TorrentDetailId,
    string ErrorText,
    DateTimeOffset OccurredAt,
    bool CanRestore)
{
    /// <param name="trackedTitles">Title per info-hash for the Link/Clean errors whose torrent has a Torrent record.</param>
    public static ErroredTorrentViewModel From(ErroredTorrentEntity entity, IReadOnlyDictionary<string, string> trackedTitles)
    {
        var isDownload = entity.Stage == ErrorStage.Grabber;
        var trackedTitle = isDownload ? null : trackedTitles.GetValueOrDefault(entity.Key);
        var restorePayload = isDownload && entity.DisplayMetadataJson is not null
            ? JsonSerializer.Deserialize<GrabberErrorRestorePayload>(entity.DisplayMetadataJson)
            : null;

        return new ErroredTorrentViewModel(
            entity.Id,
            JobName(entity.Stage),
            entity.Title ?? trackedTitle ?? (restorePayload is null ? null : TorrentMetadataSerializer.Deserialize(restorePayload.MetadataJson).Title),
            isDownload ? "MaM id" : "Info-hash",
            entity.Key,
            trackedTitle is null ? null : entity.Key,
            entity.ErrorText,
            entity.OccurredAt,
            CanRestore: restorePayload is not null);
    }

    /// <summary>The Jobs page name of the job that records errors for a stage.</summary>
    private static string JobName(ErrorStage stage) => stage switch
    {
        ErrorStage.Grabber => "Download",
        ErrorStage.Linker => "Link",
        ErrorStage.Cleaner => "Clean",
        _ => stage.ToString(),
    };
}
