using Fetchshelf.Clients.Covers;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Opds;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Organizer;

/// <summary>The identifiers cover services find a book by, read from its metadata.</summary>
internal static class CoverIdentifiers
{
    /// <summary>The book's ISBN-13 (or, failing that, ISBN-10), from its ISBN field; MAM's ISBN field may hold an ASIN.</summary>
    public static string? Isbn(TorrentEntity torrent)
    {
        var keys = BookIdentifiers.Keys(TorrentMetadataSerializer.Deserialize(torrent.MetadataJson).Isbn).ToList();
        return keys.FirstOrDefault(key => key.Length == 13) ?? keys.FirstOrDefault(key => key.Length == 10 && !key.StartsWith('B'));
    }

    /// <summary>The audiobook's ASIN, from its ASIN field or its ISBN field; null for anything but an audiobook.</summary>
    public static string? AudiobookAsin(TorrentEntity torrent)
    {
        var metadata = TorrentMetadataSerializer.Deserialize(torrent.MetadataJson);
        if (metadata.MediaType is not (MediaType.Audiobook or MediaType.PeriodicalAudiobook))
        {
            return null;
        }

        return BookIdentifiers.Keys(metadata.Asin).Concat(BookIdentifiers.Keys(metadata.Isbn))
            .FirstOrDefault(key => key.Length == 10 && key.StartsWith('B'));
    }

    /// <summary>The image as a cover, when it is a JPEG, PNG or WebP image.</summary>
    public static CoverImage? AsCover(byte[]? bytes, string source) =>
        bytes is not null && BookCoverExtractor.SniffImageType(bytes) is { } contentType ? new CoverImage(bytes, contentType, source) : null;
}

/// <summary>Open Library's cover of a book with an ISBN. Only the ISBN is sent.</summary>
public sealed class OpenLibraryCoverSource(OpenLibraryClient client) : ICoverSource
{
    public const string Name = "Open Library";

    public bool CanFind(TorrentEntity torrent) => CoverIdentifiers.Isbn(torrent) is not null;

    public async Task<CoverImage?> FetchAsync(TorrentEntity torrent, CancellationToken cancellationToken = default)
    {
        if (CoverIdentifiers.Isbn(torrent) is not { } isbn)
        {
            return null;
        }

        try
        {
            return CoverIdentifiers.AsCover(await client.GetCoverByIsbnAsync(isbn, cancellationToken), Name);
        }
        catch (CoverServiceRefusedException ex)
        {
            throw new CoverSourceRefusedException(ex.Message);
        }
    }
}

/// <summary>Audible's cover of an audiobook with an ASIN, through Audnexus. Only the ASIN is sent.</summary>
public sealed class AudnexusCoverSource(AudnexusClient client) : ICoverSource
{
    public const string Name = "Audnexus";

    public bool CanFind(TorrentEntity torrent) => CoverIdentifiers.AudiobookAsin(torrent) is not null;

    public async Task<CoverImage?> FetchAsync(TorrentEntity torrent, CancellationToken cancellationToken = default)
    {
        if (CoverIdentifiers.AudiobookAsin(torrent) is not { } asin)
        {
            return null;
        }

        try
        {
            return CoverIdentifiers.AsCover(await client.GetCoverByAsinAsync(asin, cancellationToken), Name);
        }
        catch (CoverServiceRefusedException ex)
        {
            throw new CoverSourceRefusedException(ex.Message);
        }
    }
}

/// <summary>
/// The cover sources in order, the first cover found winning. A source that refuses is left out for the rest of the run
/// (one instance per Link run), while the others go on: a book only a refused source could look for waits for a later run
/// (<see cref="CoverSourceDeferredException"/>), and when every source has refused, fetching ends for the run.
/// </summary>
public sealed class ChainedCoverSource(IReadOnlyList<ICoverSource> sources, ILogger<ChainedCoverSource>? logger = null) : ICoverSource
{
    private readonly ILogger<ChainedCoverSource> _logger = logger ?? NullLogger<ChainedCoverSource>.Instance;
    private readonly HashSet<ICoverSource> _refused = [];

    public bool CanFind(TorrentEntity torrent) => sources.Any(source => source.CanFind(torrent));

    public async Task<CoverImage?> FetchAsync(TorrentEntity torrent, CancellationToken cancellationToken = default)
    {
        var skipped = false;
        foreach (var source in sources.Where(source => source.CanFind(torrent)))
        {
            if (_refused.Contains(source))
            {
                skipped = true;
                continue;
            }

            try
            {
                if (await source.FetchAsync(torrent, cancellationToken) is { } cover)
                {
                    return cover;
                }
            }
            catch (CoverSourceRefusedException ex)
            {
                _refused.Add(source);
                _logger.LogWarning("Covers: {Error} Nothing more is asked of it in this run", ex.Message);
                skipped = true;
            }
        }

        if (_refused.Count == sources.Count)
        {
            throw new CoverSourceRefusedException("Every cover source refused.");
        }

        return skipped ? throw new CoverSourceDeferredException("A cover source that could find it refused in this run.") : null;
    }
}
