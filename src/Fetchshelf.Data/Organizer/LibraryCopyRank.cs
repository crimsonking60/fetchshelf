using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data.Organizer;

/// <summary>
/// How good a library copy's format is: the rank of the format the Link job chose for it in the format preference
/// list it chose it from, its library's own or the global one (lower is better). The Clean job keeps the best copy of a
/// book by it, and a copy "could be upgraded" when its rank is above the best. The Upgrade job puts the better copy in
/// the old copy's library when it can, so the two are ranked with the same list and the Clean job keeps the better one.
/// </summary>
public static class LibraryCopyRank
{
    /// <summary>
    /// The audio format's rank when one was chosen, else the ebook format's; <see cref="FormatRanker.NotPresent"/>
    /// without either. Selection ranks a library copy by this too, so what the Upgrade job finds better, selection does.
    /// </summary>
    public static int Of(TorrentEntity torrent, FormatPreferenceLists preferenceLists) =>
        PreferenceList(torrent, preferenceLists) is { } list
            ? FormatRanker.Rank(list, [ChosenFormat(torrent)!])
            : FormatRanker.NotPresent;

    /// <summary>
    /// How to rank copies of one book against each other, for the Clean job's pick of the best. Copies whose formats were
    /// chosen from one list are ranked by <see cref="Of"/>. Copies in libraries with different lists of their own are all
    /// ranked with the global list instead, as a rank in one list says nothing against a rank in another.
    /// </summary>
    public static Func<TorrentEntity, int> RankerFor(IReadOnlyList<TorrentEntity> copies, FormatPreferenceLists preferenceLists)
    {
        var lists = copies.Select(c => PreferenceList(c, preferenceLists)).OfType<IReadOnlyList<string>>().ToList();
        if (lists.Skip(1).All(list => list.SequenceEqual(lists[0], StringComparer.OrdinalIgnoreCase)))
        {
            return copy => Of(copy, preferenceLists);
        }

        return copy => GlobalPreferenceList(copy, preferenceLists) is { } list
            ? FormatRanker.Rank(list, [ChosenFormat(copy)!])
            : FormatRanker.NotPresent;
    }

    /// <summary>The format the Link job chose: the audio one when there is one, else the ebook one.</summary>
    public static string? ChosenFormat(TorrentEntity torrent) => torrent.ChosenAudioExtension ?? torrent.ChosenEbookExtension;

    /// <summary>The best format in the copy's preference list, such as "m4b"; null when it has no chosen format.</summary>
    public static string? BestFormat(TorrentEntity torrent, FormatPreferenceLists preferenceLists) =>
        PreferenceList(torrent, preferenceLists)?.FirstOrDefault();

    /// <summary>
    /// Whether a better format is possible for a copy in the library: linked, not replaced, and with a chosen format
    /// that isn't the first in its list (or isn't in it at all). Metadata-only and unlinked copies never are.
    /// </summary>
    public static bool CouldBeUpgraded(TorrentEntity torrent, FormatPreferenceLists preferenceLists) =>
        torrent.LibraryPath is not null
        && torrent.ReplacedWithTorrentId is null
        && ChosenFormat(torrent) is not null
        && Of(torrent, preferenceLists) > 0;

    /// <summary>
    /// The list the copy's format was chosen from, as the Link job chose it: the library's own `audio_types` or
    /// `ebook_types` when it sets one, else the global list for its media type.
    /// </summary>
    public static IReadOnlyList<string>? PreferenceList(TorrentEntity torrent, FormatPreferenceLists preferenceLists)
    {
        if (ChosenFormat(torrent) is null)
        {
            return null;
        }

        var library = torrent.LinkerName is { } name ? preferenceLists.Libraries.GetValueOrDefault(name) : null;
        return (torrent.ChosenAudioExtension is not null ? library?.AudioTypes : library?.EbookTypes)
            ?? GlobalPreferenceList(torrent, preferenceLists);
    }

    /// <summary>The global list for the copy's chosen format, leaving its library's own out; null without a chosen format.</summary>
    private static IReadOnlyList<string>? GlobalPreferenceList(TorrentEntity torrent, FormatPreferenceLists preferenceLists)
    {
        if (torrent.ChosenAudioExtension is not null)
        {
            // The media type column saves reading the metadata; music and radio have lists of their own.
            var mediaType = Enum.TryParse<Core.Metadata.MediaType>(torrent.MediaType, out var parsed)
                ? parsed
                : TorrentMetadataSerializer.Deserialize(torrent.MetadataJson).MediaType;
            return preferenceLists.AudioSideFor(mediaType);
        }

        return torrent.ChosenEbookExtension is not null ? preferenceLists.EbookTypes : null;
    }
}
