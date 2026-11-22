using Fetchshelf.Clients.Covers;
using Fetchshelf.Clients.Hardcover;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Opds;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Organizer;

/// <summary>Helpers the cover sources share.</summary>
internal static class CoverImages
{
    /// <summary>The image as a cover, when it is a JPEG, PNG or WebP image.</summary>
    public static CoverImage? AsCover(byte[]? bytes, string source) =>
        bytes is not null && BookCoverExtractor.SniffImageType(bytes) is { } contentType ? new CoverImage(bytes, contentType, source) : null;
}

/// <summary>
/// Hardcover's cover of a book, with the `[hardcover]` token: first the edition with the book's ISBN (or, for an
/// audiobook, its ASIN), then a search by title and first author, taking a hit only when its title and one of its authors
/// compare equal to the book's by Fetchshelf's keys, so a namesake or a box set isn't taken. The image is downloaded
/// without the token, from Hardcover's image host only. Queries are spaced a second apart.
/// </summary>
public sealed class HardcoverCoverSource(HardcoverClient client, ImageClient images, RequestSpacer? spacer = null) : ICoverSource
{
    public const string Name = "Hardcover";

    /// <summary>The host Hardcover serves its images from; an address elsewhere isn't followed.</summary>
    private static readonly string[] ImageHosts = ["assets.hardcover.app"];

    private readonly RequestSpacer _spacer = spacer ?? new RequestSpacer(TimeSpan.FromSeconds(1));

    public bool CanFind(CoverQuery book) => book.Isbn is not null || book.AudiobookAsin is not null || (book.Title.Length > 0 && book.Authors.Count > 0);

    public async Task<CoverImage?> FetchAsync(CoverQuery book, CancellationToken cancellationToken = default)
    {
        try
        {
            string? address = null;
            if (book.Isbn is not null || book.AudiobookAsin is not null)
            {
                var isbns = BookIdentifiers.Keys(book.Isbn).ToList();
                await _spacer.WaitTurnAsync(cancellationToken);
                address = await client.GetEditionCoverAsync(
                    isbns.FirstOrDefault(key => key.Length == 13), isbns.FirstOrDefault(key => key.Length == 10), book.AudiobookAsin, cancellationToken);
            }

            if (address is null && book.Title.Length > 0 && book.Authors.Count > 0)
            {
                await _spacer.WaitTurnAsync(cancellationToken);
                var hits = await client.SearchBooksAsync($"{book.Title} {book.Authors[0]}", cancellationToken: cancellationToken);
                address = hits.FirstOrDefault(hit => hit.ImageUrl is not null && Matches(hit, book))?.ImageUrl;
            }

            return CoverImages.AsCover(await images.GetAsync(address, ImageHosts, "Hardcover's image host", cancellationToken), Name);
        }
        catch (HardcoverException ex)
        {
            throw new CoverSourceRefusedException($"Hardcover: {ex.Message}");
        }
        catch (CoverServiceRefusedException ex)
        {
            throw new CoverSourceRefusedException(ex.Message);
        }
    }

    /// <summary>
    /// Whether a search hit is the book: the same title key, and an author in common by name key, or by name key without
    /// middle initials ("Steve J McHugh" on MAM is "Steve McHugh" on Hardcover).
    /// </summary>
    public static bool Matches(HardcoverSearchHit hit, CoverQuery book)
    {
        if (TitleNormalizer.Normalize(hit.Title) != TitleNormalizer.Normalize(book.Title))
        {
            return false;
        }

        var authors = book.Authors.Select(PersonNameKey.Of).ToHashSet(StringComparer.Ordinal);
        var withoutInitials = book.Authors.Select(PersonNameKey.WithoutMiddleInitials).ToHashSet(StringComparer.Ordinal);
        return hit.Authors.Any(author =>
            authors.Contains(PersonNameKey.Of(author)) || withoutInitials.Contains(PersonNameKey.WithoutMiddleInitials(author)));
    }
}

/// <summary>Open Library's cover of a book with an ISBN. Only the ISBN is sent.</summary>
public sealed class OpenLibraryCoverSource(OpenLibraryClient client) : ICoverSource
{
    public const string Name = "Open Library";

    public bool CanFind(CoverQuery book) => book.Isbn is not null;

    public async Task<CoverImage?> FetchAsync(CoverQuery book, CancellationToken cancellationToken = default)
    {
        if (book.Isbn is not { } isbn)
        {
            return null;
        }

        try
        {
            return CoverImages.AsCover(await client.GetCoverByIsbnAsync(isbn, cancellationToken), Name);
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

    public bool CanFind(CoverQuery book) => book.AudiobookAsin is not null;

    public async Task<CoverImage?> FetchAsync(CoverQuery book, CancellationToken cancellationToken = default)
    {
        if (book.AudiobookAsin is not { } asin)
        {
            return null;
        }

        try
        {
            return CoverImages.AsCover(await client.GetCoverByAsinAsync(asin, cancellationToken), Name);
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

    public bool CanFind(CoverQuery book) => sources.Any(source => source.CanFind(book));

    public async Task<CoverImage?> FetchAsync(CoverQuery book, CancellationToken cancellationToken = default)
    {
        var skipped = false;
        foreach (var source in sources.Where(source => source.CanFind(book)))
        {
            if (_refused.Contains(source))
            {
                skipped = true;
                continue;
            }

            try
            {
                if (await source.FetchAsync(book, cancellationToken) is { } cover)
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

/// <summary>The cover sources in the order they are tried, as configured.</summary>
public sealed record CoverSourceList(IReadOnlyList<ICoverSource> Sources);
