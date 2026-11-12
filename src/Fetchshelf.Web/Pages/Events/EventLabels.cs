using System.Text.Json;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Pages.Events;

/// <summary>Readable text for an event's type and for the JSON payloads stored with Cleaned and Updated events.</summary>
public static class EventLabels
{
    public static string For(EventKind kind) => kind switch
    {
        EventKind.RemovedFromMam => "Removed from MAM",
        _ => kind.ToString(),
    };

    /// <summary>"Title: Old → New; Authors: A → B". Stored text that isn't the expected JSON is shown as it is.</summary>
    public static string FieldChanges(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return string.Empty;
        }

        try
        {
            var changes = JsonSerializer.Deserialize<List<FieldChange>>(json) ?? [];
            return string.Join("; ", changes.Select(c => $"{FieldName(c.Field)}: {c.From} → {c.To}"));
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>The removed files as a comma-separated list. Stored text that isn't the expected JSON is shown as it is.</summary>
    public static string RemovedFiles(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return string.Empty;
        }

        try
        {
            var files = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            return files.Count == 0 ? "none" : string.Join(", ", files);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>A recorded field's name as the page shows it.</summary>
    public static string FieldName(string field) => field == "LegacyCategory" ? "Category" : field;
}
