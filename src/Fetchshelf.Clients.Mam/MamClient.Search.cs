using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fetchshelf.Clients.Mam;

public sealed partial class MamClient
{
    private static readonly TimeSpan SearchPagePoliteness = TimeSpan.FromMilliseconds(400);

    internal static readonly JsonSerializerOptions SearchRequestSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SemaphoreSlim _searchPoliteness = new(1, 1);
    private DateTimeOffset? _lastSearchPageSentAt;

    /// <summary>
    /// Searches MAM torrents against `POST /tor/js/loadSearchJSONbasic.php`, applying the
    /// 400ms-between-pages politeness rule.
    /// </summary>
    public async Task<MamSearchResult> SearchAsync(MamSearchQuery query, CancellationToken cancellationToken = default)
    {
        await ApplySearchPolitenessAsync(cancellationToken);

        var body = BuildSearchRequestBody(query);
        // A byte array has a known length, so it is sent with Content-Length. JsonContent streams the
        // body, which sends it chunked, and MAM's front end rejects that with HTTP 400. The content
        // type is exactly "application/json": with a charset parameter MAM ignores the query and
        // returns its default newest-torrents listing.
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(body, SearchRequestSerializerOptions);
        HttpRequestMessage CreateRequest()
        {
            var content = new ByteArrayContent(bodyBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return new HttpRequestMessage(HttpMethod.Post, BuildUrl("/tor/js/loadSearchJSONbasic.php")) { Content = content };
        }

        using var response = await SendAsync(CreateRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return SearchResponseParser.Parse(document.RootElement);
    }

    private async Task ApplySearchPolitenessAsync(CancellationToken cancellationToken)
    {
        await _searchPoliteness.WaitAsync(cancellationToken);
        try
        {
            if (_lastSearchPageSentAt is { } lastSent)
            {
                var elapsed = _timeProvider.GetUtcNow() - lastSent;
                var remaining = SearchPagePoliteness - elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await _delay(remaining, cancellationToken);
                }
            }

            _lastSearchPageSentAt = _timeProvider.GetUtcNow();
        }
        finally
        {
            _searchPoliteness.Release();
        }
    }

    internal static SearchRequestDto BuildSearchRequestBody(MamSearchQuery query) => new()
    {
        Description = true,
        Isbn = true,
        DlLink = query.ReturnDownloadLink,
        Thumbnail = query.ReturnThumbnail,
        MediaInfo = query.ReturnMediaInfo,
        PerPage = query.PerPage,
        Tor = new TorDto
        {
            Target = query.Target?.WireValue,
            Kind = query.Kind?.ToWireValue(),
            Text = query.Text,
            SearchFields = query.SearchFields.Count > 0 ? query.SearchFields.Select(f => f.ToWireValue()).ToList() : null,
            LanguageIds = query.LanguageIds.Count > 0 ? [.. query.LanguageIds] : null,
            MainCategoryIds = query.MainCategoryIds.Count > 0 ? [.. query.MainCategoryIds] : null,
            CategoryIds = query.CategoryIds.Count > 0 ? [.. query.CategoryIds] : null,
            StartDate = query.StartDate?.ToString("yyyy-MM-dd"),
            EndDate = query.EndDate?.ToString("yyyy-MM-dd"),
            MinSize = query.MinSizeBytes,
            MaxSize = query.MaxSizeBytes,
            MinSeeders = query.MinSeeders,
            MaxSeeders = query.MaxSeeders,
            MinLeechers = query.MinLeechers,
            MaxLeechers = query.MaxLeechers,
            MinSnatched = query.MinSnatched,
            MaxSnatched = query.MaxSnatched,
            BrowseFlagsHideVsShow = query.ShowFlags is { } showFlags ? (showFlags ? 0 : 1) : null,
            BrowseFlags = query.Flags.Count > 0 ? [.. query.Flags] : null,
            Hash = query.Hash,
            Id = query.Id,
            SortType = query.SortOrder == MamSortOrder.NewestFirst ? null : query.SortOrder.ToWireValue(),
            StartNumber = query.PageOffset != 0 ? query.PageOffset : null,
        },
    };
}
