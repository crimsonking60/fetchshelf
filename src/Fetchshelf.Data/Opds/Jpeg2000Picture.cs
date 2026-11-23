using SkiaSharp;

namespace Fetchshelf.Data.Opds;

/// <summary>
/// A JPEG 2000 picture from a PDF (its <c>JPXDecode</c> images), turned into a JPEG, which browsers and OPDS readers
/// show; browsers don't show JPEG 2000. Decoded by CoreJ2K and saved by SkiaSharp (see THIRD-PARTY-NOTICES.md).
/// </summary>
internal static class Jpeg2000Picture
{
    /// <summary>The largest picture decoded, in pixels, so a huge scan can't use up memory.</summary>
    private const long MaxPixels = 40_000_000;

    private const int JpegQuality = 90;

    static Jpeg2000Picture()
    {
        // CoreJ2K writes its warnings about odd but readable files to the console, which would fill the log; a picture it
        // can't read throws instead, and gives no cover.
        CoreJ2K.j2k.util.FacilityManager.DefaultMsgLogger = new QuietLogger();
    }

    /// <summary>Whether <paramref name="bytes"/> are a JPEG 2000 file (JP2) or a bare JPEG 2000 codestream.</summary>
    public static bool Is(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20])
        || bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0x4F, 0xFF, 0x51]);

    /// <summary>
    /// The picture as a JPEG, or null when it isn't grey (one component) or colour (three, or four with transparency,
    /// which is dropped) or is too large. Throws CoreJ2K's own exceptions for a damaged picture.
    /// </summary>
    public static byte[]? ToJpeg(byte[] bytes)
    {
        using var image = CoreJ2K.J2kImage.FromBytes(bytes, (CoreJ2K.j2k.util.ParameterList?)null);
        var (width, height, components) = (image.Width, image.Height, image.NumberOfComponents);
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels || components is not (1 or 3 or 4))
        {
            return null;
        }

        var samples = image.GetDataCopy();
        var colours = components == 1 ? 1 : 3;
        var maxima = Enumerable.Range(0, colours).Select(c => (1 << image.GetBitDepth(c)) - 1).ToArray();
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var pixels = bitmap.GetPixelSpan();
        for (var i = 0; i < width * height; i++)
        {
            var at = i * components;
            for (var c = 0; c < 3; c++)
            {
                var colour = colours == 1 ? 0 : c;
                var value = samples[at + colour];
                pixels[(i * 4) + c] = (byte)(maxima[colour] == 255 ? Math.Clamp(value, 0, 255) : Math.Clamp(value * 255L / maxima[colour], 0, 255));
            }

            pixels[(i * 4) + 3] = 255;
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return data?.ToArray();
    }

    private sealed class QuietLogger : CoreJ2K.j2k.util.IMsgLogger
    {
        public void printmsg(int sev, string msg)
        {
        }

        public void println(string str, int flind, int ind)
        {
        }

        public void flush()
        {
        }
    }
}
