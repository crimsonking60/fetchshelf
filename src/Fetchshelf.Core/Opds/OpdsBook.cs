using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Opds;

/// <summary>One file of a book in the catalog, relative to the book's folder.</summary>
public sealed record OpdsFile(string RelativePath, long SizeBytes, string Extension);

/// <summary>One series a book is in, with its number for sorting and its entry as shown ("1", "1-3", "2p1").</summary>
public sealed record OpdsSeries(string Name, decimal? Number, string? Entry);

/// <summary>A book in the OPDS catalog: a linked torrent with an ebook, and the files that can be downloaded.</summary>
public sealed record OpdsBook
{
    /// <summary>The torrent's info hash.</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required IReadOnlyList<string> Authors { get; init; }

    public required IReadOnlyList<OpdsSeries> Series { get; init; }

    public string? Description { get; init; }

    public required Language Language { get; init; }

    public required IReadOnlyList<string> Genres { get; init; }

    public required DateTimeOffset AddedAt { get; init; }

    /// <summary>The book's folder in its library.</summary>
    public required string Folder { get; init; }

    /// <summary>The index of the book's `[[library]]` in the configuration.</summary>
    public required int LibraryIndex { get; init; }

    public required IReadOnlyList<OpdsFile> Files { get; init; }

    /// <summary>Whether the book's format can hold a cover Fetchshelf knows how to read (EPUB, CBZ).</summary>
    public bool MayHaveCover => Files.Count > 0 && Files[0].Extension is "epub" or "cbz";
}

/// <summary>Turns a linked torrent into an <see cref="OpdsBook"/>, or leaves it out of the catalog.</summary>
public static class OpdsBooks
{
    /// <summary>
    /// The media types whose books are served. An audiobook's companion PDF, linked as its ebook side, is left out:
    /// the catalog is for books to read.
    /// </summary>
    public static readonly IReadOnlySet<MediaType> EbookMediaTypes =
        new HashSet<MediaType> { MediaType.Ebook, MediaType.Manga, MediaType.ComicBook, MediaType.PeriodicalEbook };

    /// <param name="id">The torrent's info hash.</param>
    /// <param name="metadata">The torrent's stored metadata.</param>
    /// <param name="folder">The torrent's library folder.</param>
    /// <param name="addedAt">When Fetchshelf first recorded the torrent.</param>
    /// <param name="libraryIndex">The index of the `[[library]]` holding the folder.</param>
    /// <param name="ebookExtension">The ebook format that was linked.</param>
    /// <param name="linkedFiles">Every file placed in the folder, relative path and size.</param>
    /// <returns>The book, or null when its media type isn't an ebook type or none of its files is the ebook format.</returns>
    public static OpdsBook? From(
        string id, TorrentMetadata metadata, string folder, DateTimeOffset addedAt, int libraryIndex,
        string ebookExtension, IEnumerable<(string RelativePath, long SizeBytes)> linkedFiles)
    {
        if (!EbookMediaTypes.Contains(metadata.MediaType))
        {
            return null;
        }

        var extension = ebookExtension.TrimStart('.').ToLowerInvariant();
        var files = linkedFiles
            .Where(file => string.Equals(ExtensionOf(file.RelativePath), extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(file => new OpdsFile(file.RelativePath, file.SizeBytes, extension))
            .ToList();
        if (files.Count == 0)
        {
            return null;
        }

        return new OpdsBook
        {
            Id = id,
            Title = metadata.Title,
            Authors = metadata.Authors,
            Series = metadata.Series
                .Select(series => new OpdsSeries(
                    series.Name,
                    series.Entries.Count > 0 ? series.SortedEntries[0].FirstNumber : null,
                    LibraryMetadataJsonBuilder.FormatSequence(series)))
                .ToList(),
            Description = string.IsNullOrWhiteSpace(metadata.Description) ? null : metadata.Description,
            Language = metadata.Language,
            Genres = LibraryMetadataJsonBuilder.BuildGenres(metadata),
            AddedAt = addedAt,
            Folder = folder,
            LibraryIndex = libraryIndex,
            Files = files,
        };
    }

    private static string ExtensionOf(string path) => Path.GetExtension(path).TrimStart('.');
}
