using System.IO.Compression;
using System.Text;
using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class EpubMetadataReaderTests
{
    private static MemoryStream Epub(string opf)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string text)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
                writer.Write(text);
            }

            Add("META-INF/container.xml", """
                <?xml version="1.0"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
                </container>
                """);
            Add("OEBPS/content.opf", opf);
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Read_CalibreMetadata()
    {
        using var epub = Epub("""
            <?xml version="1.0"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
                <dc:title>Mort</dc:title>
                <dc:identifier opf:scheme="ISBN">978-0-552-13106-3</dc:identifier>
                <dc:description>Death takes an apprentice.</dc:description>
                <meta name="calibre:series" content="Discworld"/>
                <meta name="calibre:series_index" content="4.0"/>
              </metadata>
            </package>
            """);

        Assert.Equal(new EpubInfo("Mort", "Discworld", 4.0m, "9780552131063", "Death takes an apprentice."), EpubMetadataReader.Read(epub));
    }

    [Fact]
    public void Read_Epub3Collection()
    {
        using var epub = Epub("""
            <?xml version="1.0"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:title>Thud!</dc:title>
                <dc:identifier>urn:isbn:9780552151481</dc:identifier>
                <meta property="belongs-to-collection" id="c1">Discworld</meta>
                <meta refines="#c1" property="group-position">34</meta>
              </metadata>
            </package>
            """);

        Assert.Equal(new EpubInfo("Thud!", "Discworld", 34m, "9780552151481", null), EpubMetadataReader.Read(epub));
    }

    [Fact]
    public void Read_NotAnEpub_IsNull()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a zip"));

        Assert.Null(EpubMetadataReader.Read(stream));
    }
}
