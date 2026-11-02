namespace Fetchshelf.Data.Organizer;

/// <summary>
/// Words for the log line about one torrent placed in a library, from what happened to each of its files:
/// "Copied" when every file was copied, "Placed" with a breakdown when the files went different ways.
/// </summary>
public static class LinkOutcomeDescription
{
    private static readonly (LinkOutcome Outcome, string Verb, string Count)[] Kinds =
    [
        (LinkOutcome.Hardlinked, "Hardlinked", "hardlinked"),
        (LinkOutcome.Copied, "Copied", "copied"),
        (LinkOutcome.Symlinked, "Symlinked", "symlinked"),
        (LinkOutcome.AlreadyExists, "Placed", "already there"),
    ];

    /// <summary>The verb that starts the line, and a detail (empty, or starting with a space) that ends it.</summary>
    public static (string Action, string Detail) Describe(IReadOnlyCollection<LinkOutcome> outcomes)
    {
        var present = Kinds.Where(k => outcomes.Contains(k.Outcome)).ToList();

        if (present.Count == 1 && present[0].Outcome != LinkOutcome.AlreadyExists)
        {
            return (present[0].Verb, string.Empty);
        }

        if (present.Count == 1)
        {
            return ("Placed", " (all files were already there)");
        }

        var counts = present.Select(k => $"{outcomes.Count(o => o == k.Outcome)} {k.Count}");
        return ("Placed", $" ({string.Join(", ", counts)})");
    }
}
