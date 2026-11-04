using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Fetchshelf.Clients.Hardcover;

/// <summary>
/// Typed client for Hardcover's GraphQL API. It only reads: the token's user, the books with a reading status,
/// and the books on one of the user's lists. Variables travel in the request body, never inside the query text.
/// </summary>
public sealed class HardcoverClient
{
    /// <summary>Books per request; a long shelf is read a page at a time.</summary>
    public const int PageSize = 100;

    /// <summary>The longest <c>Retry-After</c> worth waiting for before giving up on a rate-limited request.</summary>
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(2);

    private const string BookFields = """
        id
        title
        slug
        contributions { contribution author { name } }
        book_series { position series { name } }
        image { url }
        """;

    private const string MeQuery = "query Me { me { id username } }";

    private const string BooksWithStatusQuery = $$$"""
        query BooksWithStatus($userId: Int!, $statusId: Int!, $limit: Int!, $offset: Int!) {
          user_books(
            where: {user_id: {_eq: $userId}, status_id: {_eq: $statusId}}
            order_by: {id: asc}
            limit: $limit
            offset: $offset
          ) {
            book { {{{BookFields}}} }
          }
        }
        """;

    private const string ListBooksQuery = $$$"""
        query ListBooks($userId: Int!, $slug: String!, $limit: Int!, $offset: Int!) {
          lists(where: {user_id: {_eq: $userId}, slug: {_eq: $slug}}) {
            id
            name
            list_books(order_by: {id: asc}, limit: $limit, offset: $offset) {
              book { {{{BookFields}}} }
            }
          }
        }
        """;

    private readonly HttpClient _httpClient;
    private readonly HardcoverClientOptions _options;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public HardcoverClient(HttpClient httpClient, HardcoverClientOptions options, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _httpClient = httpClient;
        _options = options;
        _delay = delay ?? ((wait, ct) => Task.Delay(wait, ct));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", StripBearer(options.Token));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
    }

    /// <summary>The user the token belongs to. Also the cheapest way to check that the token works.</summary>
    public async Task<HardcoverUser> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var data = await QueryAsync(MeQuery, new { }, cancellationToken);
        var me = data.GetProperty("me");
        // Hardcover answers with a list holding the one user; accept a single object as well.
        var user = me.ValueKind == JsonValueKind.Array
            ? me.EnumerateArray().Cast<JsonElement?>().FirstOrDefault()
            : me;
        if (user is not { ValueKind: JsonValueKind.Object } found)
        {
            throw new HardcoverException("Hardcover did not say which user the token belongs to.");
        }

        return new HardcoverUser(found.GetProperty("id").GetInt64(), found.GetProperty("username").GetString() ?? string.Empty);
    }

    /// <summary>Every book the user has given this reading status, oldest first.</summary>
    public async Task<IReadOnlyList<HardcoverBook>> GetBooksWithStatusAsync(
        long userId, HardcoverReadingStatus status, CancellationToken cancellationToken = default)
    {
        var books = new List<HardcoverBook>();
        for (var offset = 0; ; offset += PageSize)
        {
            var data = await QueryAsync(
                BooksWithStatusQuery, new { userId, statusId = (int)status, limit = PageSize, offset }, cancellationToken);
            var page = ReadBooks(data.GetProperty("user_books"));
            books.AddRange(page.Books);
            if (page.Rows < PageSize)
            {
                return books;
            }
        }
    }

    /// <summary>Every book on the user's list with this slug, in the order they were added.</summary>
    /// <exception cref="HardcoverException">The user has no list with this slug.</exception>
    public async Task<IReadOnlyList<HardcoverBook>> GetListBooksAsync(
        long userId, string slug, CancellationToken cancellationToken = default)
    {
        var books = new List<HardcoverBook>();
        for (var offset = 0; ; offset += PageSize)
        {
            var data = await QueryAsync(ListBooksQuery, new { userId, slug, limit = PageSize, offset }, cancellationToken);
            var list = data.GetProperty("lists").EnumerateArray().Cast<JsonElement?>().FirstOrDefault()
                ?? throw new HardcoverException($"You have no Hardcover list with the slug '{slug}'.");
            var page = ReadBooks(list.GetProperty("list_books"));
            books.AddRange(page.Books);
            if (page.Rows < PageSize)
            {
                return books;
            }
        }
    }

    private static (List<HardcoverBook> Books, int Rows) ReadBooks(JsonElement rows)
    {
        var books = new List<HardcoverBook>();
        var count = 0;
        foreach (var row in rows.EnumerateArray())
        {
            count++;
            if (row.TryGetProperty("book", out var book) && book.ValueKind == JsonValueKind.Object)
            {
                books.Add(ReadBook(book));
            }
        }

        return (books, count);
    }

    private static HardcoverBook ReadBook(JsonElement book) => new()
    {
        Id = book.GetProperty("id").GetInt64(),
        Title = StringOrNull(book, "title") ?? string.Empty,
        Slug = StringOrNull(book, "slug"),
        Authors = Array(book, "contributions")
            // A contribution without a role is the author's own; others are "Narrator", "Illustrator" and so on.
            .Where(c => StringOrNull(c, "contribution") is null or "Author")
            .Select(c => c.TryGetProperty("author", out var author) ? StringOrNull(author, "name") : null)
            .OfType<string>()
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList(),
        Series = Array(book, "book_series")
            .Select(s => (Name: s.TryGetProperty("series", out var series) ? StringOrNull(series, "name") : null, Entry: s))
            .Where(s => !string.IsNullOrEmpty(s.Name))
            .Select(s => new HardcoverSeriesEntry(
                s.Name!,
                s.Entry.TryGetProperty("position", out var position) && position.ValueKind == JsonValueKind.Number ? position.GetDouble() : null))
            .ToList(),
        CoverUrl = book.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.Object ? StringOrNull(image, "url") : null,
    };

    private static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    private static string? StringOrNull(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Runs one query and returns its <c>data</c>, after waiting once for a rate limit to pass.</summary>
    private async Task<JsonElement> QueryAsync(string query, object variables, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var response = await _httpClient.PostAsJsonAsync(_options.Endpoint, new { query, variables }, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt == 1)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60);
                if (wait <= MaxRetryAfter)
                {
                    await _delay(wait, cancellationToken);
                    continue;
                }
            }

            ThrowForStatus(response, body);
            return ReadData(body);
        }
    }

    private static void ThrowForStatus(HttpResponseMessage response, string body)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                return;
            case HttpStatusCode.Unauthorized:
                throw new HardcoverAuthenticationException(
                    "Hardcover rejected the token: it is wrong or has expired. Create a new one in Hardcover's account settings.");
            case HttpStatusCode.Forbidden:
                var scope = ReadErrorField(body, "scope");
                throw new HardcoverAuthenticationException(scope is null
                    ? $"Hardcover refused the request: {ReadErrorField(body, "error_description") ?? ReadErrorField(body, "message") ?? ReadErrorField(body, "error") ?? "no reason given"}."
                    : $"The Hardcover token lacks the {scope} permission. Create a token that has it.");
            case HttpStatusCode.TooManyRequests:
                throw new HardcoverException("Hardcover's rate limit is used up; try again later.");
            default:
                throw new HardcoverException(
                    $"Hardcover answered {(int)response.StatusCode} {response.ReasonPhrase}{(ReadErrorField(body, "error") is { } error ? $": {error}" : string.Empty)}.");
        }
    }

    private static JsonElement ReadData(string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new HardcoverException("Hardcover's answer was not valid JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                var messages = errors.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.Object ? StringOrNull(e, "message") : e.ValueKind == JsonValueKind.String ? e.GetString() : null)
                    .OfType<string>();
                throw new HardcoverException($"Hardcover refused the query: {string.Join("; ", messages)}");
            }

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                throw new HardcoverException("Hardcover's answer had no data.");
            }

            return data.Clone();
        }
    }

    private static string? ReadErrorField(string body, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? StringOrNull(document.RootElement, name) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string StripBearer(string token)
    {
        var trimmed = token.Trim();
        return trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? trimmed["Bearer ".Length..].Trim() : trimmed;
    }

    /// <summary>For log lines: the reading status as Hardcover's website names it.</summary>
    public static string Describe(HardcoverReadingStatus status) => status switch
    {
        HardcoverReadingStatus.WantToRead => "Want to Read",
        HardcoverReadingStatus.CurrentlyReading => "Currently Reading",
        HardcoverReadingStatus.Read => "Read",
        HardcoverReadingStatus.Paused => "Paused",
        HardcoverReadingStatus.DidNotFinish => "Did Not Finish",
        _ => ((int)status).ToString(CultureInfo.InvariantCulture),
    };
}
