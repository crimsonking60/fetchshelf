using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Jobs;

namespace Fetchshelf.Web.Hosting;

/// <summary>
/// The runners behind the jobs, each registered by type. The settings values they are built with are registered once, as
/// option records or as the configured lists, so a runner takes them by type rather than by position: a new setting for
/// a runner goes into its options record.
/// </summary>
public static class RunnerServices
{
    /// <summary>The library organizer: the format lists, the libraries, and the Link and Clean runners.</summary>
    public static IServiceCollection AddOrganizer(this IServiceCollection services, FetchshelfSettings settings)
    {
        services.AddSingleton(new FormatPreferenceLists
        {
            AudioTypes = settings.AudioTypes,
            EbookTypes = settings.EbookTypes,
            MusicTypes = settings.MusicTypes,
            RadioTypes = settings.RadioTypes,
            Libraries = settings.Library
                .Where(library => library.Name is not null && (library.AudioTypes is not null || library.EbookTypes is not null))
                .ToDictionary(library => library.Name!, library => new LibraryFormatLists(library.AudioTypes, library.EbookTypes), StringComparer.Ordinal),
        });
        services.AddSingleton<IReadOnlyList<Library>>(settings.Library);
        services.AddSingleton(new LinkRunnerOptions(
            settings.ExcludeNarratorInLibraryDir,
            settings.Languages.Count > 0 && LanguageNameResolver.TryResolve(settings.Languages[0], out var firstLanguage) ? firstLanguage : null));
        services.AddScoped<ClientStatusChecker>();
        services.AddScoped<LinkRunner>();
        services.AddScoped<DownloadProgressChecker>();
        services.AddScoped<Cleaner>();
        services.AddSingleton<LibraryWriteGate>();
        if (settings.Seeding is { } seeding)
        {
            services.AddSingleton(seeding);
            services.AddScoped<SeedingRunner>();
            if (seeding.WarnBeforeRemoval)
            {
                services.AddSingleton(new SeedingHold(seeding.Requirement));
            }
        }

        // The Torrents page's bulk actions, run in the background one at a time.
        services.AddSingleton<BulkOperationQueue>();
        services.AddScoped<BulkActionRunner>();
        services.AddHostedService<BulkOperationWorker>();
        return services;
    }

    /// <summary>
    /// The auto-downloader: the tag rules, selection, the runner of every kind of block that is configured, the safety
    /// buffers and the Download runner.
    /// </summary>
    public static IServiceCollection AddAutoDownloader(this IServiceCollection services, FetchshelfSettings settings)
    {
        services.AddSingleton<IReadOnlyList<TagRule>>(settings.Tag
            .Select(t => new TagRule
            {
                Filter = SharedFilterConfigMapper.ToTorrentFilter(t),
                Category = t.Category,
                Tags = t.Tags,
                Client = t.Client,
                SavePath = t.SavePath,
            })
            .ToList());
        services.AddScoped<SelectionRunner>();
        services.AddScoped<AutograbberRunner>();
        if (ListDefinition.AllOf(settings).Count > 0)
        {
            services.AddScoped<ListImporter>();
        }

        if (settings.Follow is { } followSettings)
        {
            services.AddSingleton(followSettings);
            services.AddScoped<FollowRunner>();
            if (followSettings.AudiobookshelfSeries != AudiobookshelfSeriesMode.Off)
            {
                // The Follow job finds it by its optional parameter; [audiobookshelf] is required with it.
                services.AddScoped<AudiobookshelfSeriesSync>();
            }
            services.AddScoped<FollowService>();
        }

        if (settings.Upgrade is { } upgradeSettings)
        {
            services.AddSingleton(upgradeSettings);
            services.AddScoped<UpgradeRunner>();
        }

        if (settings.Bonus is { } bonusSettings)
        {
            services.AddSingleton(bonusSettings);
            services.AddScoped<BonusRunner>();
        }

        // The buffers each queued torrent is held to: the Download job checks them, the Account page shows what they leave.
        services.AddSingleton(new SafetyBuffers(settings.UnsatBuffer, settings.WedgeBuffer, BlockBuffersOf(settings)));
        services.AddSingleton(new DownloadRunnerOptions(settings.MinRatio, settings.AddTorrentsStopped));
        services.AddScoped<DownloadRunner>();
        services.AddScoped<RetroactiveTagApplier>();
        return services;
    }

    /// <summary>
    /// Every block's own buffers, under the grabber name its selections carry: a named autograb's name, a list's job
    /// name, `Upgrade`, and every "Follow: …" name for `[follow]`.
    /// </summary>
    public static IReadOnlyList<BlockBuffers> BlockBuffersOf(FetchshelfSettings settings) =>
        settings.Autograb.Where(a => a.Name is not null).Select(a => new BlockBuffers(a.Name!, a.UnsatBuffer, a.WedgeBuffer))
            .Concat(ListDefinition.AllOf(settings).Select(list => new BlockBuffers(list.JobName, list.UnsatBuffer, list.WedgeBuffer)))
            .Concat(settings.Upgrade is { } upgrade ? [new BlockBuffers(JobNames.Upgrade, upgrade.UnsatBuffer, upgrade.WedgeBuffer)] : [])
            .Concat(settings.Follow is { } follow ? [new BlockBuffers(FollowRunner.GrabberPrefix, follow.UnsatBuffer, follow.WedgeBuffer, IsPrefix: true)] : [])
            .ToList();
}
