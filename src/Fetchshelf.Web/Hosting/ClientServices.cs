using Fetchshelf.Clients.Abs;
using Fetchshelf.Clients.Hardcover;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Account;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Hosting;

/// <summary>The clients of the services Fetchshelf talks to: MAM, qBittorrent, and, when configured, Audiobookshelf and Hardcover.</summary>
public static class ClientServices
{
    public static IServiceCollection AddMam(this IServiceCollection services, FetchshelfSettings settings)
    {
        services.AddSingleton(new MamClientOptions { MamId = settings.MamId, UserAgent = $"Fetchshelf/{AppVersion.Current}" });
        services.AddHttpClient("mam").ConfigureHttpClient((sp, client) =>
        {
            var mamOptions = sp.GetRequiredService<MamClientOptions>();
            client.Timeout = mamOptions.Timeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(mamOptions.UserAgent);
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false }); // MamClient sets the mam_id cookie itself
        // MamClient is a singleton: one mam_id account => one rate-limited client instance.
        services.AddSingleton(sp => new MamClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("mam"),
            sp.GetRequiredService<MamClientOptions>(),
            logger: sp.GetRequiredService<ILogger<MamClient>>()));
        services.AddSingleton<IMetadataLookup, MamMetadataLookup>();
        services.AddSingleton<IMamCoverSource, MamCoverSource>();
        services.AddSingleton<MamAccessGate>();
        services.AddSingleton<MamPageGate>();
        if (settings.BackgroundJobsEnabled)
        {
            // The account's numbers from the start, not only once a job has read them.
            services.AddHostedService<AccountStartupRead>();
            services.AddSingleton(sp => new AccountSnapshotRecorder(
                sp.GetRequiredService<IDbContextFactory<FetchshelfDbContext>>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<AccountSnapshotRecorder>>()));
            services.AddHostedService<AccountSnapshotRecording>();
        }

        return services;
    }

    /// <summary>
    /// The qBittorrent clients, and the configured `[[qbittorrent]]` connections as a list of clients, built per scope,
    /// for the runners that work with every one of them.
    /// </summary>
    public static IServiceCollection AddQBittorrent(this IServiceCollection services, FetchshelfSettings settings)
    {
        services.AddHttpClient("qbittorrent");
        services.AddSingleton<QBittorrentClientFactory>();
        services.AddSingleton<QBittorrentPresenceCache>();
        services.AddScoped<IReadOnlyList<QBittorrentClient>>(sp =>
            QBittorrentConnections.Build(settings, sp.GetRequiredService<QBittorrentClientFactory>()).Select(c => c.Client).ToList());
        return services;
    }

    /// <summary>The Audiobookshelf client and the matcher, when `[audiobookshelf]` is configured.</summary>
    public static IServiceCollection AddAudiobookshelf(this IServiceCollection services, FetchshelfSettings settings)
    {
        if (settings.Audiobookshelf is not { } absSettings)
        {
            return services;
        }

        services.AddSingleton(new AbsClientOptions { BaseUrl = absSettings.Url, Token = absSettings.Token });
        services.AddHttpClient("abs");
        services.AddSingleton(sp => new AbsClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("abs"), sp.GetRequiredService<AbsClientOptions>()));
        services.AddSingleton(new AbsMatcherOptions(absSettings.FetchshelfToAbsMapping()));
        services.AddScoped<AbsMatcher>();
        return services;
    }

    /// <summary>The Hardcover client and the source of list books, when `[hardcover]` is configured.</summary>
    public static IServiceCollection AddHardcover(this IServiceCollection services, FetchshelfSettings settings)
    {
        if (settings.Hardcover is not { } hardcoverSettings)
        {
            return services;
        }

        services.AddSingleton(new HardcoverClientOptions { Token = hardcoverSettings.Token, UserAgent = $"Fetchshelf/{AppVersion.Current}" });
        services.AddHttpClient("hardcover");
        services.AddSingleton(sp => new HardcoverClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("hardcover"), sp.GetRequiredService<HardcoverClientOptions>()));
        // A singleton, so the token's user is looked up once rather than on every run.
        services.AddSingleton(sp => new HardcoverBookSource(sp.GetRequiredService<HardcoverClient>()));
        return services;
    }
}
