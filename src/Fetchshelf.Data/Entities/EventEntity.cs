namespace Fetchshelf.Data.Entities;

/// <summary>The five event payload shapes.</summary>
public enum EventKind
{
    Grabbed,
    Linked,
    Cleaned,
    Updated,
    RemovedFromMam,
}

/// <summary>
/// An immutable audit entry. One entity with a <see cref="Kind"/> discriminator and nullable per-kind payload
/// fields rather than EF Core table-per-hierarchy inheritance: the kinds differ only in which fields they fill in,
/// never in behavior.
/// </summary>
public sealed class EventEntity
{
    public int Id { get; set; }

    public required DateTimeOffset OccurredAt { get; set; }

    public string? TorrentId { get; set; }

    public long? MamId { get; set; }

    public required EventKind Kind { get; set; }

    // Grabbed
    public string? GrabberName { get; set; }

    public string? Cost { get; set; }

    public bool? WedgeUsed { get; set; }

    // Linked / Cleaned
    public string? LinkerName { get; set; }

    public string? LibraryPath { get; set; }

    public string? FilesRemovedJson { get; set; }

    // Updated: list of (field name, from, to)
    public string? FieldChangesJson { get; set; }

    // RemovedFromMam carries no payload beyond Kind/TorrentId/MamId/OccurredAt.
}
