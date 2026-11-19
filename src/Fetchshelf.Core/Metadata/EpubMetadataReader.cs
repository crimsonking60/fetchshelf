using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Fetchshelf.Core.Metadata;

/// <summary>What an epub says about itself in its package document (OPF).</summary>
/// <param name="SeriesName">Calibre's <c>calibre:series</c>, or EPUB 3's <c>belongs-to-collection</c>.</param>
/// <param name="SeriesNumber">Calibre's <c>calibre:series_index</c>, or EPUB 3's <c>group-position</c>.</param>
public sealed record EpubInfo(string? Title, string? SeriesName, decimal? SeriesNumber, string? Isbn, string? Description);

/// <summary>Reads an epub's own title, series, ISBN and description, from <c>META-INF/container.xml</c> and its OPF.</summary>
public static class EpubMetadataReader
{
    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    /// <summary>The epub's details; null when the stream isn't an epub that can be read.</summary>
    public static EpubInfo? Read(Stream epub)
    {
        try
        {
            using var zip = new ZipArchive(epub, ZipArchiveMode.Read, leaveOpen: true);
            var containerEntry = zip.GetEntry("META-INF/container.xml");
            if (containerEntry is null)
            {
                return null;
            }

            XDocument container;
            using (var stream = containerEntry.Open())
            {
                container = Load(stream);
            }

            var opfPath = container.Descendants(Container + "rootfile").Select(r => (string?)r.Attribute("full-path")).FirstOrDefault(p => p is not null);
            if (opfPath is null || zip.GetEntry(opfPath) is not { } opfEntry)
            {
                return null;
            }

            using var opfStream = opfEntry.Open();
            return FromPackage(Load(opfStream));
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or IOException)
        {
            return null;
        }
    }

    /// <summary>The details in a package document.</summary>
    public static EpubInfo FromPackage(XDocument package)
    {
        var metadata = package.Descendants(Opf + "metadata").FirstOrDefault();
        if (metadata is null)
        {
            return new EpubInfo(null, null, null, null, null);
        }

        string? Text(XName name) => metadata.Elements(name).Select(e => e.Value.Trim()).FirstOrDefault(v => v.Length > 0);
        var metas = metadata.Elements(Opf + "meta").ToList();
        string? Meta(string name) => metas.Where(m => (string?)m.Attribute("name") == name).Select(m => ((string?)m.Attribute("content"))?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));

        var seriesName = Meta("calibre:series");
        var seriesNumber = Number(Meta("calibre:series_index"));
        if (seriesName is null
            && metas.FirstOrDefault(m => (string?)m.Attribute("property") == "belongs-to-collection") is { } collection
            && collection.Value.Trim() is { Length: > 0 } collectionName)
        {
            seriesName = collectionName;
            var id = (string?)collection.Attribute("id");
            seriesNumber = Number(metas
                .Where(m => (string?)m.Attribute("property") == "group-position" && id is not null && (string?)m.Attribute("refines") == "#" + id)
                .Select(m => m.Value.Trim())
                .FirstOrDefault());
        }

        var isbn = metadata.Elements(Dc + "identifier")
            .Where(e => ((string?)e.Attribute(Opf + "scheme"))?.Equals("ISBN", StringComparison.OrdinalIgnoreCase) == true
                || e.Value.Trim().StartsWith("urn:isbn:", StringComparison.OrdinalIgnoreCase)
                || e.Value.Trim().StartsWith("isbn:", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Value.Trim().Split(':')[^1].Replace("-", "", StringComparison.Ordinal).Trim())
            .FirstOrDefault(v => v.Length is 10 or 13);
        return new EpubInfo(Text(Dc + "title"), seriesName, seriesNumber, isbn, Text(Dc + "description"));
    }

    private static decimal? Number(string? text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : null;

    private static XDocument Load(Stream stream) =>
        XDocument.Load(XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }));
}
