using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Fetchshelf.Clients.Hardcover;

/// <summary>
/// The Hardcover list a `[[hardcover_list]]` reads: one of the token's user's own lists by its slug, or another user's
/// list by that user's name and the list's slug.
/// </summary>
/// <param name="Owner">The list's owner's Hardcover username, without the <c>@</c>; null for one of the token's user's own lists.</param>
/// <param name="Slug">The list's slug: the last part of its address.</param>
public sealed partial record HardcoverListReference(string? Owner, string Slug)
{
    /// <summary>
    /// Reads a list's slug (<c>books-to-fetch</c>), its owner and slug (<c>@someone/books-to-fetch</c>, also
    /// <c>@someone/lists/books-to-fetch</c>), or its address (<c>https://hardcover.app/@someone/lists/books-to-fetch</c>,
    /// with or without <c>https://</c> and <c>www.</c>, and a trailing <c>/</c>, query or fragment). False when the text
    /// is none of them.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out HardcoverListReference? reference)
    {
        reference = null;
        var trimmed = text?.Trim() ?? string.Empty;
        if (Address().Match(trimmed) is { Success: true } address)
        {
            reference = new HardcoverListReference(address.Groups["owner"].Value, address.Groups["slug"].Value);
        }
        else if (OwnerAndSlug().Match(trimmed) is { Success: true } owned)
        {
            reference = new HardcoverListReference(owned.Groups["owner"].Value, owned.Groups["slug"].Value);
        }
        else if (SlugOnly().IsMatch(trimmed))
        {
            reference = new HardcoverListReference(null, trimmed);
        }

        return reference is not null;
    }

    /// <summary>
    /// The form the configuration keeps it in, and the stored list's key: the slug of one of the token's user's own lists,
    /// else <c>@owner/slug</c>.
    /// </summary>
    public override string ToString() => Owner is null ? Slug : $"@{Owner}/{Slug}";

    private const string Name = "[A-Za-z0-9][A-Za-z0-9_.-]*";

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?hardcover\.app/@(?<owner>" + Name + ")/lists/(?<slug>" + Name + @")/?(?:[?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Address();

    [GeneratedRegex("^@(?<owner>" + Name + ")/(?:lists/)?(?<slug>" + Name + ")/?$")]
    private static partial Regex OwnerAndSlug();

    [GeneratedRegex("^" + Name + "$")]
    private static partial Regex SlugOnly();
}
