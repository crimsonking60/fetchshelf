using Fetchshelf.Core.Jobs;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Notifications;
using Fetchshelf.Web.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Hosting;

/// <summary>
/// Notifications: one queue every part of the app publishes to, and the sender that works through it. The notifier exists
/// without any [[notify]] block, as a sink that takes nothing, so code that publishes needs no check. Whether anything is
/// sent is decided once, from the configuration read at startup (<see cref="NotificationsOn"/>): it gates the sender, the
/// hit-and-run check and the MAM wiring alike.
/// </summary>
public static class NotificationServices
{
    /// <summary>Whether any `[[notify]]` block is configured, so messages are sent at all.</summary>
    public static bool NotificationsOn(FetchshelfSettings settings) => NotifyTarget.From(settings).Count > 0;

    public static IServiceCollection AddFetchshelfNotifications(this IServiceCollection services, FetchshelfSettings settings)
    {
        var notifyTargets = NotifyTarget.From(settings);
        services.AddSingleton(sp => new Notifier(notifyTargets, sp.GetRequiredService<ILogger<Notifier>>()));
        services.AddSingleton<INotificationSink>(sp => sp.GetRequiredService<Notifier>());
        services.AddHttpClient("notify").ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton(sp => new NotificationDelivery(
            notifyTargets,
            () => sp.GetRequiredService<IHttpClientFactory>().CreateClient("notify"),
            settings.WebUrl,
            sp.GetRequiredService<ILogger<NotificationDelivery>>(),
            timeProvider: sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IJobRunListener>(sp => new JobRunNotifier(sp.GetRequiredService<INotificationSink>()));
        services.AddSingleton(sp => new NotificationStateStore(
            sp.GetRequiredService<IDbContextFactory<FetchshelfDbContext>>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new HitAndRunWatcher(
            sp.GetRequiredService<NotificationStateStore>(),
            sp.GetRequiredService<INotificationSink>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<HitAndRunWatcher>>()));
        services.AddSingleton(sp => new BudgetSpentWatcher(
            sp.GetRequiredService<NotificationStateStore>(),
            sp.GetRequiredService<INotificationSink>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<BudgetSpentWatcher>>()));
        if (notifyTargets.Count > 0)
        {
            services.AddSingleton<IHostedService>(sp => new NotificationSender(
                sp.GetRequiredService<Notifier>(),
                sp.GetRequiredService<NotificationDelivery>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<NotificationSender>>()));
            services.AddSingleton<IHostedService>(sp => new HitAndRunCheck(sp.GetRequiredService<HitAndRunWatcher>()));
        }

        return services;
    }
}
