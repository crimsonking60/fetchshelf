using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Lists;
using Fetchshelf.Core.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>
/// The `[[bookmarks_list]]`'s books: the torrents bookmarked on MAM, newest bookmark first, read through MAM's search
/// page by page under the MAM gate. Each book is one torrent, under its MAM id, and the importer gets that torrent with
/// it (<see cref="KnownTorrent"/>) instead of searching for the book. A torrent that is neither an audiobook nor an
/// ebook, or that can't be read, is left out.
/// </summary>
public sealed class MamBookmarksSource(
    MamClient mamClient,
    MamAccessGate accessGate,
    string listName,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    ILogger? logger = null) : IListBookSource
{
    private const int PerPage = 100;

    private static readonly TimeSpan PolitenessDelay = TimeSpan.FromMilliseconds(400);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((wait, ct) => Task.Delay(wait, ct));
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly Dictionary<string, MamTorrentRecord> _torrents = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<ListBook>> GetBooksAsync(CancellationToken cancellationToken = default)
    {
        _torrents.Clear();
        var books = new List<ListBook>();
        using var _ = await accessGate.AcquireAsync($"List {listName}", cancellationToken);
        for (var offset = 0L; ; offset += PerPage)
        {
            if (offset > 0)
            {
                await _delay(PolitenessDelay, cancellationToken);
            }

            var result = await mamClient.SearchAsync(
                new MamSearchQuery { Target = MamSearchTarget.Bookmarks, PageOffset = offset, PerPage = PerPage, ReturnDownloadLink = true },
                cancellationToken);
            foreach (var record in result.Data)
            {
                if (ToBook(record) is { } book && _torrents.TryAdd(book.ExternalId, record))
                {
                    books.Add(book);
                }
            }

            if (result.Data.Count < PerPage || offset + PerPage >= result.Found)
            {
                break;
            }
        }

        return books;
    }

    public MamTorrentRecord? KnownTorrent(string externalId) => _torrents.GetValueOrDefault(externalId);

    private ListBook? ToBook(MamTorrentRecord record)
    {
        if (GrabMatching.TryMap(record) is not { } metadata)
        {
            _logger.LogDebug("List {Name}: bookmarked MAM torrent {MamId} could not be read, left out", listName, record.MamId);
            return null;
        }

        if (FilterMainTypeMapper.FromMediaType(metadata.MediaType) is not (FilterMainType.Audio or FilterMainType.Ebook))
        {
            _logger.LogDebug("List {Name}: bookmarked MAM torrent {MamId} is neither an audiobook nor an ebook, left out", listName, record.MamId);
            return null;
        }

        var series = metadata.Series.FirstOrDefault();
        return new ListBook(record.MamId.ToString(System.Globalization.CultureInfo.InvariantCulture), metadata.Title, metadata.Authors)
        {
            SeriesName = series?.Name,
            SeriesNumber = series?.Entries.FirstOrDefault() is { } entry ? (double)entry.FirstNumber : null,
        };
    }
}
