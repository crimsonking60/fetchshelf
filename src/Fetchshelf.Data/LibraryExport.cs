using System.Globalization;
using System.Text;
using System.Text.Json;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data;

/// <summary>A series a torrent belongs to, with its entries as shown (<c>1</c>, <c>2.5</c>, <c>1-6</c>), if any.</summary>
public sealed record LibraryExportSeries(string Name, string? Number);

/// <summary>One torrent as the library export writes it.</summary>
public sealed record LibraryExportRow(
    long MamId,
    string Title,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Narrators,
    IReadOnlyList<LibraryExportSeries> Series,
    string MediaType,
    string Language,
    IReadOnlyList<string> Formats,
    long SizeBytes,
    string? Library,
    string? Folder,
    string Status,
    DateTimeOffset Added,
    DateTimeOffset? Linked)
{
    /// <param name="torrent">The stored torrent.</param>
    /// <param name="metadata">Its metadata, read from <see cref="TorrentEntity.MetadataJson"/>.</param>
    /// <param name="status">Where it stands with the library, as the Torrents page's Status column says.</param>
    /// <param name="linked">When it was last linked into a library, if ever.</param>
    public static LibraryExportRow From(TorrentEntity torrent, TorrentMetadata metadata, string status, DateTimeOffset? linked) => new(
        torrent.MamId,
        torrent.Title,
        metadata.Authors,
        metadata.Narrators,
        metadata.Series
            .Select(s => new LibraryExportSeries(s.Name, s.Entries.Count > 0 ? string.Join(", ", s.Entries.Select(e => e.Display)) : null))
            .ToList(),
        torrent.MediaType ?? metadata.MediaType.ToString(),
        torrent.Language ?? metadata.Language.ToString(),
        new[] { torrent.ChosenAudioExtension, torrent.ChosenEbookExtension }.OfType<string>().ToList(),
        metadata.TotalSizeBytes,
        torrent.LibraryPath is not null ? torrent.LinkerName : null,
        torrent.LibraryPath,
        status,
        torrent.CreatedAt,
        linked);
}

/// <summary>
/// Writes torrents as CSV or JSON, one row at a time, so a large library is never held in memory as one text. The
/// columns are the same in both: MAM id, title, authors, narrators, series, media type, language, the linked formats,
/// the torrent's size, its library and folder, its status, and when it was added and last linked.
/// </summary>
public static class LibraryExport
{
    private static readonly string[] Columns =
        ["mam_id", "title", "authors", "narrators", "series", "media_type", "language", "format", "size_bytes", "library", "folder", "status", "added", "linked"];

    /// <summary>
    /// RFC 4180 CSV with a header row. Lists are joined with <c>; </c>, and a series is <c>Name #1</c> as on the edit page.
    /// A text field that starts with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage return gets a leading
    /// <c>'</c>, so a spreadsheet doesn't run an uploaded title as a formula.
    /// </summary>
    public static async Task WriteCsvAsync(IEnumerable<LibraryExportRow> rows, TextWriter writer, CancellationToken cancellationToken = default)
    {
        await writer.WriteAsync(string.Join(",", Columns) + "\r\n");
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] fields =
            [
                row.MamId.ToString(CultureInfo.InvariantCulture),
                Text(row.Title),
                Text(string.Join("; ", row.Authors)),
                Text(string.Join("; ", row.Narrators)),
                Text(string.Join("; ", row.Series.Select(s => s.Number is null ? s.Name : $"{s.Name} #{s.Number}"))),
                Text(row.MediaType),
                Text(row.Language),
                Text(string.Join("; ", row.Formats)),
                row.SizeBytes.ToString(CultureInfo.InvariantCulture),
                Text(row.Library ?? string.Empty),
                Text(row.Folder ?? string.Empty),
                Text(row.Status),
                Time(row.Added),
                row.Linked is { } linked ? Time(linked) : string.Empty,
            ];
            await writer.WriteAsync(string.Join(",", fields) + "\r\n");
        }

        await writer.FlushAsync(cancellationToken);
    }

    /// <summary>A JSON array of objects, one per torrent, with the CSV's column names as keys and lists as arrays.</summary>
    public static async Task WriteJsonAsync(IEnumerable<LibraryExportRow> rows, Stream stream, CancellationToken cancellationToken = default)
    {
        await using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartArray();
        foreach (var row in rows)
        {
            json.WriteStartObject();
            json.WriteNumber("mam_id", row.MamId);
            json.WriteString("title", row.Title);
            WriteStrings(json, "authors", row.Authors);
            WriteStrings(json, "narrators", row.Narrators);
            json.WriteStartArray("series");
            foreach (var series in row.Series)
            {
                json.WriteStartObject();
                json.WriteString("name", series.Name);
                json.WriteString("number", series.Number);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteString("media_type", row.MediaType);
            json.WriteString("language", row.Language);
            WriteStrings(json, "format", row.Formats);
            json.WriteNumber("size_bytes", row.SizeBytes);
            json.WriteString("library", row.Library);
            json.WriteString("folder", row.Folder);
            json.WriteString("status", row.Status);
            json.WriteString("added", Time(row.Added));
            json.WriteString("linked", row.Linked is { } linked ? Time(linked) : null);
            json.WriteEndObject();

            if (json.BytesPending > 16 * 1024)
            {
                await json.FlushAsync(cancellationToken);
            }
        }

        json.WriteEndArray();
        await json.FlushAsync(cancellationToken);
    }

    private static void WriteStrings(Utf8JsonWriter json, string name, IEnumerable<string> values)
    {
        json.WriteStartArray(name);
        foreach (var value in values)
        {
            json.WriteStringValue(value);
        }

        json.WriteEndArray();
    }

    private static string Time(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Text(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return new StringBuilder(value.Length + 2).Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"').ToString();
    }
}
