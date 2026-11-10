using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.Organizer;

/// <summary>Applies a category-and-tags setting such as `on_cleaned`, `on_unlinked` or `on_invalid_torrent` to a torrent.</summary>
public static class QBittorrentActions
{
    /// <summary>Sets the setting's category, creating it in qBittorrent if needed, and adds its tags.</summary>
    public static async Task ApplyAsync(QBittorrentClient client, string hash, QBittorrentCategoryAndTags setting, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(setting.Category))
        {
            await client.SetCategoryAsync(hash, setting.Category, cancellationToken);
        }

        if (setting.Tags.Count > 0)
        {
            await client.AddTagsAsync(hash, setting.Tags, cancellationToken);
        }
    }

    /// <summary>What <see cref="ApplyAsync"/> does with <paramref name="setting"/>: "category invalid, tags a, b".</summary>
    public static string Describe(QBittorrentCategoryAndTags setting)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(setting.Category))
        {
            parts.Add($"category {setting.Category}");
        }

        if (setting.Tags.Count > 0)
        {
            parts.Add($"{(setting.Tags.Count == 1 ? "tag" : "tags")} {string.Join(", ", setting.Tags)}");
        }

        return string.Join(", ", parts);
    }
}
