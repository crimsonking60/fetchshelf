using System.Text;

namespace Fetchshelf.Core.Organizer;

/// <summary>Which characters a library's folder names may hold: a library's <c>folder_names</c>.</summary>
public enum FileNameRules
{
    /// <summary>What Linux and macOS allow: only <c>/</c> and NUL are removed.</summary>
    Posix,

    /// <summary>
    /// Also safe on Windows, SMB shares, exFAT and NTFS: no <c>&lt;&gt;:"\|?*</c> or control characters, no trailing dots
    /// or spaces, and no names Windows reserves.
    /// </summary>
    Portable,
}

/// <summary>Makes one folder name safe under a <see cref="FileNameRules"/>, and no longer than a file system allows.</summary>
public static class FileNameCleaner
{
    /// <summary>The longest name most file systems take for one folder or file, in bytes of UTF-8.</summary>
    public const int MaxComponentBytes = 255;

    /// <summary>What a name that ends up empty becomes, so the folder doesn't move up a level.</summary>
    public const string EmptyName = "_";

    private static readonly HashSet<string> ReservedNames = new(
        ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(1, 9).SelectMany(n => new[] { $"COM{n}", $"LPT{n}" })],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="name"/> with the characters <paramref name="rules"/> don't allow replaced or removed, trimmed, and
    /// cut to <see cref="MaxComponentBytes"/>. A colon becomes <c> -</c> under <see cref="FileNameRules.Portable"/>
    /// (<c>Dune - Part One</c>); the other characters are removed. A name that ends up empty is <see cref="EmptyName"/>.
    /// </summary>
    public static string Clean(string name, FileNameRules rules)
    {
        var cleaned = CleanCharacters(name, rules);
        if (Encoding.UTF8.GetByteCount(cleaned) > MaxComponentBytes)
        {
            cleaned = Trim(CutToBytes(cleaned, MaxComponentBytes), rules);
        }

        return cleaned.Length == 0 ? EmptyName : cleaned;
    }

    /// <summary><paramref name="name"/> with the characters <paramref name="rules"/> don't allow replaced or removed, and trimmed, but not cut to length.</summary>
    public static string CleanCharacters(string name, FileNameRules rules)
    {
        var cleaned = Trim(rules == FileNameRules.Portable ? ReplacePortable(name) : RemovePosix(name), rules);
        return rules == FileNameRules.Portable ? AvoidReservedName(cleaned) : cleaned;
    }

    /// <summary>The longest start of <paramref name="text"/> that fits in <paramref name="maxBytes"/> bytes of UTF-8, never splitting a character.</summary>
    public static string CutToBytes(string text, int maxBytes)
    {
        var builder = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > maxBytes)
            {
                break;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    private static string RemovePosix(string name) => new(name.Where(c => c != '/' && c != '\0').ToArray());

    private static string ReplacePortable(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c == ':')
            {
                while (builder.Length > 0 && builder[^1] == ' ')
                {
                    builder.Length--;
                }

                builder.Append(" -");
            }
            else if (c >= ' ' && c is not ('<' or '>' or '"' or '/' or '\\' or '|' or '?' or '*'))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string Trim(string name, FileNameRules rules) =>
        rules == FileNameRules.Portable ? name.Trim().TrimEnd('.', ' ') : name.Trim();

    /// <summary><c>CON</c>, <c>NUL.txt</c> and the other names Windows reserves get a <c>_</c> after the reserved part.</summary>
    private static string AvoidReservedName(string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        var stem = dot < 0 ? name : name[..dot];
        return ReservedNames.Contains(stem.TrimEnd()) ? stem + "_" + name[stem.Length..] : name;
    }
}
