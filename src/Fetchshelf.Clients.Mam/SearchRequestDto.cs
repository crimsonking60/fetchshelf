using System.Text.Json.Serialization;

namespace Fetchshelf.Clients.Mam;

/// <summary>
/// Wire shape for `POST /tor/js/loadSearchJSONbasic.php`. Property names are the ones MAM expects;
/// don't rename them.
/// </summary>
internal sealed class SearchRequestDto
{
    [JsonPropertyName("description")]
    public bool Description { get; set; }

    [JsonPropertyName("mediaInfo")]
    public bool MediaInfo { get; set; }

    [JsonPropertyName("dlLink")]
    public bool DlLink { get; set; }

    [JsonPropertyName("isbn")]
    public bool Isbn { get; set; }

    [JsonPropertyName("thumbnail")]
    public bool Thumbnail { get; set; }

    [JsonPropertyName("perpage")]
    public long? PerPage { get; set; }

    [JsonPropertyName("tor")]
    public required TorDto Tor { get; set; }
}

internal sealed class TorDto
{
    [JsonPropertyName("searchIn")]
    public string? Target { get; set; }

    [JsonPropertyName("searchType")]
    public string? Kind { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("srchIn")]
    public List<string>? SearchFields { get; set; }

    [JsonPropertyName("browse_lang")]
    public List<int>? LanguageIds { get; set; }

    [JsonPropertyName("main_cat")]
    public List<int>? MainCategoryIds { get; set; }

    [JsonPropertyName("cat")]
    public List<int>? CategoryIds { get; set; }

    [JsonPropertyName("startDate")]
    public string? StartDate { get; set; }

    [JsonPropertyName("endDate")]
    public string? EndDate { get; set; }

    [JsonPropertyName("minSize")]
    public long? MinSize { get; set; }

    [JsonPropertyName("maxSize")]
    public long? MaxSize { get; set; }

    [JsonPropertyName("minSeeders")]
    public long? MinSeeders { get; set; }

    [JsonPropertyName("maxSeeders")]
    public long? MaxSeeders { get; set; }

    [JsonPropertyName("minLeechers")]
    public long? MinLeechers { get; set; }

    [JsonPropertyName("maxLeechers")]
    public long? MaxLeechers { get; set; }

    [JsonPropertyName("minSnatched")]
    public long? MinSnatched { get; set; }

    [JsonPropertyName("maxSnatched")]
    public long? MaxSnatched { get; set; }

    [JsonPropertyName("browseFlagsHideVsShow")]
    public int? BrowseFlagsHideVsShow { get; set; }

    [JsonPropertyName("browseFlags")]
    public List<int>? BrowseFlags { get; set; }

    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    [JsonPropertyName("id")]
    public long? Id { get; set; }

    [JsonPropertyName("sortType")]
    public string? SortType { get; set; }

    [JsonPropertyName("startNumber")]
    public long? StartNumber { get; set; }
}
