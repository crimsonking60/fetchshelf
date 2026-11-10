using System.Security.Cryptography;
using System.Text;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// Computes a `.torrent` file's info-hash: the SHA1 of the raw bencoded bytes of its top-level
/// `info` dictionary value. No bencode/BitTorrent
/// library is already a dependency of this solution, so this is a minimal from-scratch decoder -
/// it only ever needs to locate the `info` value's byte span, never build a full object model.
/// </summary>
public static class TorrentFileInfoHash
{
    /// <summary>
    /// The info-hash of <paramref name="torrentBytes"/>, or false when they are not a torrent file, as when MAM answers
    /// a stale download link with a page instead.
    /// </summary>
    public static bool TryCompute(byte[] torrentBytes, out string infoHash)
    {
        try
        {
            infoHash = Compute(torrentBytes);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            infoHash = string.Empty;
            return false;
        }
    }

    public static string Compute(byte[] torrentBytes)
    {
        if (torrentBytes.Length == 0 || torrentBytes[0] != (byte)'d')
        {
            throw new FormatException("Not a bencoded dictionary.");
        }

        var pos = 1;
        while (torrentBytes[pos] != (byte)'e')
        {
            var (key, afterKey) = ReadString(torrentBytes, pos);
            var valueEnd = SkipValue(torrentBytes, afterKey);

            if (key == "info")
            {
                var hash = SHA1.HashData(torrentBytes.AsSpan(afterKey, valueEnd - afterKey));
                return Convert.ToHexStringLower(hash);
            }

            pos = valueEnd;
        }

        throw new FormatException("Bencoded dictionary has no 'info' key.");
    }

    private static (string Value, int NextPos) ReadString(byte[] data, int pos)
    {
        var colon = Array.IndexOf(data, (byte)':', pos);
        var length = int.Parse(Encoding.ASCII.GetString(data, pos, colon - pos));
        var start = colon + 1;
        return (Encoding.ASCII.GetString(data, start, length), start + length);
    }

    /// <summary>Returns the position immediately after the bencoded value starting at <paramref name="pos"/>.</summary>
    private static int SkipValue(byte[] data, int pos)
    {
        switch ((char)data[pos])
        {
            case 'i':
                var end = Array.IndexOf(data, (byte)'e', pos);
                return end + 1;
            case 'l':
                pos++;
                while (data[pos] != (byte)'e')
                {
                    pos = SkipValue(data, pos);
                }

                return pos + 1;
            case 'd':
                pos++;
                while (data[pos] != (byte)'e')
                {
                    var (_, afterKey) = ReadString(data, pos);
                    pos = SkipValue(data, afterKey);
                }

                return pos + 1;
            default:
                var (_, nextPos) = ReadString(data, pos);
                return nextPos;
        }
    }
}
