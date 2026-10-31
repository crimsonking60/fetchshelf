using System.Globalization;
using System.Text.Json;

namespace Fetchshelf.Clients.Mam;

/// <summary>
/// MAM's JSON responses encode some fields as either a string or a number depending on context
/// (confirmed against the original Rust implementation's `serde::{bool_string_or_number,
/// num_string_or_number, string_or_number}`). These helpers tolerate either encoding.
/// </summary>
internal static class MamJson
{
    public static double GetDouble(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => double.Parse(element.GetString()!, CultureInfo.InvariantCulture),
        JsonValueKind.Number => element.GetDouble(),
        _ => throw new FormatException($"Expected a string or number, got {element.ValueKind}."),
    };

    public static long GetInt64(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => long.Parse(element.GetString()!, CultureInfo.InvariantCulture),
        JsonValueKind.Number => element.GetInt64(),
        _ => throw new FormatException($"Expected a string or number, got {element.ValueKind}."),
    };

    public static bool GetBool(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() is "yes" or "1",
        JsonValueKind.Number => element.GetInt64() == 1,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new FormatException($"Expected a string, number, or boolean, got {element.ValueKind}."),
    };

    public static string GetString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Number => element.GetRawText(),
        _ => throw new FormatException($"Expected a string or number, got {element.ValueKind}."),
    };
}
