using System.Text;
using System.Xml;

namespace Fetchshelf.Core.Metadata;

/// <summary>
/// Makes text from MAM safe to write into XML. XML 1.0 can't hold most control characters (a form feed, say), and
/// an <see cref="XmlWriter"/> throws on them, so one stray character in a description would break the whole document.
/// </summary>
public static class XmlSafeText
{
    /// <summary><paramref name="text"/> without the characters XML can't hold; the text itself when it has none.</summary>
    public static string Clean(string text)
    {
        if (IsClean(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(text[i]).Append(text[i + 1]);
                i++;
            }
            else if (XmlConvert.IsXmlChar(text[i]))
            {
                builder.Append(text[i]);
            }
        }

        return builder.ToString();
    }

    private static bool IsClean(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (!XmlConvert.IsXmlChar(text[i]))
            {
                return false;
            }
        }

        return true;
    }
}
