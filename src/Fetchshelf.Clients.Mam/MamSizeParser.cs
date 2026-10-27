using System.Globalization;
using System.Text.RegularExpressions;

namespace Fetchshelf.Clients.Mam;

/// <summary>Parses MaM's size syntax, Fetchshelf-spec.md §12.6: a decimal number (possibly with thousands separators, like "1,020.5 KiB") plus a unit (B, KiB/KB, MiB/MB, GiB/GB, TiB/TB; powers of 1024).</summary>
public static partial class MamSizeParser
{
    [GeneratedRegex(@"^\s*([\d.,]+)\s*([A-Za-z]+)\s*$")]
    private static partial Regex SizePattern();

    public static long ParseBytes(string sizeText)
    {
        var match = SizePattern().Match(sizeText);
        if (!match.Success)
        {
            throw new FormatException($"Unrecognized size: '{sizeText}'.");
        }

        var value = decimal.Parse(match.Groups[1].Value.Replace(",", string.Empty), CultureInfo.InvariantCulture);
        var multiplier = match.Groups[2].Value.ToUpperInvariant() switch
        {
            "B" => 1L,
            "KIB" or "KB" => 1024L,
            "MIB" or "MB" => 1024L * 1024,
            "GIB" or "GB" => 1024L * 1024 * 1024,
            "TIB" or "TB" => 1024L * 1024 * 1024 * 1024,
            _ => throw new FormatException($"Unrecognized size unit: '{match.Groups[2].Value}'."),
        };

        return (long)(value * multiplier);
    }
}
