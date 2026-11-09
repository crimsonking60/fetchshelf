using System.Globalization;
using System.Text;
using System.Xml;
using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Organizer;

/// <summary>
/// Builds the `metadata.opf` document: an OPF 2.0 package holding only metadata, in the form Calibre writes next to
/// its books and imports from. Audiobookshelf reads it too. Text from MAM goes through <see cref="XmlSafeText"/>, so
/// a character XML can't hold doesn't stop the book from being linked.
/// </summary>
public static class LibraryMetadataOpfBuilder
{
    private const string Opf = "http://www.idpf.org/2007/opf";
    private const string Dc = "http://purl.org/dc/elements/1.1/";
    private const string MamIdentifierId = "mam_id";

    public static string Build(TorrentMetadata metadata)
    {
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) };
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            // OPF elements are written with an explicit empty prefix, so `package`, `metadata` and `meta` come out
            // unprefixed in the default namespace, as Calibre writes them; `opf:` is only used on attributes.
            writer.WriteStartDocument();
            writer.WriteStartElement(string.Empty, "package", Opf);
            writer.WriteAttributeString("unique-identifier", MamIdentifierId);
            writer.WriteAttributeString("version", "2.0");
            writer.WriteStartElement(string.Empty, "metadata", Opf);
            writer.WriteAttributeString("xmlns", "dc", null, Dc);
            writer.WriteAttributeString("xmlns", "opf", null, Opf);

            writer.WriteStartElement("dc", "identifier", Dc);
            writer.WriteAttributeString("id", MamIdentifierId);
            writer.WriteAttributeString("opf", "scheme", Opf, "MAM");
            writer.WriteString(metadata.MamId.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            writer.WriteElementString("dc", "title", Dc, XmlSafeText.Clean(metadata.Title));
            foreach (var author in metadata.Authors)
            {
                WriteCreator(writer, author, "aut");
            }

            foreach (var narrator in metadata.Narrators)
            {
                WriteCreator(writer, narrator, "nrt");
            }

            if (!string.IsNullOrWhiteSpace(metadata.Description))
            {
                writer.WriteElementString("dc", "description", Dc, XmlSafeText.Clean(metadata.Description));
            }

            if (LanguageCodes.For(metadata.Language) is { } language)
            {
                writer.WriteElementString("dc", "language", Dc, language);
            }

            WriteIdentifier(writer, "ISBN", metadata.Isbn);
            WriteIdentifier(writer, "ASIN", metadata.Asin);
            foreach (var genre in LibraryMetadataJsonBuilder.BuildGenres(metadata))
            {
                writer.WriteElementString("dc", "subject", Dc, XmlSafeText.Clean(genre));
            }

            foreach (var series in metadata.Series)
            {
                WriteMeta(writer, "calibre:series", series.Name);
                if (series.Entries.Count > 0)
                {
                    WriteMeta(writer, "calibre:series_index", series.SortedEntries[0].FirstNumber.ToString(CultureInfo.InvariantCulture));
                }
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCreator(XmlWriter writer, string name, string role)
    {
        writer.WriteStartElement("dc", "creator", Dc);
        writer.WriteAttributeString("opf", "role", Opf, role);
        writer.WriteString(XmlSafeText.Clean(name));
        writer.WriteEndElement();
    }

    private static void WriteIdentifier(XmlWriter writer, string scheme, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        writer.WriteStartElement("dc", "identifier", Dc);
        writer.WriteAttributeString("opf", "scheme", Opf, scheme);
        writer.WriteString(XmlSafeText.Clean(value));
        writer.WriteEndElement();
    }

    private static void WriteMeta(XmlWriter writer, string name, string content)
    {
        writer.WriteStartElement(string.Empty, "meta", Opf);
        writer.WriteAttributeString("name", name);
        writer.WriteAttributeString("content", XmlSafeText.Clean(content));
        writer.WriteEndElement();
    }
}
