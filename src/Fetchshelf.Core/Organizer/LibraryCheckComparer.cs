using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Organizer;

/// <summary>A file as the Library check found it.</summary>
public sealed record CheckedFile(string Name, long SizeBytes, DateTimeOffset ModifiedAt);

/// <summary>A folder as the Library check walked it: its full path and the files directly in it.</summary>
public sealed record CheckedFolder(string Path, IReadOnlyList<CheckedFile> Files);

/// <summary>A configured library, as the Library check needs it.</summary>
/// <param name="PlacesFiles">False for `no_link`, whose books have a folder on record but no files placed.</param>
public sealed record CheckedLibrary(string Name, string LibraryDir, bool IsSeriesLayout, bool PlacesFiles);

/// <summary>A linked book: its folder and the files Fetchshelf placed there, relative to it.</summary>
public sealed record KnownBook(string TorrentId, string Title, IReadOnlyList<string> Authors, string LibraryPath, IReadOnlyList<string> LinkedFiles);

/// <summary>A book's title and authors, as a folder's `metadata.json` names them.</summary>
public sealed record BookIdentity(string Title, IReadOnlyList<string> Authors);

public enum LibraryFindingKind
{
    /// <summary>A folder holding files that no book's folder is, or is inside of.</summary>
    UnknownFolder,

    /// <summary>A file in a series folder that no book of it lists, or a stray file above the book folders.</summary>
    UnknownFile,

    /// <summary>A linked book whose folder doesn't exist.</summary>
    FolderMissing,

    /// <summary>A linked book whose folder exists, but some of whose files don't.</summary>
    FilesMissing,
}

/// <summary>What would stop Relink placing a book's files again, as the Library check found when it asked qBittorrent.</summary>
public enum RelinkBlocker
{
    /// <summary>Nothing: Relink can place the files.</summary>
    None,

    /// <summary>qBittorrent no longer has the torrent, so there is nothing to link from.</summary>
    NotInQBittorrent,

    /// <summary>qBittorrent has the torrent, but it hasn't finished downloading.</summary>
    NotFinished,

    /// <summary>qBittorrent has the torrent, but can't find its files (moved or deleted outside it); the detail is where it looks.</summary>
    MissingFiles,

    /// <summary>No library accepts the torrent's qBittorrent category, save path and tags.</summary>
    NoLibraryAccepts,

    /// <summary>Libraries take the torrent by its category, but none of them takes the book's genres.</summary>
    NoLibraryTakesGenres,

    /// <summary>The book has no author, which a book-layout library's folder names need.</summary>
    NoAuthor,

    /// <summary>qBittorrent couldn't be asked.</summary>
    Unknown,
}

/// <summary>One thing the Library check found that doesn't match.</summary>
/// <param name="Path">The folder or file found, or the book's folder on record.</param>
public sealed record LibraryFinding(LibraryFindingKind Kind, string Path, string? LibraryName)
{
    /// <summary>For an unknown folder or file: its size, with everything below it.</summary>
    public long Bytes { get; init; }

    /// <summary>For an unknown folder: how many files it holds, with its sub-folders'.</summary>
    public int Files { get; init; }

    /// <summary>For an unknown folder or file: when its newest file was last changed.</summary>
    public DateTimeOffset? NewestAt { get; init; }

    /// <summary>For a book's finding: the book.</summary>
    public string? TorrentId { get; init; }

    public string? Title { get; init; }

    public IReadOnlyList<string> Authors { get; init; } = [];

    /// <summary>For <see cref="LibraryFindingKind.FilesMissing"/>: the linked files that aren't there, relative to the folder.</summary>
    public IReadOnlyList<string> MissingFiles { get; init; } = [];

    /// <summary>For an unknown folder that looks like a linked book: that book, which it is probably an older folder of.</summary>
    public KnownBook? ProbablyOlderFolderOf { get; init; }

    /// <summary>For a book's finding: what would stop Relink, when the check could ask qBittorrent; null when it didn't.</summary>
    public RelinkBlocker? Blocker { get; init; }

    /// <summary>
    /// What goes with <see cref="Blocker"/>: the torrent's qBittorrent category for
    /// <see cref="RelinkBlocker.NoLibraryAccepts"/>, the book's genres for <see cref="RelinkBlocker.NoLibraryTakesGenres"/>, why
    /// qBittorrent couldn't be asked for <see cref="RelinkBlocker.Unknown"/>.
    /// </summary>
    public string? BlockerDetail { get; init; }
}

/// <summary>
/// Compares the library folders as walked with the linked books, and says what doesn't match (#25). Pure: the walk and
/// reading a folder's `metadata.json` are done by the caller.
/// </summary>
public static class LibraryCheckComparer
{
    /// <summary>The files Fetchshelf writes next to a book's or a series' files, which no book lists.</summary>
    private static readonly HashSet<string> SideFiles = new(["metadata.json", "metadata.opf", "series.json", "cover.jpg", "cover.png", "cover.webp"], StringComparer.OrdinalIgnoreCase);

    /// <param name="libraries">The configured libraries.</param>
    /// <param name="folders">
    /// Every folder walked under the libraries' folders, and the folders of books outside them (a `library_dir` changed
    /// since), each with the files directly in it. Only folders under a library's folder can be unknown.
    /// </param>
    /// <param name="books">The linked books.</param>
    /// <param name="readIdentity">Reads an unknown folder's `metadata.json`, if it has one; asked only for unknown folders.</param>
    public static List<LibraryFinding> Compare(
        IReadOnlyList<CheckedLibrary> libraries, IReadOnlyList<CheckedFolder> folders, IReadOnlyList<KnownBook> books,
        Func<string, BookIdentity?>? readIdentity = null)
    {
        var byPath = folders.ToDictionary(f => Trim(f.Path), StringComparer.Ordinal);
        var bookFolders = books.Select(b => Trim(b.LibraryPath)).ToHashSet(StringComparer.Ordinal);
        var aboveBooks = books.SelectMany(b => Ancestors(Trim(b.LibraryPath))).ToHashSet(StringComparer.Ordinal);
        var roots = libraries.Select(l => Trim(l.LibraryDir)).ToHashSet(StringComparer.Ordinal);

        var findings = new List<LibraryFinding>();
        findings.AddRange(UnknownFolders(libraries, folders, byPath, bookFolders, aboveBooks, roots, books, readIdentity));
        findings.AddRange(UnknownFiles(libraries, folders, byPath, books, bookFolders, aboveBooks, roots));
        findings.AddRange(BookFindings(libraries, books, byPath));
        return findings;
    }

    private static IEnumerable<LibraryFinding> UnknownFolders(
        IReadOnlyList<CheckedLibrary> libraries, IReadOnlyList<CheckedFolder> folders, Dictionary<string, CheckedFolder> byPath,
        HashSet<string> bookFolders, HashSet<string> aboveBooks, HashSet<string> roots, IReadOnlyList<KnownBook> books,
        Func<string, BookIdentity?>? readIdentity)
    {
        // A folder holding files, inside a library, that is no book's folder, inside none and above none. Of nested ones,
        // the outermost is reported: a book's `Disc 1` belongs to the book's folder when that holds files of its own.
        var candidates = folders
            .Select(f => Trim(f.Path))
            .Where(path => byPath[path].Files.Count > 0 && !roots.Contains(path) && LibraryOf(libraries, path) is not null
                && !bookFolders.Contains(path) && !aboveBooks.Contains(path) && !Ancestors(path).Any(bookFolders.Contains))
            .ToHashSet(StringComparer.Ordinal);
        var reported = candidates.Where(path => !Ancestors(path).Any(candidates.Contains)).ToHashSet(StringComparer.Ordinal);

        // Sizes and dates of everything below each reported folder, by walking each folder's ancestors once.
        var totals = reported.ToDictionary(p => p, _ => (Bytes: 0L, Files: 0, Newest: (DateTimeOffset?)null), StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            var path = Trim(folder.Path);
            var owner = reported.Contains(path) ? path : Ancestors(path).FirstOrDefault(reported.Contains);
            if (owner is null || folder.Files.Count == 0)
            {
                continue;
            }

            var (bytes, files, newest) = totals[owner];
            totals[owner] = (bytes + folder.Files.Sum(f => f.SizeBytes), files + folder.Files.Count, Max(newest, folder.Files.Max(f => f.ModifiedAt)));
        }

        foreach (var path in reported.Order(StringComparer.Ordinal))
        {
            var (bytes, files, newest) = totals[path];
            yield return new LibraryFinding(LibraryFindingKind.UnknownFolder, path, LibraryOf(libraries, path)!.Name)
            {
                Bytes = bytes,
                Files = files,
                NewestAt = newest,
                ProbablyOlderFolderOf = OlderFolderOf(path, readIdentity?.Invoke(path), books),
            };
        }
    }

    /// <summary>
    /// Files no book lists: in a series library, files in a series folder that none of its books lists; anywhere, files
    /// in a library's own folder or in a folder above book folders, such as an author folder.
    /// </summary>
    private static IEnumerable<LibraryFinding> UnknownFiles(
        IReadOnlyList<CheckedLibrary> libraries, IReadOnlyList<CheckedFolder> folders, Dictionary<string, CheckedFolder> byPath,
        IReadOnlyList<KnownBook> books, HashSet<string> bookFolders, HashSet<string> aboveBooks, HashSet<string> roots)
    {
        var listedBySeriesFolder = books
            .Where(b => LibraryOf(libraries, Trim(b.LibraryPath)) is { IsSeriesLayout: true })
            .GroupBy(b => Trim(b.LibraryPath), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(b => b.LinkedFiles).Select(f => f.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.Ordinal);

        foreach (var folder in folders.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            var path = Trim(folder.Path);
            var library = LibraryOf(libraries, path);
            if (library is null)
            {
                continue;
            }

            var seriesFolder = listedBySeriesFolder.Keys.FirstOrDefault(s => PathPrefix.IsSameOrUnder(path, s));
            IEnumerable<CheckedFile> unknown;
            if (seriesFolder is not null)
            {
                var relativeFolder = Path.GetRelativePath(seriesFolder, path).Replace('\\', '/');
                unknown = folder.Files.Where(f =>
                    !listedBySeriesFolder[seriesFolder].Contains(relativeFolder == "." ? f.Name : $"{relativeFolder}/{f.Name}")
                    && !(relativeFolder == "." && SideFiles.Contains(f.Name)));
            }
            else if ((roots.Contains(path) || aboveBooks.Contains(path)) && !bookFolders.Contains(path))
            {
                unknown = folder.Files;
            }
            else
            {
                continue;
            }

            foreach (var file in unknown)
            {
                yield return new LibraryFinding(LibraryFindingKind.UnknownFile, Path.Combine(path, file.Name), library.Name)
                {
                    Bytes = file.SizeBytes,
                    Files = 1,
                    NewestAt = file.ModifiedAt,
                };
            }
        }
    }

    private static IEnumerable<LibraryFinding> BookFindings(
        IReadOnlyList<CheckedLibrary> libraries, IReadOnlyList<KnownBook> books, Dictionary<string, CheckedFolder> byPath)
    {
        foreach (var book in books.OrderBy(b => b.LibraryPath, StringComparer.Ordinal))
        {
            var path = Trim(book.LibraryPath);
            var library = LibraryOf(libraries, path);
            if (library is { PlacesFiles: false })
            {
                continue; // no_link: the folder on record was never made
            }

            if (!byPath.ContainsKey(path))
            {
                yield return Book(LibraryFindingKind.FolderMissing, book, library);
                continue;
            }

            var missing = book.LinkedFiles.Where(relative =>
            {
                var full = Path.Combine(path, relative.Replace('\\', '/'));
                return !(byPath.TryGetValue(Trim(Path.GetDirectoryName(full)!), out var folder)
                    && folder.Files.Any(f => f.Name == Path.GetFileName(full)));
            }).ToList();
            if (missing.Count > 0)
            {
                yield return Book(LibraryFindingKind.FilesMissing, book, library) with { MissingFiles = missing };
            }
        }
    }

    private static LibraryFinding Book(LibraryFindingKind kind, KnownBook book, CheckedLibrary? library) =>
        new(kind, Trim(book.LibraryPath), library?.Name) { TorrentId = book.TorrentId, Title = book.Title, Authors = book.Authors };

    /// <summary>
    /// The linked book an unknown folder is probably an older folder of: by its `metadata.json`'s title and an author,
    /// else a book in the same parent folder whose title the folder's name holds.
    /// </summary>
    private static KnownBook? OlderFolderOf(string path, BookIdentity? identity, IReadOnlyList<KnownBook> books)
    {
        if (identity is not null)
        {
            var title = TitleNormalizer.Normalize(identity.Title);
            var authors = identity.Authors.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (books.FirstOrDefault(b => title.Length > 0 && TitleNormalizer.Normalize(b.Title) == title && b.Authors.Any(authors.Contains)) is { } match)
            {
                return match;
            }
        }

        var parent = Path.GetDirectoryName(path);
        var name = TitleNormalizer.Normalize(Path.GetFileName(path));
        return books.FirstOrDefault(b =>
            Path.GetDirectoryName(Trim(b.LibraryPath)) == parent
            && TitleNormalizer.Normalize(b.Title) is { Length: > 0 } title
            && name.Contains(title, StringComparison.Ordinal));
    }

    private static CheckedLibrary? LibraryOf(IReadOnlyList<CheckedLibrary> libraries, string path) =>
        libraries.Where(l => PathPrefix.IsSameOrUnder(path, l.LibraryDir)).MaxBy(l => l.LibraryDir.Length);

    /// <summary>The folders above <paramref name="path"/>, nearest first.</summary>
    private static IEnumerable<string> Ancestors(string path)
    {
        for (var parent = Path.GetDirectoryName(path); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
        {
            yield return parent;
        }
    }

    private static string Trim(string path) => path.Length > 1 ? path.TrimEnd('/', '\\') : path;

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset b) => a is { } value && value > b ? value : b;
}
