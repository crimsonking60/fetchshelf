namespace Fetchshelf.Data.Entities;

/// <summary>
/// A key/value entry in the persistent Config store, separate from the configuration file
/// (Fetchshelf-spec.md §3.2). The MaM session cookie is no longer kept here; it always comes from config.
/// </summary>
public sealed class ConfigStoreEntry
{
    public required string Key { get; set; }

    public required string Value { get; set; }
}
