using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Organizer;

/// <summary>Finds linked copies of the same book and decides which copy to keep.</summary>
public static class DuplicateCopies
{
    /// <summary>
    /// Groups copies of the same book (<see cref="SameBookComparer"/>), in the order given. Each copy is compared with
    /// the first copy of each group, so a group never mixes two books that only resemble a third.
    /// </summary>
    public static List<List<T>> GroupBySameBook<T>(IReadOnlyList<T> copies, Func<T, TorrentMetadata> metadataOf)
    {
        var groups = new List<(TorrentMetadata First, List<T> Members)>();
        foreach (var copy in copies)
        {
            var metadata = metadataOf(copy);
            var group = groups.FirstOrDefault(g => SameBookComparer.AreSameBook(g.First, metadata));
            if (group.Members is not null)
            {
                group.Members.Add(copy);
            }
            else
            {
                groups.Add((metadata, [copy]));
            }
        }

        return groups.Select(g => g.Members).ToList();
    }

    /// <summary>
    /// The copy to keep: the best format rank (lowest, see <see cref="FormatRanker"/>), and between copies that rank
    /// the same, the one with the most linked bytes. On a full tie the earlier copy is kept.
    /// </summary>
    public static T PickKeeper<T>(IReadOnlyList<T> copies, Func<T, int> formatRankOf, Func<T, long> linkedSizeOf) =>
        copies.OrderBy(formatRankOf).ThenByDescending(linkedSizeOf).First();
}
