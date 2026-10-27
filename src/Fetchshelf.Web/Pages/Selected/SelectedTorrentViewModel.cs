using System.Text.Json;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Pages.Selected;

/// <summary>Display model for one row on the Selected page, Fetchshelf-spec.md §10.4.</summary>
public sealed record SelectedTorrentViewModel(
    int Id,
    long MamId,
    string Title,
    string CostDecision,
    string? Category,
    IReadOnlyList<string> Tags,
    string? GrabberName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt)
{
    public static SelectedTorrentViewModel From(SelectedTorrentEntity entity)
    {
        var metadata = TorrentMetadataSerializer.Deserialize(entity.MetadataJson);
        var tags = entity.TagsJson is null ? [] : JsonSerializer.Deserialize<List<string>>(entity.TagsJson) ?? [];
        return new SelectedTorrentViewModel(
            entity.Id, entity.MamId, metadata.Title, entity.CostDecision, entity.Category, tags,
            entity.GrabberName, entity.CreatedAt, entity.StartedAt);
    }
}
