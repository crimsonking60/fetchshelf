using System.Globalization;
using System.Text.Json;

namespace Fetchshelf.Clients.Mam;

/// <summary>
/// Parses a raw `loadSearchJSONbasic.php` response into <see cref="MamSearchResult"/>. Field
/// shapes (including the nested-JSON-string encoding of `author_info`/`narrator_info`/
/// `series_info`/`categories`) follow what MAM actually sends.
/// </summary>
internal static class SearchResponseParser
{
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    public static MamSearchResult Parse(JsonElement root)
    {
        if (TryGetErrorMessage(root, out var errorMessage))
        {
            if (errorMessage == "Nothing returned, out of 0")
            {
                return new MamSearchResult { PerPage = 0, Start = 0, Total = 0, Found = 0, Data = [] };
            }

            throw new InvalidOperationException($"MAM search failed: {errorMessage}");
        }

        var data = new List<MamTorrentRecord>();
        foreach (var item in root.GetProperty("data").EnumerateArray())
        {
            data.Add(ParseTorrent(item));
        }

        return new MamSearchResult
        {
            PerPage = root.GetProperty("perpage").GetInt32(),
            Start = root.GetProperty("start").GetInt32(),
            Total = root.GetProperty("total").GetInt32(),
            Found = root.GetProperty("found").GetInt32(),
            Data = data,
        };
    }

    private static bool TryGetErrorMessage(JsonElement root, out string message)
    {
        message = "";
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("error", out var errorElement)
            && errorElement.ValueKind == JsonValueKind.String)
        {
            message = errorElement.GetString() ?? "";
            return true;
        }

        return false;
    }

    private static MamTorrentRecord ParseTorrent(JsonElement element)
    {
        var isbn = GetOptionalString(element, "isbn");
        var description = GetOptionalString(element, "description");
        var downloadHash = GetOptionalString(element, "dl");

        return new MamTorrentRecord
        {
            MamId = MamJson.GetInt64(element.GetProperty("id")),
            Title = MamJson.GetString(element.GetProperty("title")),
            Authors = ParseIdNameMap(element.GetProperty("author_info")),
            Narrators = ParseIdNameMap(element.GetProperty("narrator_info")),
            Series = ParseSeriesInfo(element.GetProperty("series_info")),
            Isbn = isbn,
            Description = description,
            MediaTypeId = element.TryGetProperty("mediatype", out var mediaTypeElement) ? (int)MamJson.GetInt64(mediaTypeElement) : 0,
            OldMainCategoryId = element.TryGetProperty("main_cat", out var mainCatElement) ? (int)MamJson.GetInt64(mainCatElement) : 0,
            FictionCategoryId = element.TryGetProperty("maincat", out var fictionElement) ? (int)MamJson.GetInt64(fictionElement) : 0,
            LegacyCategoryId = (int)MamJson.GetInt64(element.GetProperty("category")),
            GenreCategoryIds = ParseIntArray(element.GetProperty("categories")),
            LanguageId = (int)MamJson.GetInt64(element.GetProperty("language")),
            FlagsRaw = (int)MamJson.GetInt64(element.GetProperty("browseflags")),
            FileTypesRaw = MamJson.GetString(element.GetProperty("filetype")),
            FileCount = (int)MamJson.GetInt64(element.GetProperty("numfiles")),
            SizeRaw = MamJson.GetString(element.GetProperty("size")),
            TagsRaw = element.TryGetProperty("tags", out var tagsElement) ? MamJson.GetString(tagsElement) : "",
            UploaderId = ParseUploaderId(element),
            UploaderName = ParseUploaderName(element),
            UploadedAt = DateTimeOffset.ParseExact(
                MamJson.GetString(element.GetProperty("added")), DateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
            Seeders = MamJson.GetInt64(element.GetProperty("seeders")),
            Leechers = MamJson.GetInt64(element.GetProperty("leechers")),
            TimesCompleted = element.TryGetProperty("times_completed", out var timesEl) ? MamJson.GetInt64(timesEl) : 0,
            IsFree = element.TryGetProperty("free", out var freeEl) && MamJson.GetBool(freeEl),
            IsPersonalFreeleech = element.TryGetProperty("personal_freeleech", out var pfEl) && MamJson.GetBool(pfEl),
            IsFreeleechVip = element.TryGetProperty("fl_vip", out var flVipEl) && MamJson.GetBool(flVipEl),
            IsVip = element.TryGetProperty("vip", out var vipEl) && MamJson.GetBool(vipEl),
            VipExpireUnixSeconds = element.TryGetProperty("vip_expire", out var vipExpireEl) ? MamJson.GetInt64(vipExpireEl) : 0,
            DownloadHash = downloadHash,
        };
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var text = MamJson.GetString(value);
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static long ParseUploaderId(JsonElement element)
    {
        if (element.TryGetProperty("owner", out var ownerElement)
            && (ownerElement.ValueKind == JsonValueKind.Number || ownerElement.ValueKind == JsonValueKind.String))
        {
            return MamJson.GetInt64(ownerElement);
        }

        return TryGetOwnership(element, out var id, out _) ? id : 0;
    }

    private static string ParseUploaderName(JsonElement element)
    {
        if (element.TryGetProperty("owner_name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
        {
            var name = nameElement.GetString();
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }

        return TryGetOwnership(element, out _, out var ownershipName) ? ownershipName : "";
    }

    /// <summary>Falls back to the `ownership` 2-tuple `[id, name]` when `owner`/`owner_name` are absent, as in some MAM responses.</summary>
    private static bool TryGetOwnership(JsonElement element, out long id, out string name)
    {
        id = 0;
        name = "";
        if (!element.TryGetProperty("ownership", out var ownershipElement) || ownershipElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var items = ownershipElement.EnumerateArray().ToList();
        if (items.Count != 2 || items[1].ValueKind != JsonValueKind.String)
        {
            return false;
        }

        id = MamJson.GetInt64(items[0]);
        name = items[1].GetString() ?? "";
        return true;
    }

    private static IReadOnlyDictionary<long, string> ParseIdNameMap(JsonElement element) =>
        ParseNestedOrDefault(element, inner =>
        {
            var map = new Dictionary<long, string>();
            if (inner.ValueKind != JsonValueKind.Object)
            {
                return map;
            }

            foreach (var property in inner.EnumerateObject())
            {
                if (long.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    map[id] = MamJson.GetString(property.Value);
                }
            }

            return map;
        }, []);

    private static IReadOnlyList<MamSeriesInfo> ParseSeriesInfo(JsonElement element) =>
        ParseNestedOrDefault(element, inner =>
        {
            var list = new List<MamSeriesInfo>();
            if (inner.ValueKind != JsonValueKind.Object)
            {
                return list;
            }

            foreach (var property in inner.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var items = property.Value.EnumerateArray().ToList();
                if (items.Count < 2)
                {
                    continue;
                }

                list.Add(new MamSeriesInfo(MamJson.GetString(items[0]), MamJson.GetString(items[1])));
            }

            return list;
        }, []);

    private static IReadOnlyList<int> ParseIntArray(JsonElement element) =>
        ParseNestedOrDefault(element, inner =>
        {
            var list = new List<int>();
            if (inner.ValueKind != JsonValueKind.Array)
            {
                return list;
            }

            foreach (var item in inner.EnumerateArray())
            {
                list.Add((int)MamJson.GetInt64(item));
            }

            return list;
        }, []);

    /// <summary>
    /// Some MAM fields are JSON-encoded as a string containing JSON rather than a nested object/array
    /// directly. Falls back to <paramref name="fallback"/> on any shape the parser doesn't recognize.
    /// </summary>
    private static T ParseNestedOrDefault<T>(JsonElement element, Func<JsonElement, T> parse, T fallback)
    {
        try
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var text = element.GetString();
                if (string.IsNullOrEmpty(text))
                {
                    return fallback;
                }

                using var document = JsonDocument.Parse(text);
                return parse(document.RootElement);
            }

            return parse(element);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }
}
