using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Pages.Errors;

/// <summary>Display model for one row on the Errors page, Fetchshelf-spec.md §10.4.</summary>
public sealed record ErroredTorrentViewModel(int Id, ErrorStage Stage, string Key, string ErrorText, DateTimeOffset OccurredAt, bool CanRestore)
{
    public static ErroredTorrentViewModel From(ErroredTorrentEntity entity) => new(
        entity.Id, entity.Stage, entity.Key, entity.ErrorText, entity.OccurredAt,
        CanRestore: entity.Stage == ErrorStage.Grabber && entity.DisplayMetadataJson is not null);
}
