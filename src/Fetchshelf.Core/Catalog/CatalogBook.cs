using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Catalog;

/// <summary>One file of a book in the catalog, relative to the book's folder.</summary>
public sealed record CatalogFile(string RelativePath, long SizeBytes, string Extension);

/// <summary>One series a book is in, with its number for sorting and its entry as shown ("1", "1-3", "2p1").</summary>
public sealed record CatalogSeries(string Name, decimal? Number, string? Entry);

/// <summary>
/// A linked book in the catalog: its metadata, where it is, and the audio and ebook files Fetchshelf placed for it. A
/// book can have both, such as an audiobook with its companion PDF.
/// </summary>
public sealed record CatalogBook
{
    /// <summary>The torrent's info hash, or for a book of a split collection its own record's id.</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required IReadOnlyList<string> Authors { get; init; }

    public required IReadOnlyList<CatalogSeries> Series { get; init; }

    public string? Description { get; init; }

    public required MediaType MediaType { get; init; }

    public required Language Language { get; init; }

    public required IReadOnlyList<string> Genres { get; init; }

    public required DateTimeOffset AddedAt { get; init; }

    /// <summary>The book's folder in its library.</summary>
    public required string Folder { get; init; }

    /// <summary>The index of the book's `[[library]]` in the configuration.</summary>
    public required int LibraryIndex { get; init; }

    /// <summary>The audio files, in play order (by path, numbers compared as numbers).</summary>
    public required IReadOnlyList<CatalogFile> AudioFiles { get; init; }

    /// <summary>The ebook files, in the same order.</summary>
    public required IReadOnlyList<CatalogFile> EbookFiles { get; init; }

    /// <summary>The matched Audiobookshelf item's id, or null when the book isn't matched.</summary>
    public string? AbsId { get; init; }

    /// <summary>Whether the book's ebook can hold a cover Fetchshelf knows how to read (EPUB, CBZ).</summary>
    public bool MayHaveCover => EbookFiles.Count > 0 && EbookFiles[0].Extension is "epub" or "cbz";
}

/// <summary>Turns a linked torrent into a <see cref="CatalogBook"/>, or leaves it out of the catalog.</summary>
public static class CatalogBooks
{
    /// <param name="id">The torrent's info hash.</param>
    /// <param name="metadata">The torrent's stored metadata.</param>
    /// <param name="folder">The torrent's library folder.</param>
    /// <param name="addedAt">When Fetchshelf first recorded the torrent.</param>
    /// <param name="libraryIndex">The index of the `[[library]]` holding the folder.</param>
    /// <param name="audioExtension">The audio format that was linked, if any.</param>
    /// <param name="ebookExtension">The ebook format that was linked, if any.</param>
    /// <param name="linkedFiles">Every file placed in the folder, relative path and size.</param>
    /// <returns>The book, or null when none of its files is of a linked format.</returns>
    public static CatalogBook? From(
        string id, TorrentMetadata metadata, string folder, DateTimeOffset addedAt, int libraryIndex,
        string? audioExtension, string? ebookExtension, IEnumerable<(string RelativePath, long SizeBytes)> linkedFiles)
    {
        var files = linkedFiles.ToList();
        var audio = FilesOf(files, audioExtension);
        var ebook = FilesOf(files, ebookExtension);
        if (audio.Count == 0 && ebook.Count == 0)
        {
            return null;
        }

        return new CatalogBook
        {
            Id = id,
            Title = metadata.Title,
            Authors = metadata.Authors,
            Series = metadata.Series
                .Select(series => new CatalogSeries(
                    series.Name,
                    series.Entries.Count > 0 ? series.SortedEntries[0].FirstNumber : null,
                    LibraryMetadataJsonBuilder.FormatSequence(series)))
                .ToList(),
            Description = string.IsNullOrWhiteSpace(metadata.Description) ? null : metadata.Description,
            MediaType = metadata.MediaType,
            Language = metadata.Language,
            Genres = LibraryMetadataJsonBuilder.BuildGenres(metadata),
            AddedAt = addedAt,
            Folder = folder,
            LibraryIndex = libraryIndex,
            AudioFiles = audio,
            EbookFiles = ebook,
        };
    }

    private static List<CatalogFile> FilesOf(List<(string RelativePath, long SizeBytes)> files, string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return [];
        }

        var wanted = extension.TrimStart('.').ToLowerInvariant();
        return files
            .Where(file => string.Equals(Path.GetExtension(file.RelativePath).TrimStart('.'), wanted, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.RelativePath, NumberAwareComparer.Instance)
            .Select(file => new CatalogFile(file.RelativePath, file.SizeBytes, wanted))
            .ToList();
    }
}
