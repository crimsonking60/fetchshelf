using System.Text.Json;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Notifications;

namespace Fetchshelf.Web.Pages.Events;

/// <summary>Readable text for an event's type, its details, and the JSON payloads stored with Cleaned and Updated events.</summary>
public static class EventLabels
{
    /// <summary>Details longer than this are shortened on the page, with the whole text a click away.</summary>
    public const int DetailsLimit = 200;

    /// <summary>How many characters of shortened details the page shows.</summary>
    public const int ShortenedLength = 180;

    /// <summary>What the event did, as the Details column shows it.</summary>
    public static string Details(EventEntity e) => e.Kind switch
    {
        EventKind.Linked => $"Linked{(string.IsNullOrEmpty(e.LinkerName) ? "" : $" by {e.LinkerName}")} to {e.LibraryPath}",
        EventKind.Cleaned => $"Cleaned{(string.IsNullOrEmpty(e.LinkerName) ? "" : $" ({e.LinkerName})")}, removed files: {RemovedFiles(e.FilesRemovedJson)}",
        EventKind.Unlinked => $"Unlinked{(string.IsNullOrEmpty(e.LinkerName) ? "" : $" ({e.LinkerName})")}, removed files: {RemovedFiles(e.FilesRemovedJson)}",
        EventKind.Updated => $"{(e.EditedByHand == true ? "Edited by hand: " : "")}{FieldChanges(e.FieldChangesJson)}",
        EventKind.Grabbed => $"Grabbed{(string.IsNullOrEmpty(e.GrabberName) ? "" : $" by {e.GrabberName}")} (cost: {e.Cost})",
        EventKind.RemovedFromMam => "Removed from MAM",
        EventKind.BonusPurchase => $"Bought {EventNotifications.BonusPurchaseDescription(e.BonusSpend, e.BonusUploadGb)}"
            + (e.PointsBefore is { } before && e.PointsAfter is { } after
                ? $" with bonus points, {BonusPlanner.Points(before)} → {BonusPlanner.Points(after)}"
                : ""),
        _ => string.Empty,
    };

    /// <summary>
    /// The first <see cref="ShortenedLength"/> characters of details longer than <see cref="DetailsLimit"/>, for the
    /// page to show with a link to the rest; null for details short enough to show whole. A character made of two
    /// UTF-16 units is never cut in half.
    /// </summary>
    public static string? Shortened(string details)
    {
        if (details.Length <= DetailsLimit)
        {
            return null;
        }

        var length = char.IsHighSurrogate(details[ShortenedLength - 1]) ? ShortenedLength - 1 : ShortenedLength;
        return details[..length];
    }

    public static string For(EventKind kind) => kind switch
    {
        EventKind.RemovedFromMam => "Removed from MAM",
        EventKind.BonusPurchase => "Bonus purchase",
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
