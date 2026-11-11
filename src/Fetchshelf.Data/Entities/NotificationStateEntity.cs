namespace Fetchshelf.Data.Entities;

/// <summary>
/// A value notifications need to remember between runs, such as the last hit-and-run count seen. A table of its own,
/// rather than a column elsewhere, so a later "notify when this changes" value has a place.
/// </summary>
public sealed class NotificationStateEntity
{
    public required string Key { get; set; }

    public required string Value { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}
