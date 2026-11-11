using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data.Notifications;

/// <summary>What a saved event means for notifications: a grab, a link, a torrent MAM no longer has.</summary>
public static class EventNotifications
{
    /// <summary>
    /// The notification for the event, or null for a kind that sends none. <paramref name="metadata"/> is the event's
    /// torrent, for its title and authors; without it the message names the MAM id.
    /// </summary>
    public static Notification? From(EventEntity @event, TorrentMetadata? metadata)
    {
        var name = metadata?.DisplayName() ?? (@event.MamId is { } mamId ? $"torrent {mamId}" : "a torrent");
        var link = @event.TorrentId is { } id ? $"/torrents/{Uri.EscapeDataString(id)}" : null;
        var fields = new Dictionary<string, object?>
        {
            ["torrent_id"] = @event.TorrentId,
            ["mam_id"] = @event.MamId,
            ["title"] = metadata?.Title,
            ["authors"] = metadata?.Authors,
        };

        switch (@event.Kind)
        {
            case EventKind.Grabbed:
                var how = string.Join(", ", new[] { @event.GrabberName, DescribeCost(@event.Cost, @event.WedgeUsed) }.Where(part => !string.IsNullOrEmpty(part)));
                fields["grabber"] = @event.GrabberName;
                fields["cost"] = @event.Cost;
                fields["wedge_used"] = @event.WedgeUsed;
                return new Notification(
                    NotificationEvent.Grabbed, $"Grabbed {metadata?.Title ?? name}",
                    how.Length == 0 ? $"Grabbed {name}." : $"Grabbed {name} ({how}).",
                    @event.OccurredAt, fields, link, Item: name);
            case EventKind.Linked:
                var library = @event.LinkerName ?? @event.LibraryPath;
                fields["library"] = @event.LinkerName;
                fields["library_path"] = @event.LibraryPath;
                return new Notification(
                    NotificationEvent.Linked, $"Linked {metadata?.Title ?? name}",
                    library is null ? $"Linked {name}." : $"Linked {name} into {library}.",
                    @event.OccurredAt, fields, link, Item: name);
            case EventKind.RemovedFromMam:
                return new Notification(
                    NotificationEvent.RemovedFromMam, "MAM no longer has a torrent", $"MAM no longer has {name}.",
                    @event.OccurredAt, fields, link);
            default:
                return null;
        }
    }

    /// <summary>The cost as a reader knows it: "freeleech", "VIP", "wedge used". Null for a cost not recorded.</summary>
    public static string? DescribeCost(string? cost, bool? wedgeUsed) => cost switch
    {
        null or "" => null,
        nameof(Core.AutoDownloader.SelectedTorrentCost.GlobalFreeleech) => "freeleech",
        nameof(Core.AutoDownloader.SelectedTorrentCost.PersonalFreeleech) => "personal freeleech",
        nameof(Core.AutoDownloader.SelectedTorrentCost.Vip) => "VIP",
        nameof(Core.AutoDownloader.SelectedTorrentCost.Ratio) => "ratio",
        nameof(Core.AutoDownloader.SelectedTorrentCost.UseWedge) or nameof(Core.AutoDownloader.SelectedTorrentCost.TryWedge) =>
            wedgeUsed == true ? "wedge used" : "no wedge needed",
        _ => cost,
    };
}
