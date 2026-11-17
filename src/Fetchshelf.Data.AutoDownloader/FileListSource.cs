using Fetchshelf.Core.Lists;
using Fetchshelf.Data.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>
/// One `[[file_list]]`'s books, read from its file on every run (<see cref="ListFileReader"/>). A file that is missing
/// or can't be read fails the run; a line that can't be read is left out and logged with its line number.
/// </summary>
public sealed class FileListSource(FileListEntry entry, ILogger? logger = null) : IListBookSource
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public int UnreadableLines { get; private set; }

    public async Task<IReadOnlyList<ListBook>> GetBooksAsync(CancellationToken cancellationToken = default)
    {
        var contents = ListFileReader.Read(await ReadTextAsync(entry, cancellationToken), entry.Shelf);
        UnreadableLines = contents.Problems.Count;
        foreach (var problem in contents.Problems)
        {
            _logger.LogWarning("List file {Path}, line {Line}: not read, {Reason}", entry.Path, problem.Line, problem.Reason);
        }

        if (entry.Shelf is not null && !contents.HasShelfColumn)
        {
            _logger.LogWarning(
                "List file {Path} has no shelf column (such as Exclusive Shelf or Read Status), so 'shelf' is ignored and every book is read",
                entry.Path);
        }

        return contents.Books;
    }

    /// <summary>The list file's text.</summary>
    /// <exception cref="ListFileException">The file is missing or can't be read.</exception>
    public static async Task<string> ReadTextAsync(FileListEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            return await File.ReadAllTextAsync(entry.FullPath, cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ListFileException($"List file not found: {entry.Path}", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ListFileException($"List file could not be read: {entry.Path}: {ex.Message}", ex);
        }
    }
}

/// <summary>A list file that is missing or can't be read.</summary>
public sealed class ListFileException(string message, Exception innerException) : Exception(message, innerException);
