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
    /// <param name="placedUnderOtherName">
    /// File names used instead of the planned ones, because a different file of that name was already there.
    /// </param>
    public static (string Action, string Detail) Describe(
        IReadOnlyCollection<LinkOutcome> outcomes, IReadOnlyCollection<string>? placedUnderOtherName = null)
    {
        var (action, detail) = DescribeOutcomes(outcomes);
        if (placedUnderOtherName is not { Count: > 0 })
        {
            return (action, detail);
        }

        var names = string.Join(", ", placedUnderOtherName.Select(name => $"\"{name}\""));
        var renamed = placedUnderOtherName.Count == 1
            ? $"1 file placed as {names}: a different file of that name was already there"
            : $"{placedUnderOtherName.Count} files placed as {names}: different files of those names were already there";
        return (action, detail.Length == 0 ? $" ({renamed})" : $"{detail[..^1]}; {renamed})");
    }

    private static (string Action, string Detail) DescribeOutcomes(IReadOnlyCollection<LinkOutcome> outcomes)
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
