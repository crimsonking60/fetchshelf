using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

/// <summary>A library's genre and main-category condition, in its two separate dimensions.</summary>
public class GenreConditionTests
{
    private static TorrentMetadata Book(FictionCategory? fiction, params GenreCategory[] genres) => new()
    {
        MamId = 1,
        VipStatus = VipStatus.NotVip,
        MediaType = MediaType.Ebook,
        FictionCategory = fiction,
        LegacyCategory = LegacyCategory.EbookHistory,
        GenreCategories = genres.ToHashSet(),
        Language = Language.English,
        Title = "A Book",
        MetadataSource = MetadataSource.Mam,
        UploadTime = DateTimeOffset.UnixEpoch,
    };

    private static GenreCondition Condition(GenreCategory[]? allow = null, GenreCategory[]? deny = null, FictionCategory? fiction = null) =>
        new((allow ?? []).ToHashSet(), (deny ?? []).ToHashSet(), fiction);

    // The plan's example: a History library, a Historical fiction library without romance, and a catch-all.
    private static readonly GenreCondition History = Condition([GenreCategory.Historical, GenreCategory.Politics], fiction: FictionCategory.Nonfiction);
    private static readonly GenreCondition HistoricalFiction = Condition([GenreCategory.Historical], [GenreCategory.Romance], FictionCategory.Fiction);

    [Fact]
    public void NonfictionHistorical_IsHistory_NotHistoricalFiction()
    {
        var book = Book(FictionCategory.Nonfiction, GenreCategory.Historical);

        Assert.True(History.Matches(book));
        Assert.False(HistoricalFiction.Matches(book));
    }

    [Fact]
    public void FictionHistorical_IsHistoricalFiction_NotHistory()
    {
        var book = Book(FictionCategory.Fiction, GenreCategory.Historical);

        Assert.False(History.Matches(book));
        Assert.True(HistoricalFiction.Matches(book));
    }

    [Fact]
    public void FictionHistoricalRomance_IsNeither()
    {
        var book = Book(FictionCategory.Fiction, GenreCategory.Historical, GenreCategory.Romance);

        Assert.False(History.Matches(book));
        Assert.False(HistoricalFiction.Matches(book));
        Assert.True(GenreCondition.None.Matches(book));
    }

    [Fact]
    public void ABookWithoutGenres_MatchesNoAllowList_AndNoDenyList()
    {
        var book = Book(FictionCategory.Fiction);

        Assert.False(Condition([GenreCategory.Fantasy]).Matches(book));
        Assert.True(Condition(deny: [GenreCategory.Romance]).Matches(book));
    }

    [Fact]
    public void ABookWithoutAMainCategory_MatchesNoFictionSetting()
    {
        var book = Book(null, GenreCategory.Poetry);

        Assert.False(Condition(fiction: FictionCategory.Fiction).Matches(book));
        Assert.False(Condition(fiction: FictionCategory.Nonfiction).Matches(book));
        Assert.True(Condition([GenreCategory.Poetry]).Matches(book));
    }

    [Fact]
    public void AnyOneAllowedGenre_IsEnough() =>
        Assert.True(Condition([GenreCategory.Fantasy, GenreCategory.ScienceFiction]).Matches(Book(FictionCategory.Fiction, GenreCategory.ScienceFiction, GenreCategory.Humor)));

    [Fact]
    public void IsEmpty_OnlyWithoutAnyCondition()
    {
        Assert.True(GenreCondition.None.IsEmpty);
        Assert.False(Condition(deny: [GenreCategory.Horror]).IsEmpty);
        Assert.False(Condition(fiction: FictionCategory.Fiction).IsEmpty);
    }
}
