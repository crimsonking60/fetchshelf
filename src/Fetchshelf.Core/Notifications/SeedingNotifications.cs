using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Notifications;

/// <summary>The `unsatisfied_not_seeding` message.</summary>
public static class SeedingNotifications
{
    public static Notification NotSeeding(string torrentId, long mamId, string title, SeedingEstimate estimate, SeedingRequirement requirement, DateTimeOffset at) =>
        new(
            NotificationEvent.UnsatisfiedNotSeeding,
            $"Stopped seeding before it was satisfied: {title}",
            $"{title} isn't seeding. {SeedingEstimator.Describe(estimate, requirement)}",
            at,
            new Dictionary<string, object?>
            {
                ["torrent_id"] = torrentId,
                ["mam_id"] = mamId,
                ["seeded_hours"] = Math.Floor(estimate.Seeded.TotalHours),
                ["hours_needed"] = Math.Ceiling(estimate.StillNeeded.TotalHours),
                ["deadline"] = estimate.Deadline,
            },
            $"/torrents/{Uri.EscapeDataString(torrentId)}",
            Item: title);
}
