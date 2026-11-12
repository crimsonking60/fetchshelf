namespace Fetchshelf.Core.Metadata;

/// <summary>One changed field in an Updated event, with its old and new value as shown to the user.</summary>
public sealed record FieldChange(string Field, string From, string To);

/// <summary>The fields an Updated event records when a torrent's metadata changes.</summary>
public static class MetadataDiff
{
    /// <summary>Every field an Updated event can record, in the order <see cref="Between"/> records them.</summary>
    public static readonly IReadOnlyList<string> Fields = ["Title", "Authors", "Narrators", "Language", "LegacyCategory"];

    /// <summary>The recorded fields that differ, in a fixed order; empty when none does.</summary>
    public static List<FieldChange> Between(TorrentMetadata from, TorrentMetadata to)
    {
        var changes = new List<FieldChange>();
        AddIfChanged(changes, "Title", from.Title, to.Title);
        AddIfChanged(changes, "Authors", string.Join(", ", from.Authors), string.Join(", ", to.Authors));
        AddIfChanged(changes, "Narrators", string.Join(", ", from.Narrators), string.Join(", ", to.Narrators));
        AddIfChanged(changes, "Language", from.Language.ToString(), to.Language.ToString());
        AddIfChanged(changes, "LegacyCategory", from.LegacyCategory.ToString(), to.LegacyCategory.ToString());
        return changes;
    }

    private static void AddIfChanged(List<FieldChange> changes, string field, string from, string to)
    {
        if (!string.Equals(from, to, StringComparison.Ordinal))
        {
            changes.Add(new FieldChange(field, from, to));
        }
    }
}
