namespace Fetchshelf.Core.Metadata;

/// <summary>The four main types a shared-filter `categories` entry is scoped to, Fetchshelf-spec.md §4.3.</summary>
public enum FilterMainType
{
    Audio,
    Ebook,
    Musicology,
    Radio,
}

/// <summary>
/// Resolves a config category name (case-insensitive, with tolerant spellings)
/// to a <see cref="LegacyCategory"/> within a given main type.
/// </summary>
public static class CategoryNameResolver
{
    public static bool TryResolve(FilterMainType mainType, string name, out LegacyCategory category)
    {
        var table = mainType switch
        {
            FilterMainType.Audio => AudioAliases,
            FilterMainType.Ebook => EbookAliases,
            FilterMainType.Musicology => MusicologyAliases,
            FilterMainType.Radio => RadioAliases,
            _ => throw new ArgumentOutOfRangeException(nameof(mainType)),
        };

        return table.TryGetValue(name.Trim().ToLowerInvariant(), out category);
    }

    private static readonly Dictionary<string, LegacyCategory> AudioAliases = new()
    {
        ["action"] = LegacyCategory.AudiobookActionAdventure,
        ["action/adventure"] = LegacyCategory.AudiobookActionAdventure,
        ["art"] = LegacyCategory.AudiobookArt,
        ["biographical"] = LegacyCategory.AudiobookBiographical,
        ["business"] = LegacyCategory.AudiobookBusiness,
        ["computer"] = LegacyCategory.AudiobookComputerInternet,
        ["internet"] = LegacyCategory.AudiobookComputerInternet,
        ["computer/internet"] = LegacyCategory.AudiobookComputerInternet,
        ["crafts"] = LegacyCategory.AudiobookCrafts,
        ["crime/thriller"] = LegacyCategory.AudiobookCrimeThriller,
        ["fantasy"] = LegacyCategory.AudiobookFantasy,
        ["food"] = LegacyCategory.AudiobookFood,
        ["general fiction"] = LegacyCategory.AudiobookGeneralFiction,
        ["general non-fic"] = LegacyCategory.AudiobookGeneralNonFic,
        ["general non fic"] = LegacyCategory.AudiobookGeneralNonFic,
        ["general nonfic"] = LegacyCategory.AudiobookGeneralNonFic,
        ["general non-fiction"] = LegacyCategory.AudiobookGeneralNonFic,
        ["general non fiction"] = LegacyCategory.AudiobookGeneralNonFic,
        ["general nonfiction"] = LegacyCategory.AudiobookGeneralNonFic,
        ["historical fiction"] = LegacyCategory.AudiobookHistoricalFiction,
        ["history"] = LegacyCategory.AudiobookHistory,
        ["home"] = LegacyCategory.AudiobookHomeGarden,
        ["garden"] = LegacyCategory.AudiobookHomeGarden,
        ["home/garden"] = LegacyCategory.AudiobookHomeGarden,
        ["horror"] = LegacyCategory.AudiobookHorror,
        ["humor"] = LegacyCategory.AudiobookHumor,
        ["instructional"] = LegacyCategory.AudiobookInstructional,
        ["juvenile"] = LegacyCategory.AudiobookJuvenile,
        ["language"] = LegacyCategory.AudiobookLanguage,
        ["classics"] = LegacyCategory.AudiobookLiteraryClassics,
        ["literary classics"] = LegacyCategory.AudiobookLiteraryClassics,
        ["math"] = LegacyCategory.AudiobookMathScienceTech,
        ["science"] = LegacyCategory.AudiobookMathScienceTech,
        ["tech"] = LegacyCategory.AudiobookMathScienceTech,
        ["math/science/tech"] = LegacyCategory.AudiobookMathScienceTech,
        ["medical"] = LegacyCategory.AudiobookMedical,
        ["mystery"] = LegacyCategory.AudiobookMystery,
        ["nature"] = LegacyCategory.AudiobookNature,
        ["philosophy"] = LegacyCategory.AudiobookPhilosophy,
        ["pol"] = LegacyCategory.AudiobookPolSocRelig,
        ["soc"] = LegacyCategory.AudiobookPolSocRelig,
        ["relig"] = LegacyCategory.AudiobookPolSocRelig,
        ["pol/soc/relig"] = LegacyCategory.AudiobookPolSocRelig,
        ["recreation"] = LegacyCategory.AudiobookRecreation,
        ["romance"] = LegacyCategory.AudiobookRomance,
        ["sf"] = LegacyCategory.AudiobookScienceFiction,
        ["science fiction"] = LegacyCategory.AudiobookScienceFiction,
        ["self help"] = LegacyCategory.AudiobookSelfHelp,
        ["self-help"] = LegacyCategory.AudiobookSelfHelp,
        ["travel"] = LegacyCategory.AudiobookTravelAdventure,
        ["travel/adventure"] = LegacyCategory.AudiobookTravelAdventure,
        ["true crime"] = LegacyCategory.AudiobookTrueCrime,
        ["urban fantasy"] = LegacyCategory.AudiobookUrbanFantasy,
        ["western"] = LegacyCategory.AudiobookWestern,
        ["ya"] = LegacyCategory.AudiobookYoungAdult,
        ["young adult"] = LegacyCategory.AudiobookYoungAdult,
    };

    private static readonly Dictionary<string, LegacyCategory> EbookAliases = new()
    {
        ["action"] = LegacyCategory.EbookActionAdventure,
        ["action/adventure"] = LegacyCategory.EbookActionAdventure,
        ["art"] = LegacyCategory.EbookArt,
        ["biographical"] = LegacyCategory.EbookBiographical,
        ["business"] = LegacyCategory.EbookBusiness,
        ["comics"] = LegacyCategory.EbookComicsGraphicNovels,
        ["graphic novels"] = LegacyCategory.EbookComicsGraphicNovels,
        ["comics/graphic novels"] = LegacyCategory.EbookComicsGraphicNovels,
        ["computer"] = LegacyCategory.EbookComputerInternet,
        ["internet"] = LegacyCategory.EbookComputerInternet,
        ["computer/internet"] = LegacyCategory.EbookComputerInternet,
        ["crafts"] = LegacyCategory.EbookCrafts,
        ["crime"] = LegacyCategory.EbookCrimeThriller,
        ["thriller"] = LegacyCategory.EbookCrimeThriller,
        ["crime/thriller"] = LegacyCategory.EbookCrimeThriller,
        ["fantasy"] = LegacyCategory.EbookFantasy,
        ["food"] = LegacyCategory.EbookFood,
        ["general fiction"] = LegacyCategory.EbookGeneralFiction,
        ["general non-fic"] = LegacyCategory.EbookGeneralNonFic,
        ["general non fic"] = LegacyCategory.EbookGeneralNonFic,
        ["general nonfic"] = LegacyCategory.EbookGeneralNonFic,
        ["general non-fiction"] = LegacyCategory.EbookGeneralNonFic,
        ["general non fiction"] = LegacyCategory.EbookGeneralNonFic,
        ["general nonfiction"] = LegacyCategory.EbookGeneralNonFic,
        ["historical fiction"] = LegacyCategory.EbookHistoricalFiction,
        ["history"] = LegacyCategory.EbookHistory,
        ["home"] = LegacyCategory.EbookHomeGarden,
        ["garden"] = LegacyCategory.EbookHomeGarden,
        ["home/garden"] = LegacyCategory.EbookHomeGarden,
        ["horror"] = LegacyCategory.EbookHorror,
        ["humor"] = LegacyCategory.EbookHumor,
        ["illusion"] = LegacyCategory.EbookIllusionMagic,
        ["magic"] = LegacyCategory.EbookIllusionMagic,
        ["illusion/magic"] = LegacyCategory.EbookIllusionMagic,
        ["instructional"] = LegacyCategory.EbookInstructional,
        ["juvenile"] = LegacyCategory.EbookJuvenile,
        ["language"] = LegacyCategory.EbookLanguage,
        ["literary classics"] = LegacyCategory.EbookLiteraryClassics,
        ["magazines"] = LegacyCategory.EbookMagazinesNewspapers,
        ["newspapers"] = LegacyCategory.EbookMagazinesNewspapers,
        ["magazines/newspapers"] = LegacyCategory.EbookMagazinesNewspapers,
        ["math"] = LegacyCategory.EbookMathScienceTech,
        ["science"] = LegacyCategory.EbookMathScienceTech,
        ["tech"] = LegacyCategory.EbookMathScienceTech,
        ["math/science/tech"] = LegacyCategory.EbookMathScienceTech,
        ["medical"] = LegacyCategory.EbookMedical,
        ["mixed collections"] = LegacyCategory.EbookMixedCollections,
        ["mystery"] = LegacyCategory.EbookMystery,
        ["nature"] = LegacyCategory.EbookNature,
        ["philosophy"] = LegacyCategory.EbookPhilosophy,
        ["pol"] = LegacyCategory.EbookPolSocRelig,
        ["soc"] = LegacyCategory.EbookPolSocRelig,
        ["relig"] = LegacyCategory.EbookPolSocRelig,
        ["pol/soc/relig"] = LegacyCategory.EbookPolSocRelig,
        ["recreation"] = LegacyCategory.EbookRecreation,
        ["romance"] = LegacyCategory.EbookRomance,
        ["sf"] = LegacyCategory.EbookScienceFiction,
        ["science fiction"] = LegacyCategory.EbookScienceFiction,
        ["self help"] = LegacyCategory.EbookSelfHelp,
        ["self-help"] = LegacyCategory.EbookSelfHelp,
        ["travel"] = LegacyCategory.EbookTravelAdventure,
        ["travel/adventure"] = LegacyCategory.EbookTravelAdventure,
        ["true crime"] = LegacyCategory.EbookTrueCrime,
        ["urban fantasy"] = LegacyCategory.EbookUrbanFantasy,
        ["western"] = LegacyCategory.EbookWestern,
        ["ya"] = LegacyCategory.EbookYoungAdult,
        ["young adult"] = LegacyCategory.EbookYoungAdult,
    };

    private static readonly Dictionary<string, LegacyCategory> MusicologyAliases = new()
    {
        ["guitar/bass tabs"] = LegacyCategory.MusicologyGuitarBassTabs,
        ["individual sheet"] = LegacyCategory.MusicologyIndividualSheet,
        ["individual sheet mp3"] = LegacyCategory.MusicologyIndividualSheetMp3,
        ["instructional book with video"] = LegacyCategory.MusicologyInstructionalBookWithVideo,
        ["instructional media - music"] = LegacyCategory.MusicologyInstructionalMediaMusic,
        ["lick library - ltp/jam with"] = LegacyCategory.MusicologyLickLibraryLtpJamWith,
        ["lick library - techniques/ql"] = LegacyCategory.MusicologyLickLibraryTechniquesQl,
        ["music - complete editions"] = LegacyCategory.MusicologyMusicCompleteEditions,
        ["music book"] = LegacyCategory.MusicologyMusicBook,
        ["music book mp3"] = LegacyCategory.MusicologyMusicBookMp3,
        ["sheet collection"] = LegacyCategory.MusicologySheetCollection,
        ["sheet collection mp3"] = LegacyCategory.MusicologySheetCollectionMp3,
    };

    private static readonly Dictionary<string, LegacyCategory> RadioAliases = new()
    {
        ["comedy"] = LegacyCategory.RadioComedy,
        ["drama"] = LegacyCategory.RadioDrama,
        ["factual"] = LegacyCategory.RadioFactualDocumentary,
        ["documentary"] = LegacyCategory.RadioFactualDocumentary,
        ["factual/documentary"] = LegacyCategory.RadioFactualDocumentary,
        ["reading"] = LegacyCategory.RadioReading,
    };
}
