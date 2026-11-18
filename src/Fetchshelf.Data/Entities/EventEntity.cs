namespace Fetchshelf.Data.Entities;

/// <summary>The event payload shapes. Stored as numbers, so new kinds go at the end.</summary>
public enum EventKind
{
    Grabbed,
    Linked,
    Cleaned,
    Updated,
    RemovedFromMam,
    Unlinked,
    BonusPurchase,

    /// <summary>A folder or file the Library check found unknown was deleted from a library; its path is in <see cref="EventEntity.LibraryPath"/>.</summary>
    DeletedFromLibrary,
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

    /// <summary>For Grabbed: the `name` of the `[[qbittorrent]]` it was added to, when there are several.</summary>
    public string? ClientName { get; set; }

    // Linked / Cleaned / Unlinked
    public string? LinkerName { get; set; }

    public string? LibraryPath { get; set; }

    public string? FilesRemovedJson { get; set; }

    // Updated: list of (field name, from, to)
    public string? FieldChangesJson { get; set; }

    /// <summary>
    /// For Updated: true when the details were edited by hand on the torrent's Edit page; null when they came from MAM,
    /// and for events written before hand edits were recorded.
    /// </summary>
    public bool? EditedByHand { get; set; }

    // RemovedFromMam carries no payload beyond Kind/TorrentId/MamId/OccurredAt.

    // BonusPurchase: what bonus points bought, with no torrent.

    /// <summary>For BonusPurchase: what was bought, a <c>BonusPurchaseKind</c> name: Wedge, Vip or Upload.</summary>
    public string? BonusSpend { get; set; }

    /// <summary>For an upload credit BonusPurchase: how many GB.</summary>
    public int? BonusUploadGb { get; set; }

    /// <summary>For BonusPurchase: the points before the purchase.</summary>
    public double? PointsBefore { get; set; }

    /// <summary>For BonusPurchase: the points MAM said were left after it.</summary>
    public double? PointsAfter { get; set; }
}
