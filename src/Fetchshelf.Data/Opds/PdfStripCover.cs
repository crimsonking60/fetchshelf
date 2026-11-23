using SkiaSharp;
using UglyToad.PdfPig.Content;

namespace Fetchshelf.Data.Opds;

/// <summary>
/// A PDF cover that a scan cut into strips: images of the same width in pixels, drawn one directly above the other across
/// the same part of the page, that together cover at least half of it, such as three strips of at most 500 pixels.
/// They are decoded, stacked and saved as one JPEG, the only place Fetchshelf draws a cover rather than passing an image
/// through. Uses SkiaSharp, whose native library is part of every image (see THIRD-PARTY-NOTICES.md).
/// </summary>
internal static class PdfStripCover
{
    /// <summary>More strips than this aren't a cut-up cover but a page of pictures.</summary>
    private const int MaxStrips = 12;

    /// <summary>The largest cover put together, in pixels, so a huge scan can't use up memory.</summary>
    private const long MaxPixels = 40_000_000;

    /// <summary>How far apart, in points, strips may be and still count as touching or as lined up.</summary>
    private const double Tolerance = 2;

    private const int JpegQuality = 90;

    /// <summary>The strips put together as one JPEG, or null when the page's images aren't such strips.</summary>
    public static BookCover? Join(IReadOnlyList<IPdfImage> images, double pageArea)
    {
        if (images.Count is < 2 or > MaxStrips)
        {
            return null;
        }

        // Top to bottom: PDF's y grows upwards.
        var strips = images.OrderByDescending(i => i.BoundingBox.Top).ToList();
        var first = strips[0];
        for (var i = 1; i < strips.Count; i++)
        {
            var (above, strip) = (strips[i - 1].BoundingBox, strips[i].BoundingBox);
            if (strips[i].WidthInSamples != first.WidthInSamples
                || Math.Abs(strip.Left - first.BoundingBox.Left) > Tolerance
                || Math.Abs(strip.Right - first.BoundingBox.Right) > Tolerance
                || Math.Abs(strip.Top - above.Bottom) > Tolerance)
            {
                return null;
            }
        }

        var area = (first.BoundingBox.Top - strips[^1].BoundingBox.Bottom) * first.BoundingBox.Width;
        var pixels = (long)first.WidthInSamples * strips.Sum(s => (long)s.HeightInSamples);
        if (area < pageArea / 2 || pixels > MaxPixels)
        {
            return null;
        }

        var decoded = new List<SKImage>();
        try
        {
            foreach (var strip in strips)
            {
                if (BookCoverExtractor.PdfImageBytes(strip) is not { } bytes || SKImage.FromEncodedData(bytes) is not { } decodedStrip)
                {
                    return null;
                }

                decoded.Add(decodedStrip);
            }

            // The width in pixels is the same by the check above; a strip decoding otherwise isn't one of these.
            var width = decoded[0].Width;
            if (decoded.Any(d => d.Width != width))
            {
                return null;
            }

            using var cover = new SKBitmap(width, decoded.Sum(d => d.Height));
            using (var canvas = new SKCanvas(cover))
            {
                var y = 0;
                foreach (var strip in decoded)
                {
                    canvas.DrawImage(strip, 0, y, SKSamplingOptions.Default);
                    y += strip.Height;
                }
            }

            using var data = cover.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            return data is not null && data.Size <= BookCoverExtractor.MaxCoverBytes ? new BookCover(data.ToArray(), "image/jpeg") : null;
        }
        finally
        {
            foreach (var strip in decoded)
            {
                strip.Dispose();
            }
        }
    }
}
