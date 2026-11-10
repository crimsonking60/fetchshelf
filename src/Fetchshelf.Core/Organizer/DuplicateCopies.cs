using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Organizer;

/// <summary>Finds linked copies of the same book and decides which copy to keep.</summary>
public static class DuplicateCopies
{
    /// <summary>
    /// Whether two linked copies are the same book: the same normalized title, and <see cref="SameBookComparer"/>
    /// agrees. The comparer alone leaves titles out, since its other callers have already matched them.
    /// </summary>
    public static bool AreCopiesOfSameBook(TorrentMetadata a, TorrentMetadata b)
    {
        var title = TitleNormalizer.Normalize(a.Title);
        return title.Length > 0 && title == TitleNormalizer.Normalize(b.Title) && SameBookComparer.AreSameBook(a, b);
    }

    /// <summary>
    /// Groups copies of the same book (<see cref="AreCopiesOfSameBook"/>), in the order given. Each copy is compared
    /// with the first copy of each group, so a group never mixes two books that only resemble a third.
    /// </summary>
    /// <remarks>
    /// Only groups with the same normalized title can match, so each copy is compared with those alone: comparing
    /// it with every group would make a large library's Clean run quadratic.
    /// </remarks>
    public static List<List<T>> GroupBySameBook<T>(IReadOnlyList<T> copies, Func<T, TorrentMetadata> metadataOf)
    {
        var groups = new List<List<T>>();
        var groupsByTitle = new Dictionary<string, List<(TorrentMetadata First, List<T> Members)>>(StringComparer.Ordinal);
        foreach (var copy in copies)
        {
            var metadata = metadataOf(copy);
            var title = TitleNormalizer.Normalize(metadata.Title);
            if (title.Length == 0)
            {
                groups.Add([copy]); // a blank title is the same book as nothing
                continue;
            }

            if (!groupsByTitle.TryGetValue(title, out var sameTitle))
            {
                sameTitle = [];
                groupsByTitle[title] = sameTitle;
            }

            var group = sameTitle.FirstOrDefault(g => SameBookComparer.AreSameBook(g.First, metadata));
            if (group.Members is not null)
            {
                group.Members.Add(copy);
            }
            else
            {
                List<T> members = [copy];
                sameTitle.Add((metadata, members));
                groups.Add(members);
            }
        }

        return groups;
    }

    /// <summary>
    /// The copy to keep: the best format rank (lowest, see <see cref="FormatRanker"/>), and between copies that rank
    /// the same, the one with the most linked bytes. On a full tie the earlier copy is kept.
    /// </summary>
    public static T PickKeeper<T>(IReadOnlyList<T> copies, Func<T, int> formatRankOf, Func<T, long> linkedSizeOf) =>
        copies.OrderBy(formatRankOf).ThenByDescending(linkedSizeOf).First();
}
