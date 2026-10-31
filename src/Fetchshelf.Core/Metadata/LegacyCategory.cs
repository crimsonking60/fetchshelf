namespace Fetchshelf.Core.Metadata;

/// <summary>
/// Tracker legacy categories (fixed list). Enum values are the tracker's own
/// category ids, which are unique across all media types.
/// </summary>
public enum LegacyCategory
{
    // Audiobook
    AudiobookActionAdventure = 39,
    AudiobookArt = 49,
    AudiobookBiographical = 50,
    AudiobookBusiness = 83,
    AudiobookComputerInternet = 51,
    AudiobookCrafts = 97,
    AudiobookCrimeThriller = 40,
    AudiobookFantasy = 41,
    AudiobookFood = 106,
    AudiobookGeneralFiction = 42,
    AudiobookGeneralNonFic = 52,
    AudiobookHistoricalFiction = 98,
    AudiobookHistory = 54,
    AudiobookHomeGarden = 55,
    AudiobookHorror = 43,
    AudiobookHumor = 99,
    AudiobookInstructional = 84,
    AudiobookJuvenile = 44,
    AudiobookLanguage = 56,
    AudiobookLiteraryClassics = 45,
    AudiobookMathScienceTech = 57,
    AudiobookMedical = 85,
    AudiobookMystery = 87,
    AudiobookNature = 119,
    AudiobookPhilosophy = 88,
    AudiobookPolSocRelig = 58,
    AudiobookRecreation = 59,
    AudiobookRomance = 46,
    AudiobookScienceFiction = 47,
    AudiobookSelfHelp = 53,
    AudiobookTravelAdventure = 89,
    AudiobookTrueCrime = 100,
    AudiobookUrbanFantasy = 108,
    AudiobookWestern = 48,
    AudiobookYoungAdult = 111,

    // Ebook
    EbookActionAdventure = 60,
    EbookArt = 71,
    EbookBiographical = 72,
    EbookBusiness = 90,
    EbookComicsGraphicNovels = 61,
    EbookComputerInternet = 73,
    EbookCrafts = 101,
    EbookCrimeThriller = 62,
    EbookFantasy = 63,
    EbookFood = 107,
    EbookGeneralFiction = 64,
    EbookGeneralNonFic = 74,
    EbookHistoricalFiction = 102,
    EbookHistory = 76,
    EbookHomeGarden = 77,
    EbookHorror = 65,
    EbookHumor = 103,
    EbookIllusionMagic = 115,
    EbookInstructional = 91,
    EbookJuvenile = 66,
    EbookLanguage = 78,
    EbookLiteraryClassics = 67,
    EbookMagazinesNewspapers = 79,
    EbookMathScienceTech = 80,
    EbookMedical = 92,
    EbookMixedCollections = 118,
    EbookMystery = 94,
    EbookNature = 120,
    EbookPhilosophy = 95,
    EbookPolSocRelig = 81,
    EbookRecreation = 82,
    EbookRomance = 68,
    EbookScienceFiction = 69,
    EbookSelfHelp = 75,
    EbookTravelAdventure = 96,
    EbookTrueCrime = 104,
    EbookUrbanFantasy = 109,
    EbookWestern = 70,
    EbookYoungAdult = 112,

    // Musicology
    MusicologyGuitarBassTabs = 19,
    MusicologyIndividualSheet = 20,
    MusicologyIndividualSheetMp3 = 24,
    MusicologyInstructionalBookWithVideo = 126,
    MusicologyInstructionalMediaMusic = 22,
    MusicologyLickLibraryLtpJamWith = 113,
    MusicologyLickLibraryTechniquesQl = 114,
    MusicologyMusicCompleteEditions = 17,
    MusicologyMusicBook = 26,
    MusicologyMusicBookMp3 = 27,
    MusicologySheetCollection = 30,
    MusicologySheetCollectionMp3 = 31,

    // Radio
    RadioComedy = 127,
    RadioDrama = 130,
    RadioFactualDocumentary = 128,
    RadioReading = 132,
}

/// <summary>
/// Groups legacy categories by the media type they belong to, so cleaning rules such as
/// "Ebook with an audio legacy category becomes Audiobook" can be implemented.
/// </summary>
public static class LegacyCategoryGroups
{
    public static readonly IReadOnlySet<LegacyCategory> Audiobook = new HashSet<LegacyCategory>
    {
        LegacyCategory.AudiobookActionAdventure, LegacyCategory.AudiobookArt, LegacyCategory.AudiobookBiographical,
        LegacyCategory.AudiobookBusiness, LegacyCategory.AudiobookComputerInternet, LegacyCategory.AudiobookCrafts,
        LegacyCategory.AudiobookCrimeThriller, LegacyCategory.AudiobookFantasy, LegacyCategory.AudiobookFood,
        LegacyCategory.AudiobookGeneralFiction, LegacyCategory.AudiobookGeneralNonFic, LegacyCategory.AudiobookHistoricalFiction,
        LegacyCategory.AudiobookHistory, LegacyCategory.AudiobookHomeGarden, LegacyCategory.AudiobookHorror,
        LegacyCategory.AudiobookHumor, LegacyCategory.AudiobookInstructional, LegacyCategory.AudiobookJuvenile,
        LegacyCategory.AudiobookLanguage, LegacyCategory.AudiobookLiteraryClassics, LegacyCategory.AudiobookMathScienceTech,
        LegacyCategory.AudiobookMedical, LegacyCategory.AudiobookMystery, LegacyCategory.AudiobookNature,
        LegacyCategory.AudiobookPhilosophy, LegacyCategory.AudiobookPolSocRelig, LegacyCategory.AudiobookRecreation,
        LegacyCategory.AudiobookRomance, LegacyCategory.AudiobookScienceFiction, LegacyCategory.AudiobookSelfHelp,
        LegacyCategory.AudiobookTravelAdventure, LegacyCategory.AudiobookTrueCrime, LegacyCategory.AudiobookUrbanFantasy,
        LegacyCategory.AudiobookWestern, LegacyCategory.AudiobookYoungAdult,
    };

    public static readonly IReadOnlySet<LegacyCategory> Ebook = new HashSet<LegacyCategory>
    {
        LegacyCategory.EbookActionAdventure, LegacyCategory.EbookArt, LegacyCategory.EbookBiographical,
        LegacyCategory.EbookBusiness, LegacyCategory.EbookComicsGraphicNovels, LegacyCategory.EbookComputerInternet,
        LegacyCategory.EbookCrafts, LegacyCategory.EbookCrimeThriller, LegacyCategory.EbookFantasy,
        LegacyCategory.EbookFood, LegacyCategory.EbookGeneralFiction, LegacyCategory.EbookGeneralNonFic,
        LegacyCategory.EbookHistoricalFiction, LegacyCategory.EbookHistory, LegacyCategory.EbookHomeGarden,
        LegacyCategory.EbookHorror, LegacyCategory.EbookHumor, LegacyCategory.EbookIllusionMagic,
        LegacyCategory.EbookInstructional, LegacyCategory.EbookJuvenile, LegacyCategory.EbookLanguage,
        LegacyCategory.EbookLiteraryClassics, LegacyCategory.EbookMagazinesNewspapers, LegacyCategory.EbookMathScienceTech,
        LegacyCategory.EbookMedical, LegacyCategory.EbookMixedCollections, LegacyCategory.EbookMystery,
        LegacyCategory.EbookNature, LegacyCategory.EbookPhilosophy, LegacyCategory.EbookPolSocRelig,
        LegacyCategory.EbookRecreation, LegacyCategory.EbookRomance, LegacyCategory.EbookScienceFiction,
        LegacyCategory.EbookSelfHelp, LegacyCategory.EbookTravelAdventure, LegacyCategory.EbookTrueCrime,
        LegacyCategory.EbookUrbanFantasy, LegacyCategory.EbookWestern, LegacyCategory.EbookYoungAdult,
    };

    public static readonly IReadOnlySet<LegacyCategory> Musicology = new HashSet<LegacyCategory>
    {
        LegacyCategory.MusicologyGuitarBassTabs, LegacyCategory.MusicologyIndividualSheet,
        LegacyCategory.MusicologyIndividualSheetMp3, LegacyCategory.MusicologyInstructionalBookWithVideo,
        LegacyCategory.MusicologyInstructionalMediaMusic, LegacyCategory.MusicologyLickLibraryLtpJamWith,
        LegacyCategory.MusicologyLickLibraryTechniquesQl, LegacyCategory.MusicologyMusicCompleteEditions,
        LegacyCategory.MusicologyMusicBook, LegacyCategory.MusicologyMusicBookMp3,
        LegacyCategory.MusicologySheetCollection, LegacyCategory.MusicologySheetCollectionMp3,
    };

    public static readonly IReadOnlySet<LegacyCategory> Radio = new HashSet<LegacyCategory>
    {
        LegacyCategory.RadioComedy, LegacyCategory.RadioDrama,
        LegacyCategory.RadioFactualDocumentary, LegacyCategory.RadioReading,
    };

    public static bool IsAudiobookCategory(LegacyCategory category) => Audiobook.Contains(category);
}
