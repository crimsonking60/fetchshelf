using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data.Organizer;

/// <summary>
/// How good a library copy's format is: the rank of the format the Link job chose for it in the format preference
/// list it chose it from, its library's own or the global one (lower is better). The Clean job keeps the best copy of a book by it, and a copy "could be upgraded" when its
/// rank is above the best, so the two never disagree.
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
        var library = torrent.LinkerName is { } name ? preferenceLists.Libraries.GetValueOrDefault(name) : null;
        if (torrent.ChosenAudioExtension is not null)
        {
            if (library?.AudioTypes is { } libraryAudio)
            {
                return libraryAudio;
            }

            // The media type column saves reading the metadata; music and radio have lists of their own.
            var mediaType = Enum.TryParse<Core.Metadata.MediaType>(torrent.MediaType, out var parsed)
                ? parsed
                : TorrentMetadataSerializer.Deserialize(torrent.MetadataJson).MediaType;
            return preferenceLists.AudioSideFor(mediaType);
        }

        return torrent.ChosenEbookExtension is not null ? library?.EbookTypes ?? preferenceLists.EbookTypes : null;
    }
}
