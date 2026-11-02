using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.AutoDownloader.Tests;

public class TagRuleResolverTests
{
    private static TorrentFilterCandidate BuildCandidate(MediaType mediaType = MediaType.Audiobook) => new()
    {
        Metadata = new TorrentMetadata
        {
            MamId = 1,
            VipStatus = VipStatus.NotVip,
            MediaType = mediaType,
            LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
            Language = Language.English,
            Title = "Some Title",
            MetadataSource = MetadataSource.Mam,
            UploadTime = DateTimeOffset.UtcNow,
        },
        UploaderName = "uploader",
        Seeders = 1,
        Leechers = 1,
        TimesCompleted = 1,
    };

    private static TagRule MatchAllRule(string? category = null, IReadOnlyList<string>? tags = null) => new()
    {
        Filter = new TorrentFilter(),
        Category = category,
        Tags = tags ?? [],
    };

    [Fact]
    public void Resolve_AutograbCategorySet_OverridesMatchingRuleCategory()
    {
        var candidate = BuildCandidate();
        var rules = new List<TagRule> { MatchAllRule(category: "rule-category") };

        var (category, _) = TagRuleResolver.Resolve(candidate, "autograb-category", rules);

        Assert.Equal("autograb-category", category);
    }

    [Fact]
    public void FirstMatchingRule_WinsCategory_WhenNoAutograbCategory()
    {
        var candidate = BuildCandidate();
        var rules = new List<TagRule> { MatchAllRule(category: "first"), MatchAllRule(category: "second") };

        var (category, _) = TagRuleResolver.Resolve(candidate, null, rules);

        Assert.Equal("first", category);
    }

    [Fact]
    public void Resolve_SeveralMatchingRules_TagsAreTheirUnion()
    {
        var candidate = BuildCandidate();
        var rules = new List<TagRule> { MatchAllRule(tags: ["new"]), MatchAllRule(tags: ["fiction"]) };

        var (_, tags) = TagRuleResolver.Resolve(candidate, null, rules);

        Assert.Equal(new HashSet<string> { "new", "fiction" }, tags);
    }
}
