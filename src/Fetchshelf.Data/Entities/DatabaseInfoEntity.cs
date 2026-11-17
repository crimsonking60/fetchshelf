namespace Fetchshelf.Data.Entities;

/// <summary>
/// A fact about the database itself, such as the Fetchshelf version that last started on it. Kept in the database, so a
/// backup of it carries it along.
/// </summary>
public sealed class DatabaseInfoEntity
{
    public required string Key { get; set; }

    public required string Value { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}
