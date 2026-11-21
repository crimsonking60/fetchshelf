using System.IO.Compression;
using Fetchshelf.Core.Catalog;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Web.Catalog;

/// <summary>Serves one of a book's files, for the OPDS catalog and the Books page alike.</summary>
public static class BookFiles
{
    /// <summary>
    /// The book's file at <paramref name="index"/> among its files of <paramref name="kind"/>, with range support and a
    /// "Title - Author.ext" name. Nothing from the request becomes part of the path: the book and file come from the
    /// catalog, and the stored relative path must still lead inside the book's folder.
    /// </summary>
    public static IResult Serve(CatalogBook book, CatalogFileKind kind, int index, ILogger logger)
    {
        var files = CatalogFiles.Of(book, kind);
        if (index < 0 || index >= files.Count || PathOf(book, files[index], logger) is not { } path)
        {
            return Results.NotFound();
        }

        return Results.File(
            path, CatalogFiles.ContentTypeFor(files[index].Extension), CatalogFiles.DownloadName(book, files, index), enableRangeProcessing: true);
    }

    /// <summary>
    /// All of a book's audio files as one zip, streamed as it is written: stored without compression (audio doesn't
    /// compress), never built on disk, named after the files as placed, `Disc N` folders included. It has no length
    /// and can't be resumed, which is why the files can also be downloaded one by one. Not found when the book has no
    /// audio, or when any of its files is missing or outside its folder.
    /// </summary>
    public static IResult ServeAudioZip(CatalogBook book, ILogger logger)
    {
        var entries = new List<(string Name, string Path)>();
        foreach (var file in book.AudioFiles)
        {
            if (PathOf(book, file, logger) is not { } path)
            {
                return Results.NotFound();
            }

            entries.Add((file.RelativePath.Replace('\\', '/'), path));
        }

        if (entries.Count == 0)
        {
            return Results.NotFound();
        }

        return Results.Stream(
            async response =>
            {
                var output = new SyncWritesBuffered(response);
                await using (var zip = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null))
                {
                    foreach (var (name, path) in entries)
                    {
                        var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                        entry.LastWriteTime = File.GetLastWriteTime(path);
                        await using (var target = await entry.OpenAsync())
                        {
                            await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                            await source.CopyToAsync(target);
                        }

                        await output.FlushAsync();
                    }
                }

                await output.FlushAsync();
            },
            "application/zip",
            CatalogFiles.NamedAfter(book, string.Empty, "zip"));
    }

    /// <summary>The file's full path, or null when it would lead outside the book's folder or is missing.</summary>
    private static string? PathOf(CatalogBook book, CatalogFile file, ILogger logger)
    {
        var folder = Path.GetFullPath(book.Folder);
        var path = Path.GetFullPath(Path.Combine(folder, file.RelativePath));
        if (path == folder.TrimEnd(Path.DirectorySeparatorChar) || !PathPrefix.IsSameOrUnder(path, folder))
        {
            return null;
        }

        if (!File.Exists(path))
        {
            logger.LogWarning("Download of {Title}: {Path} is missing", book.Title, path);
            return null;
        }

        return path;
    }

    /// <summary>
    /// The response stream for a zip being written. The zip writes an entry's small trailing record synchronously,
    /// which the server refuses; those writes are kept here and sent with the next asynchronous write or flush.
    /// </summary>
    private sealed class SyncWritesBuffered(Stream inner) : Stream
    {
        private readonly MemoryStream _pending = new();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => _pending.Write(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => _pending.Write(buffer);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await SendPendingAsync(cancellationToken);
            await inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await SendPendingAsync(cancellationToken);
            await inner.FlushAsync(cancellationToken);
        }

        // Nothing is sent synchronously: what is pending goes with the next asynchronous write or flush.
        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        private async Task SendPendingAsync(CancellationToken cancellationToken)
        {
            if (_pending.Length > 0)
            {
                await inner.WriteAsync(_pending.GetBuffer().AsMemory(0, (int)_pending.Length), cancellationToken);
                _pending.SetLength(0);
            }
        }
    }
}
