namespace Fetchshelf.Core.Metadata;

/// <summary>
/// Resolves a config flag name to a <see cref="BookFlags"/> value
/// ("`crude_language` (also `crude`, `language`)" plus the other flag names taken verbatim).
/// </summary>
public static class FlagNameResolver
{
    public static bool TryResolve(string name, out BookFlags flag) =>
        Aliases.TryGetValue(name.Trim().ToLowerInvariant(), out flag);

    private static readonly Dictionary<string, BookFlags> Aliases = new()
    {
        ["crude_language"] = BookFlags.CrudeLanguage,
        ["crude"] = BookFlags.CrudeLanguage,
        ["language"] = BookFlags.CrudeLanguage,
        ["crude language"] = BookFlags.CrudeLanguage,
        ["violence"] = BookFlags.Violence,
        ["some_explicit"] = BookFlags.SomeExplicit,
        ["some explicit"] = BookFlags.SomeExplicit,
        ["explicit"] = BookFlags.Explicit,
        ["abridged"] = BookFlags.Abridged,
        ["lgbt"] = BookFlags.Lgbt,
    };
}
