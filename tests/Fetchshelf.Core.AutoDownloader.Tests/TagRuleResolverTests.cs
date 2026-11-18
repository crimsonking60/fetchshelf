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

    private static TagRule MatchAllRule(
        string? category = null, IReadOnlyList<string>? tags = null, string? client = null, string? savePath = null) => new()
        {
            Filter = new TorrentFilter(),
            Category = category,
            Tags = tags ?? [],
            Client = client,
            SavePath = savePath,
        };

    [Fact]
    public void Resolve_AutograbCategorySet_OverridesMatchingRuleCategory()
    {
        var candidate = BuildCandidate();
        var rules = new List<TagRule> { MatchAllRule(category: "rule-category") };

        var (placement, _) = TagRuleResolver.Resolve(candidate, new DownloadPlacement(Category: "autograb-category"), rules);

        Assert.Equal("autograb-category", placement.Category);
    }

    [Fact]
    public void FirstMatchingRule_WinsCategory_WhenNoAutograbCategory()
    {
        var candidate = BuildCandidate();
        var rules = new List<TagRule> { MatchAllRule(category: "first"), MatchAllRule(category: "second") };

        var (placement, _) = TagRuleResolver.Resolve(candidate, DownloadPlacement.None, rules);

        Assert.Equal("first", placement.Category);
    }

    [Fact]
    public void Resolve_SeveralMatchingRules_TagsAreTheirUnion()
    {
        var candidate = BuildCandidate();
        var rules = new List<TagRule> { MatchAllRule(tags: ["new"]), MatchAllRule(tags: ["fiction"]) };

        var (_, tags) = TagRuleResolver.Resolve(candidate, DownloadPlacement.None, rules);

        Assert.Equal(new HashSet<string> { "new", "fiction" }, tags);
    }

    [Fact]
    public void Resolve_ClientAndSavePath_FromTheFirstMatchingRuleThatSetsEach()
    {
        var candidate = BuildCandidate();
        var rules = new List<TagRule>
        {
            MatchAllRule(category: "books"),
            MatchAllRule(client: "vpn"),
            MatchAllRule(client: "other", savePath: "/data/audiobooks"),
        };

        var (placement, _) = TagRuleResolver.Resolve(candidate, DownloadPlacement.None, rules);

        Assert.Equal(new DownloadPlacement("books", "vpn", "/data/audiobooks"), placement);
    }

    [Fact]
    public void Resolve_BlockClientAndSavePath_OverrideTheRules()
    {
        var candidate = BuildCandidate();
        var rules = new List<TagRule> { MatchAllRule(category: "books", client: "vpn", savePath: "/data/rule") };

        var (placement, _) = TagRuleResolver.Resolve(candidate, new DownloadPlacement(Client: "home", SavePath: "/data/own"), rules);

        Assert.Equal(new DownloadPlacement("books", "home", "/data/own"), placement);
    }
}
