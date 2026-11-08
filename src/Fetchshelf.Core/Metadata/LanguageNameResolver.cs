namespace Fetchshelf.Core.Metadata;

/// <summary>
/// Resolves a config language name (case-insensitive, with tolerant spellings) to
/// a <see cref="Language"/>.
/// </summary>
public static class LanguageNameResolver
{
    public static bool TryResolve(string name, out Language language) =>
        Aliases.TryGetValue(name.Trim().ToLowerInvariant(), out language);

    /// <summary>The language's name for display, with spaces: "Brazilian Portuguese", "Ancient Greek".</summary>
    public static string DisplayName(Language language) =>
        string.Concat(language.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));

    private static readonly Dictionary<string, Language> Aliases = new()
    {
        ["english"] = Language.English,
        ["afrikaans"] = Language.Afrikaans,
        ["arabic"] = Language.Arabic,
        ["bengali"] = Language.Bengali,
        ["bosnian"] = Language.Bosnian,
        ["bulgarian"] = Language.Bulgarian,
        ["burmese"] = Language.Burmese,
        ["cantonese"] = Language.Cantonese,
        ["catalan"] = Language.Catalan,
        ["chinese"] = Language.Chinese,
        ["croatian"] = Language.Croatian,
        ["czech"] = Language.Czech,
        ["danish"] = Language.Danish,
        ["dutch"] = Language.Dutch,
        ["estonian"] = Language.Estonian,
        ["farsi"] = Language.Farsi,
        ["finnish"] = Language.Finnish,
        ["french"] = Language.French,
        ["german"] = Language.German,
        ["greek"] = Language.Greek,
        ["ancient greek"] = Language.AncientGreek,
        ["greek ancient"] = Language.AncientGreek,
        ["greek, ancient"] = Language.AncientGreek,
        ["gujarati"] = Language.Gujarati,
        ["hebrew"] = Language.Hebrew,
        ["hindi"] = Language.Hindi,
        ["hungarian"] = Language.Hungarian,
        ["icelandic"] = Language.Icelandic,
        ["indonesian"] = Language.Indonesian,
        ["irish"] = Language.Irish,
        ["italian"] = Language.Italian,
        ["japanese"] = Language.Japanese,
        ["javanese"] = Language.Javanese,
        ["kannada"] = Language.Kannada,
        ["korean"] = Language.Korean,
        ["lithuanian"] = Language.Lithuanian,
        ["latin"] = Language.Latin,
        ["latvian"] = Language.Latvian,
        ["malay"] = Language.Malay,
        ["malayalam"] = Language.Malayalam,
        ["manx"] = Language.Manx,
        ["marathi"] = Language.Marathi,
        ["norwegian"] = Language.Norwegian,
        ["polish"] = Language.Polish,
        ["portuguese"] = Language.Portuguese,
        ["bp"] = Language.BrazilianPortuguese,
        ["brazilian"] = Language.BrazilianPortuguese,
        ["brazilian portuguese"] = Language.BrazilianPortuguese,
        ["brazilian portuguese (bp)"] = Language.BrazilianPortuguese,
        ["punjabi"] = Language.Punjabi,
        ["romanian"] = Language.Romanian,
        ["russian"] = Language.Russian,
        ["scottish"] = Language.ScottishGaelic,
        ["scottish gaelic"] = Language.ScottishGaelic,
        ["gaelic"] = Language.ScottishGaelic,
        ["sanskrit"] = Language.Sanskrit,
        ["serbian"] = Language.Serbian,
        ["slovenian"] = Language.Slovenian,
        ["spanish"] = Language.Spanish,
        ["castilian"] = Language.CastilianSpanish,
        ["castilian spanish"] = Language.CastilianSpanish,
        ["swedish"] = Language.Swedish,
        ["tagalog"] = Language.Tagalog,
        ["tamil"] = Language.Tamil,
        ["telugu"] = Language.Telugu,
        ["thai"] = Language.Thai,
        ["turkish"] = Language.Turkish,
        ["ukrainian"] = Language.Ukrainian,
        ["urdu"] = Language.Urdu,
        ["vietnamese"] = Language.Vietnamese,
        ["other"] = Language.Other,
    };
}
