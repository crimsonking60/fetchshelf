using SkiaSharp;
using UglyToad.PdfPig.Content;

namespace Fetchshelf.Data.Opds;

/// <summary>
/// A PDF cover that a scan cut into tiles of different sizes, side by side and one above the other, that together fill
/// the page without overlapping, such as a picture across the top, a column down the left and a picture beside it. The
/// tiles are decoded and drawn where the page draws them, on white, at the resolution of the sharpest tile, and saved
/// as one JPEG. Uses SkiaSharp, as <see cref="PdfStripCover"/> does.
/// </summary>
internal static class PdfMosaicCover
{
    /// <summary>More tiles than this aren't a cut-up cover but a page of pictures.</summary>
    private const int MaxTiles = 24;

    /// <summary>How much of the page the tiles must fill together.</summary>
    public const double MinCoverage = 0.85;

    /// <summary>The largest cover put together, in pixels, so a huge scan can't use up memory.</summary>
    private const long MaxPixels = 40_000_000;

    /// <summary>The grid, per side, the page is cut into to measure how much of it the tiles fill, overlaps counted once.</summary>
    private const int CoverageGrid = 100;

    /// <summary>How much more than the page they fill the tiles' areas may add up to, for edges that touch or overlap a little.</summary>
    private const double MaxOverlap = 1.1;

    private const int JpegQuality = 90;

    /// <summary>How much of the page, 0 to 1, the images fill together.</summary>
    public static double Coverage(IReadOnlyList<IPdfImage> images, UglyToad.PdfPig.Core.PdfRectangle page)
    {
        if (page.Width <= 0 || page.Height <= 0)
        {
            return 0;
        }

        var covered = new bool[CoverageGrid, CoverageGrid];
        foreach (var box in images.Select(i => i.BoundingBox))
        {
            int Column(double x) => Math.Clamp((int)Math.Round((x - page.Left) / page.Width * CoverageGrid), 0, CoverageGrid);
            int Row(double y) => Math.Clamp((int)Math.Round((y - page.Bottom) / page.Height * CoverageGrid), 0, CoverageGrid);
            for (var column = Column(box.Left); column < Column(box.Right); column++)
            {
                for (var row = Row(box.Bottom); row < Row(box.Top); row++)
                {
                    covered[column, row] = true;
                }
            }
        }

        return covered.Cast<bool>().Count(c => c) / (double)(CoverageGrid * CoverageGrid);
    }

    /// <summary>
    /// The tiles drawn as one JPEG, or null when they aren't a cut-up scan: too many, one with transparency (a soft mask),
    /// which a designed page layers and which comes out black drawn without it, or tiles drawn over each other. Null too
    /// when one can't be decoded or the result is too large.
    /// </summary>
    public static BookCover? Join(IReadOnlyList<IPdfImage> tiles, UglyToad.PdfPig.Core.PdfRectangle page)
    {
        if (tiles.Count is < 2 or > MaxTiles || tiles.Any(t => t.BoundingBox.Width <= 0 || t.BoundingBox.Height <= 0 || t.MaskImage is not null))
        {
            return null;
        }

        // Tiles side by side add up to the part of the page they fill; layered pictures add up to more.
        var pageArea = page.Width * page.Height;
        if (tiles.Sum(t => t.BoundingBox.Area) > Coverage(tiles, page) * pageArea * MaxOverlap)
        {
            return null;
        }

        var (left, right) = (tiles.Min(t => t.BoundingBox.Left), tiles.Max(t => t.BoundingBox.Right));
        var (bottom, top) = (tiles.Min(t => t.BoundingBox.Bottom), tiles.Max(t => t.BoundingBox.Top));

        // Pixels per point of the sharpest tile, less if the cover would be too large.
        var scale = tiles.Max(t => t.WidthInSamples / t.BoundingBox.Width);
        var pixels = (right - left) * (top - bottom) * scale * scale;
        if (pixels > MaxPixels)
        {
            scale *= Math.Sqrt(MaxPixels / pixels);
        }

        var (width, height) = ((int)Math.Round((right - left) * scale), (int)Math.Round((top - bottom) * scale));
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var decoded = new List<(IPdfImage Tile, SKImage Image)>();
        try
        {
            foreach (var tile in tiles)
            {
                if (BookCoverExtractor.PdfImageBytes(tile) is not { } bytes || SKImage.FromEncodedData(bytes) is not { } image)
                {
                    return null;
                }

                decoded.Add((tile, image));
            }

            using var cover = new SKBitmap(width, height);
            using (var canvas = new SKCanvas(cover))
            {
                canvas.Clear(SKColors.White);
                foreach (var (tile, image) in decoded)
                {
                    // PDF's y grows upwards, the bitmap's downwards.
                    var box = tile.BoundingBox;
                    var target = SKRect.Create(
                        (float)((box.Left - left) * scale), (float)((top - box.Top) * scale), (float)(box.Width * scale), (float)(box.Height * scale));
                    canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Linear));
                }
            }

            using var data = cover.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            return data is not null && data.Size <= BookCoverExtractor.MaxCoverBytes ? new BookCover(data.ToArray(), "image/jpeg") : null;
        }
        finally
        {
            foreach (var (_, image) in decoded)
            {
                image.Dispose();
            }
        }
    }
}
