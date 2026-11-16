using Fetchshelf.Core.Jobs;
using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Web.Opds;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;

namespace Fetchshelf.Web.Hosting;

/// <summary>What the host is built with, besides the command line.</summary>
/// <param name="Paths">Where the configuration file, the database and the log folder are.</param>
public sealed record FetchshelfAppOptions(FetchshelfPaths Paths);

/// <summary>A built app, with the settings it was built from and the `[backup]` folder, if any, resolved to a full path.</summary>
public sealed record FetchshelfBuiltApp(WebApplication App, FetchshelfSettings Settings, string? BackupFolder);

/// <summary>
/// The Fetchshelf host: <see cref="Build"/> puts the app together, from loading the configuration to the middleware, and
/// <see cref="RunAsync"/> is the whole life of the process, command-line modes included.
/// </summary>
public static class FetchshelfApp
{
    /// <summary>
    /// Builds the app: loads the configuration, registers every service and maps the endpoints. It doesn't touch the
    /// database. <c>builder.Build()</c> runs on the caller's path, which <c>WebApplicationFactory</c> relies on.
    /// </summary>
    /// <exception cref="ConfigurationException">The configuration can't be used; the message says why.</exception>
    public static FetchshelfBuiltApp Build(string[] args, FetchshelfAppOptions options)
    {
        var paths = options.Paths;
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog(dispose: true);

        var settings = ConfigLoader.Load(paths.ConfigFile);
        Directory.CreateDirectory(paths.DataFolder);
        // The backup folder can only be checked once the data file's place is known: a relative one is under its folder.
        var backupFolder = settings.Backup?.ResolveFolder(paths.DataFile);

        builder.WebHost.UseUrls(ListenAddress.Build(settings));
        builder.Services.AddSingleton(settings);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services
            .AddFetchshelfData(paths)
            .AddFetchshelfNotifications(settings)
            .AddQBittorrent(settings)
            .AddMam(settings)
            .AddAudiobookshelf(settings)
            .AddHardcover(settings)
            .AddOrganizer(settings)
            .AddAutoDownloader(settings)
            .AddFetchshelfJobs(settings)
            .AddFetchshelfWeb(settings, paths);

        var app = builder.Build();
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseRouting();
        app.MapStaticAssets();
        app.MapRazorPages()
           .WithStaticAssets();
        app.MapThemeEndpoint();
        app.MapHealthChecks("/health");
        if (settings.Opds is not null)
        {
            app.MapOpdsEndpoints();
        }

        return new FetchshelfBuiltApp(app, settings, backupFolder);
    }

    /// <summary>
    /// Runs Fetchshelf: <c>--healthcheck</c> and <c>--check-config</c> answer and exit; otherwise it builds the app,
    /// backs up and upgrades the database, runs <c>--update-search-title</c> and exits if asked, registers the jobs and
    /// serves until stopped. Returns the process's exit code.
    /// </summary>
    public static async Task<int> RunAsync(string[] args)
    {
        var paths = FetchshelfPaths.FromEnvironment();
        if (args.Contains(HealthProbe.Argument))
        {
            return await HealthProbe.RunAsync(paths.ConfigFile, TimeSpan.FromSeconds(5));
        }

        if (args.Contains(ConfigCheck.Argument))
        {
            return ConfigCheck.Run(paths.ConfigFile, Console.Out);
        }

        Log.Logger = FetchshelfLogging
            .Build(paths.LogFolder, Environment.GetEnvironmentVariable("FETCHSHELF_LOG"))
            .CreateLogger();
        Log.Information("Fetchshelf {Version} starting", AppVersion.Current);

        FetchshelfBuiltApp built;
        try
        {
            built = Build(args, new FetchshelfAppOptions(paths));
        }
        catch (ConfigurationException ex)
        {
            Log.Fatal("Cannot start with {ConfigFile}: {Reason:l}", paths.ConfigFile, ex.Message);
            await Log.CloseAndFlushAsync();
            return 1;
        }

        var (app, settings, backupFolder) = built;
        if (!PrepareDatabase(app, settings, paths, backupFolder))
        {
            await Log.CloseAndFlushAsync();
            return 1;
        }

        if (args.Contains("--update-search-title"))
        {
            using var maintenanceScope = app.Services.CreateScope();
            var maintenanceDbContext = maintenanceScope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            Log.Information("Update search title started");
            var (rekeyedTorrents, rekeyedSelections) = await TitleMaintenanceRunner.RunAsync(maintenanceDbContext);
            Log.Information("Update search title done: {Torrents} torrent and {Selections} selection keys changed", rekeyedTorrents, rekeyedSelections);
            await Log.CloseAndFlushAsync();
            return 0;
        }

        if (settings.BackgroundJobsEnabled)
        {
            FetchshelfJobs.RegisterOrganizerJobs(app.Services, settings, paths.DataFile, backupFolder);
            FetchshelfJobs.RegisterAutoDownloaderJobs(app.Services, settings);
        }

        if (NotificationServices.NotificationsOn(settings))
        {
            FetchshelfJobs.ConnectMamNotifications(app.Services);
        }

        // Before the schedulers start, which plan each job's first run from its last one.
        var jobRegistry = app.Services.GetRequiredService<IJobRegistry>();
        await jobRegistry.LoadLastRunsAsync();
        app.Lifetime.ApplicationStopping.Register(jobRegistry.InterruptAll);

        // The runs the shutdown stopped are written in the background once they have ended; give them a moment to end and reach
        // the database. This waits once the hosted services have stopped but before the host is disposed, as the writes need its
        // services. Any run that isn't written by then is marked interrupted at the next start.
        app.Lifetime.ApplicationStopped.Register(() =>
        {
            async Task RecordedAsync()
            {
                await jobRegistry.RunsEndedAsync();
                await jobRegistry.RunRecordsWrittenAsync();
            }

            if (!RecordedAsync().Wait(TimeSpan.FromSeconds(5)))
            {
                Log.Warning("Some job runs could not be recorded before Fetchshelf stopped");
            }
        });

        await app.RunAsync();
        return 0;
    }

    /// <summary>
    /// Backs the database up when `[backup]` asks for a backup before upgrades and one is due, then upgrades it. False when
    /// that backup failed, so Fetchshelf must not start.
    /// </summary>
    private static bool PrepareDatabase(WebApplication app, FetchshelfSettings settings, FetchshelfPaths paths, string? backupFolder)
    {
        using var startupScope = app.Services.CreateScope();
        var startupDbContext = startupScope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        if (settings.Backup is { BeforeUpgrade: true } && backupFolder is not null)
        {
            try
            {
                if (DatabaseBackup.BeforeUpgrade(startupDbContext, paths.DataFile, backupFolder, AppVersion.Current,
                        BackupSettings.KeepBeforeUpgrade, DateTimeOffset.UtcNow) is { } backup)
                {
                    Log.Information("Backed up the database to {Backup} before upgrading it", backup.Path);
                }
            }
            catch (Exception ex)
            {
                // Upgrading without the copy [backup] promises would be worse than not starting.
                Log.Fatal(ex, "Cannot back up the database before upgrading it, so Fetchshelf does not start: {Reason:l}. "
                    + "To start without that backup, set before_upgrade = false in [backup]", ex.Message);
                return false;
            }
        }

        DatabaseMigrator.Migrate(startupDbContext, settings.DatabaseWal, message => Log.Information("{Upgrade}", message));
        return true;
    }
}
