using Fetchshelf.Clients.Abs;
using Fetchshelf.Clients.Hardcover;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Notifications;
using Fetchshelf.Data.Opds;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web;
using Fetchshelf.Web.Hosting;
using Fetchshelf.Web.Jobs;
using Fetchshelf.Web.Notifications;
using Fetchshelf.Web.Opds;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;

var paths = FetchshelfPaths.FromEnvironment();
if (args.Contains(HealthProbe.Argument))
{
    Environment.ExitCode = await HealthProbe.RunAsync(paths.ConfigFile, TimeSpan.FromSeconds(5));
    return;
}

if (args.Contains(ConfigCheck.Argument))
{
    Environment.ExitCode = ConfigCheck.Run(paths.ConfigFile, Console.Out);
    return;
}

Log.Logger = FetchshelfLogging
    .Build(paths.LogFolder, Environment.GetEnvironmentVariable("FETCHSHELF_LOG"))
    .CreateLogger();
Log.Information("Fetchshelf {Version} starting", AppVersion.Current);

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog(dispose: true);

var configFilePath = paths.ConfigFile;
FetchshelfSettings settings;
try
{
    settings = ConfigLoader.Load(configFilePath);
}
catch (ConfigurationException ex)
{
    Log.Fatal("Cannot start with {ConfigFile}: {Reason:l}", configFilePath, ex.Message);
    await Log.CloseAndFlushAsync();
    Environment.ExitCode = 1;
    return;
}

var dataFilePath = paths.DataFile;
var dataFileDirectory = paths.DataFolder;
Directory.CreateDirectory(dataFileDirectory);

// The backup folder can only be checked once the data file's place is known: a relative one is under its folder.
string? backupFolder = null;
if (settings.Backup is { } configuredBackup)
{
    try
    {
        backupFolder = configuredBackup.ResolveFolder(dataFilePath);
    }
    catch (ConfigurationException ex)
    {
        Log.Fatal("Cannot start with {ConfigFile}: {Reason:l}", configFilePath, ex.Message);
        await Log.CloseAndFlushAsync();
        Environment.ExitCode = 1;
        return;
    }
}

// Keep the keys that encrypt antiforgery cookies next to the database: the default location is inside the
// container, so a new container (every image update) would otherwise reject the cookie browsers still hold.
builder.Services.AddDataProtection()
    .SetApplicationName("Fetchshelf")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataFileDirectory, "keys")));

// The two cookies those keys encrypt are named after this installation, so a second Fetchshelf on the same host
// (browsers share cookies across ports) can't overwrite them and fail every form with HTTP 400.
var instanceId = InstanceId.LoadOrCreate(dataFileDirectory);
builder.Services.AddAntiforgery(options => options.Cookie.Name = $".Fetchshelf.Antiforgery.{instanceId}");
builder.Services.Configure<Microsoft.AspNetCore.Mvc.CookieTempDataProviderOptions>(
    options => options.Cookie.Name = $".Fetchshelf.TempData.{instanceId}");

builder.WebHost.UseUrls(ListenAddress.Build(settings));

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<IJobRunStore, JobRunStore>();

// Notifications: one queue every part of the app publishes to, and the sender that works through it. The notifier
// exists without any [[notify]] block, as a sink that takes nothing, so code that publishes needs no check. Whether
// anything is sent is decided once, here, from the configuration read at startup: notificationsOn gates the sender, the
// hit-and-run check and the MAM wiring alike.
var notifyTargets = NotifyTarget.From(settings);
var notificationsOn = notifyTargets.Count > 0;
builder.Services.AddSingleton(sp => new Notifier(notifyTargets, sp.GetRequiredService<ILogger<Notifier>>()));
builder.Services.AddSingleton<INotificationSink>(sp => sp.GetRequiredService<Notifier>());
builder.Services.AddHttpClient("notify").ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton(sp => new NotificationDelivery(
    notifyTargets,
    () => sp.GetRequiredService<IHttpClientFactory>().CreateClient("notify"),
    settings.WebUrl,
    sp.GetRequiredService<ILogger<NotificationDelivery>>(),
    timeProvider: sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IJobRunListener>(sp => new JobRunNotifier(sp.GetRequiredService<INotificationSink>()));
// Each Download run wakes the watcher that follows downloads into the library (see DownloadProgressWatcher below).
builder.Services.AddSingleton<DownloadWatchSignal>();
builder.Services.AddSingleton<IJobRunListener>(sp => new DownloadWatchWaker(sp.GetRequiredService<DownloadWatchSignal>()));
builder.Services.AddSingleton(sp => new NotificationStateStore(
    sp.GetRequiredService<IDbContextFactory<FetchshelfDbContext>>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new HitAndRunWatcher(
    sp.GetRequiredService<NotificationStateStore>(),
    sp.GetRequiredService<INotificationSink>(),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILogger<HitAndRunWatcher>>()));
builder.Services.AddSingleton(sp => new BudgetSpentWatcher(
    sp.GetRequiredService<NotificationStateStore>(),
    sp.GetRequiredService<INotificationSink>(),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILogger<BudgetSpentWatcher>>()));
if (notificationsOn)
{
    builder.Services.AddSingleton<IHostedService>(sp => new NotificationSender(
        sp.GetRequiredService<Notifier>(),
        sp.GetRequiredService<NotificationDelivery>(),
        sp.GetRequiredService<TimeProvider>(),
        sp.GetRequiredService<ILogger<NotificationSender>>()));
    builder.Services.AddSingleton<IHostedService>(sp => new HitAndRunCheck(sp.GetRequiredService<HitAndRunWatcher>()));
}

// The registry is told which failures are MAM refusing the session or account, so the Errors page can group them, and
// who to tell when a run ends, so a job that starts failing can be sent to a phone.
builder.Services.AddSingleton<IJobRegistry>(sp => new JobRegistry(
    sp.GetRequiredService<ILogger<JobRegistry>>(),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<IJobRunStore>(),
    JobFailureClassifier.Classify,
    sp.GetServices<IJobRunListener>()));
builder.Services.AddHttpClient("qbittorrent");
builder.Services.AddSingleton<QBittorrentClientFactory>();
builder.Services.AddSingleton<QBittorrentPresenceCache>();
builder.Services.TryAddSingleton(TimeProvider.System);
if (settings.Opds is not null)
{
    builder.Services.AddSingleton<OpdsCatalogCache>();
    // Covers read out of the books live next to the database, like the form keys, so they survive a new container.
    builder.Services.AddSingleton(sp => new OpdsCoverCache(
        Path.Combine(dataFileDirectory, "covers"), sp.GetRequiredService<ILogger<OpdsCoverCache>>()));
}

builder.Services.AddSingleton(new MamClientOptions { MamId = settings.MamId, UserAgent = $"Fetchshelf/{AppVersion.Current}" });
builder.Services.AddHttpClient("mam").ConfigureHttpClient((sp, client) =>
{
    var mamOptions = sp.GetRequiredService<MamClientOptions>();
    client.Timeout = mamOptions.Timeout;
    client.DefaultRequestHeaders.UserAgent.ParseAdd(mamOptions.UserAgent);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false }); // MamClient sets the mam_id cookie itself
// A single AddDbContextFactory registration backs both the scoped FetchshelfDbContext (for
// EventStore/LinkRunner/Cleaner) and the short-lived contexts DatabaseHealthCheck creates itself -
// registering AddDbContext separately as well would conflict over DbContextOptions' lifetime.
builder.Services.AddDbContextFactory<FetchshelfDbContext>(options => options.UseFetchshelfSqlite(dataFilePath));
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<FetchshelfDbContext>>().CreateDbContext());
builder.Services.AddScoped<EventStore>();
builder.Services.AddScoped<LibraryQueries>();
builder.Services.AddScoped<ClientStatusChecker>();
// MamClient is a singleton: one mam_id account => one rate-limited client instance.
builder.Services.AddSingleton(sp => new MamClient(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("mam"),
    sp.GetRequiredService<MamClientOptions>(),
    logger: sp.GetRequiredService<ILogger<MamClient>>()));
builder.Services.AddSingleton<IMetadataLookup, MamMetadataLookup>();
builder.Services.AddSingleton<MamAccessGate>();

if (settings.Audiobookshelf is { } absSettings)
{
    builder.Services.AddSingleton(new AbsClientOptions { BaseUrl = absSettings.Url, Token = absSettings.Token });
    builder.Services.AddHttpClient("abs");
    builder.Services.AddSingleton(sp => new AbsClient(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("abs"), sp.GetRequiredService<AbsClientOptions>()));
    builder.Services.AddSingleton(new AbsMatcherOptions(absSettings.FetchshelfToAbsMapping()));
    builder.Services.AddScoped<AbsMatcher>();
}

if (settings.Hardcover is { } hardcoverSettings)
{
    builder.Services.AddSingleton(new HardcoverClientOptions { Token = hardcoverSettings.Token, UserAgent = $"Fetchshelf/{AppVersion.Current}" });
    builder.Services.AddHttpClient("hardcover");
    builder.Services.AddSingleton(sp => new HardcoverClient(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("hardcover"), sp.GetRequiredService<HardcoverClientOptions>()));
    // A singleton, so the token's user is looked up once rather than on every run.
    builder.Services.AddSingleton<IHardcoverBookSource>(sp => new HardcoverBookSource(sp.GetRequiredService<HardcoverClient>()));
}

var formatPreferenceLists = new FormatPreferenceLists
{
    AudioTypes = settings.AudioTypes,
    EbookTypes = settings.EbookTypes,
    MusicTypes = settings.MusicTypes,
    RadioTypes = settings.RadioTypes,
    Libraries = settings.Library
        .Where(library => library.Name is not null && (library.AudioTypes is not null || library.EbookTypes is not null))
        .ToDictionary(library => library.Name!, library => new LibraryFormatLists(library.AudioTypes, library.EbookTypes), StringComparer.Ordinal),
};
builder.Services.AddSingleton(formatPreferenceLists);
// The values the runners are built with, each registered once, so every runner takes them by type, not by position.
builder.Services.AddSingleton<IReadOnlyList<Library>>(settings.Library);
builder.Services.AddScoped<IReadOnlyList<QBittorrentClient>>(sp =>
    QBittorrentConnections.Build(settings, sp.GetRequiredService<QBittorrentClientFactory>()).Select(c => c.Client).ToList());
builder.Services.AddSingleton(new LinkRunnerOptions(settings.ExcludeNarratorInLibraryDir));
builder.Services.AddScoped<LinkRunner>();
builder.Services.AddScoped<DownloadProgressChecker>();
builder.Services.AddScoped<Cleaner>();

builder.Services.AddSingleton<IReadOnlyList<TagRule>>(settings.Tag
    .Select(t => new TagRule { Filter = SharedFilterConfigMapper.ToTorrentFilter(t), Category = t.Category, Tags = t.Tags })
    .ToList());
builder.Services.AddScoped<SelectionRunner>();
builder.Services.AddScoped<AutograbberRunner>();
builder.Services.AddScoped<SnatchlistImporter>();
if (settings.Hardcover is not null)
{
    builder.Services.AddScoped<HardcoverListImporter>();
}

if (settings.Follow is { } followSettings)
{
    builder.Services.AddSingleton(followSettings);
    builder.Services.AddScoped<FollowRunner>();
    builder.Services.AddScoped<FollowService>();
}

if (settings.Upgrade is { } upgradeSettings)
{
    builder.Services.AddSingleton(upgradeSettings);
    builder.Services.AddScoped<UpgradeRunner>();
}

if (settings.Bonus is { } bonusSettings)
{
    builder.Services.AddSingleton(bonusSettings);
    builder.Services.AddScoped<BonusRunner>();
}

// The buffers each queued torrent is held to: the Download job checks them, the Account page shows what they leave.
builder.Services.AddSingleton(new SafetyBuffers(
    settings.UnsatBuffer, settings.WedgeBuffer,
    settings.Autograb.Where(a => a.Name is not null).Select(a => new AutograbBuffers(a.Name!, a.UnsatBuffer, a.WedgeBuffer)).ToList()));
builder.Services.AddScoped<Fetchshelf.Web.Pages.Account.AccountSummaryBuilder>();
builder.Services.AddSingleton(new DownloadRunnerOptions(settings.MinRatio, settings.AddTorrentsStopped));
builder.Services.AddScoped<DownloadRunner>();
builder.Services.AddScoped<RetroactiveTagApplier>();

if (settings.BackgroundJobsEnabled)
{
    // AddSingleton<IHostedService>, not AddHostedService: with a factory lambda, AddHostedService
    // de-duplicates by implementation type, so only the first IntervalJobScheduler would be kept.
    void AddScheduler(string jobName, int intervalMinutes, int startupPosition) =>
        builder.Services.AddSingleton<IHostedService>(sp => new IntervalJobScheduler(
            sp.GetRequiredService<IJobRegistry>(), jobName, TimeSpan.FromMinutes(intervalMinutes),
            StartupStagger * startupPosition, sp.GetRequiredService<TimeProvider>()));

    // A job that is due, or has never run, first runs soon after start: Link first, then each MAM job in
    // configuration order, then Download, one stagger apart. MamAccessGate already keeps the MAM jobs from
    // overlapping; the stagger keeps their order predictable and the log readable.
    var startupPosition = 1;
    AddScheduler(JobNames.Link, settings.LinkInterval, startupPosition);

    if (settings.Backup is { Interval: > 0 } scheduledBackup)
    {
        // The Backup job talks to no other service, so it starts alongside Link.
        AddScheduler(JobNames.Backup, scheduledBackup.Interval, startupPosition);
    }

    if (settings.Audiobookshelf is { } scheduledAbsSettings)
    {
        // Audiobookshelf doesn't talk to MAM, so it starts alongside Link.
        AddScheduler(JobNames.Audiobookshelf, scheduledAbsSettings.Interval, startupPosition);
    }

    var mamJobs = settings.Autograb.Select((entry, i) => (AutoDownloaderJobNames.Autograb(entry, i), entry.SearchInterval))
        .Concat(settings.Snatchlist.Select((entry, i) => (AutoDownloaderJobNames.Snatchlist(entry, i), entry.SearchInterval)))
        .Concat(settings.HardcoverList.Select((entry, i) => (AutoDownloaderJobNames.HardcoverList(entry, i), entry.SearchInterval)))
        .Concat(settings.Follow is { } scheduledFollow ? [(JobNames.Follow, scheduledFollow.SearchInterval)] : [])
        .Concat(settings.Upgrade is { } scheduledUpgrade ? [(JobNames.Upgrade, scheduledUpgrade.SearchInterval)] : [])
        .Concat(settings.Bonus is { } scheduledBonus ? [(JobNames.Bonus, (int?)scheduledBonus.BonusInterval)] : []);
    foreach (var (jobName, searchInterval) in mamJobs)
    {
        var intervalMinutes = searchInterval ?? settings.SearchInterval;
        if (intervalMinutes > 0)
        {
            AddScheduler(jobName, intervalMinutes, ++startupPosition);
        }
    }

    if (settings.SearchInterval > 0)
    {
        AddScheduler(JobNames.Download, settings.SearchInterval, ++startupPosition);
    }

    // Follows what is downloading into the library, so it is linked minutes after it finishes, whether a page is open
    // or not, rather than at the Link job's next interval.
    if (settings.QBittorrent.Count > 0)
    {
        builder.Services.AddSingleton<IHostedService>(sp => new DownloadProgressWatcher(
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<IJobRegistry>(), sp.GetRequiredService<DownloadWatchSignal>(),
            sp.GetRequiredService<ILogger<DownloadProgressWatcher>>(), sp.GetRequiredService<TimeProvider>()));
    }
}

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AddPageRoute("/Torrents/Detail", "torrents/{id}");
    options.Conventions.AddPageRoute("/Torrents/Edit", "torrents/{id}/edit");
    options.Conventions.AddPageRoute("/Search/Detail", "search/{mamId:long}");
    options.Conventions.AddPageRoute("/Following/Detail", "following/{id:int}");
    options.Conventions.AddPageRoute("/Following/ToLookAt", "following/to-look-at");
});
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

using (var startupScope = app.Services.CreateScope())
{
    var startupDbContext = startupScope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
    if (settings.Backup is { BeforeUpgrade: true } && backupFolder is not null)
    {
        try
        {
            if (DatabaseBackup.BeforeUpgrade(startupDbContext, dataFilePath, backupFolder, AppVersion.Current,
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
            await Log.CloseAndFlushAsync();
            Environment.ExitCode = 1;
            return;
        }
    }

    DatabaseMigrator.Migrate(startupDbContext, settings.DatabaseWal, message => Log.Information("{Upgrade}", message));
}

if (args.Contains("--update-search-title"))
{
    using var maintenanceScope = app.Services.CreateScope();
    var maintenanceDbContext = maintenanceScope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
    Log.Information("Update search title started");
    var (rekeyedTorrents, rekeyedSelections) = await TitleMaintenanceRunner.RunAsync(maintenanceDbContext);
    Log.Information("Update search title done: {Torrents} torrent and {Selections} selection keys changed", rekeyedTorrents, rekeyedSelections);
    await Log.CloseAndFlushAsync();
    return;
}

if (settings.BackgroundJobsEnabled)
{
    RegisterOrganizerJobs(app.Services, settings, dataFilePath, backupFolder);
    RegisterAutoDownloaderJobs(app.Services, settings);
}

if (notificationsOn)
{
    ConnectMamNotifications(app.Services);
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

await app.RunAsync();

static void RegisterOrganizerJobs(IServiceProvider services, FetchshelfSettings settings, string dataFilePath, string? backupFolder)
{
    var jobRegistry = services.GetRequiredService<IJobRegistry>();
    var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
    var qbittorrentFactory = services.GetRequiredService<QBittorrentClientFactory>();

    jobRegistry.Register(JobNames.Link, async ct =>
    {
        using var scope = scopeFactory.CreateScope();
        var linkRunner = scope.ServiceProvider.GetRequiredService<LinkRunner>();
        var (linked, failed) = (0, 0);
        foreach (var (connection, client) in QBittorrentConnections.Build(settings, qbittorrentFactory))
        {
            var summary = await linkRunner.RunAsync(connection, client, ct);
            (linked, failed) = (linked + summary.Linked, failed + summary.Failed);
        }

        // Clean is a run of its own, so cancelling this one doesn't cancel it; this run only stops waiting for it.
        await jobRegistry.TriggerAsync(JobNames.Clean, JobTrigger.Chained).WaitAsync(ct);

        return JobOutcomes.Link(linked, failed);
    }, "Moves finished downloads from qBittorrent into your library folders.");

    jobRegistry.Register(JobNames.Clean, async ct =>
    {
        using var scope = scopeFactory.CreateScope();
        var cleaner = scope.ServiceProvider.GetRequiredService<Cleaner>();
        return JobOutcomes.Clean(await cleaner.RunAsync(QBittorrentConnections.Build(settings, qbittorrentFactory), ct));
    }, "Retires superseded duplicate torrents once a newer copy is linked.");

    if (settings.Audiobookshelf is not null)
    {
        jobRegistry.Register(JobNames.Audiobookshelf, async ct =>
        {
            using var scope = scopeFactory.CreateScope();
            var matcher = scope.ServiceProvider.GetRequiredService<AbsMatcher>();
            return JobOutcomes.Audiobookshelf(await matcher.RunAsync(ct));
        }, "Matches linked torrents to their Audiobookshelf library item.");
    }

    if (settings.Backup is { } backupSettings && backupFolder is not null)
    {
        var timeProvider = services.GetRequiredService<TimeProvider>();
        jobRegistry.Register(JobNames.Backup, ct => Task.Run(
            () => (JobOutcome?)JobOutcomes.Backup(DatabaseBackup.RunScheduled(dataFilePath, backupFolder, backupSettings.Keep, timeProvider.GetUtcNow())), ct),
            "Copies the database into the [backup] folder while Fetchshelf runs, and keeps the newest ones.");
    }
}

static void RegisterAutoDownloaderJobs(IServiceProvider services, FetchshelfSettings settings)
{
    var jobRegistry = services.GetRequiredService<IJobRegistry>();
    var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
    var ignoreTorrents = settings.IgnoreTorrents.Select(long.Parse).ToList();

    for (var i = 0; i < settings.Autograb.Count; i++)
    {
        var entry = settings.Autograb[i];
        var jobName = AutoDownloaderJobNames.Autograb(entry, i);
        jobRegistry.Register(jobName, async ct =>
        {
            using var scope = scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<AutograbberRunner>();
            var summary = await runner.RunAsync(entry, settings.UnsatBuffer, ignoreTorrents, ct);
            if (summary.SelectedForDownload > 0 && settings.DownloadAfterSelection)
            {
                await jobRegistry.TriggerOrQueueAsync(JobNames.Download, JobTrigger.Chained).WaitAsync(ct);
            }

            return JobOutcomes.Autograb(summary);
        }, "Searches MAM for new matches and queues them for download.");
    }

    for (var i = 0; i < settings.Snatchlist.Count; i++)
    {
        var entry = settings.Snatchlist[i];
        var jobName = AutoDownloaderJobNames.Snatchlist(entry, i);
        jobRegistry.Register(jobName, async ct =>
        {
            using var scope = scopeFactory.CreateScope();
            var importer = scope.ServiceProvider.GetRequiredService<SnatchlistImporter>();
            return JobOutcomes.Snatchlist(await importer.RunAsync(entry, ct));
        }, "Reads your MAM snatchlist to refresh stored torrents or record them as owned.");
    }

    for (var i = 0; i < settings.HardcoverList.Count; i++)
    {
        var entry = settings.HardcoverList[i];
        var jobName = AutoDownloaderJobNames.HardcoverList(entry, i);
        jobRegistry.Register(jobName, async ct =>
        {
            using var scope = scopeFactory.CreateScope();
            var importer = scope.ServiceProvider.GetRequiredService<HardcoverListImporter>();
            var summary = await importer.RunAsync(entry, settings.UnsatBuffer, ignoreTorrents, ct);
            if (summary.SelectedForDownload > 0 && settings.DownloadAfterSelection)
            {
                await jobRegistry.TriggerOrQueueAsync(JobNames.Download, JobTrigger.Chained).WaitAsync(ct);
            }

            return JobOutcomes.HardcoverList(summary);
        }, "Searches MAM for the books on a Hardcover shelf or list and queues them for download.");
    }

    if (settings.Follow is not null)
    {
        jobRegistry.Register(JobNames.Follow, async ct =>
        {
            using var scope = scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<FollowRunner>();
            var summary = await runner.RunAsync(settings.UnsatBuffer, ignoreTorrents, ct);
            if (summary.SelectedForDownload > 0 && settings.DownloadAfterSelection)
            {
                await jobRegistry.TriggerOrQueueAsync(JobNames.Download, JobTrigger.Chained).WaitAsync(ct);
            }

            return JobOutcomes.Follow(summary);
        }, "Searches MAM for the followed series and authors, and selects or lists the books the library is missing.");
    }

    if (settings.Upgrade is not null)
    {
        jobRegistry.Register(JobNames.Upgrade, async ct =>
        {
            using var scope = scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<UpgradeRunner>();
            var summary = await runner.RunAsync(settings.UnsatBuffer, ignoreTorrents, ct);
            if (summary.SelectedForDownload > 0 && settings.DownloadAfterSelection)
            {
                await jobRegistry.TriggerOrQueueAsync(JobNames.Download, JobTrigger.Chained).WaitAsync(ct);
            }

            return JobOutcomes.Upgrade(summary);
        }, "Searches MAM for better formats of books in the library, such as m4b for an mp3 copy, and selects them when a grab block's cost allows.");
    }

    if (settings.Bonus is not null)
    {
        jobRegistry.Register(JobNames.Bonus, async ct =>
        {
            using var scope = scopeFactory.CreateScope();
            var summary = await scope.ServiceProvider.GetRequiredService<BonusRunner>().RunAsync(ct);
            return JobOutcomes.Bonus(summary);
        }, "Spends bonus points as the [bonus] section says: a wedge when one is due, a VIP top-up, then upload credit, keeping keep_points back.");
    }

    var budgetSpent = services.GetRequiredService<BudgetSpentWatcher>();
    jobRegistry.Register(JobNames.Download, async ct =>
    {
        using var scope = scopeFactory.CreateScope();
        var downloadRunner = scope.ServiceProvider.GetRequiredService<DownloadRunner>();
        var summary = await downloadRunner.RunAsync(ct);

        // Told once when selections start waiting for budget, not on every run they keep waiting. A run MAM stopped
        // didn't try them all, so it says nothing about how many wait.
        if (!summary.StoppedByMam)
        {
            await budgetSpent.CheckAsync(summary.LeftQueued, summary.LimitedBy);
        }

        return JobOutcomes.Download(summary);
    }, "Sends queued, selected torrents to qBittorrent.");
}

/// <summary>
/// Connects what MAM answers to notifications: a refused `mam_id`, a rate limit, and each account read, which is checked
/// for a new hit-and-run. The handlers run on the thread of the MAM call, so each only hands something over: a
/// notification to the queue, an account read to <see cref="HitAndRunWatcher"/>'s background check.
/// </summary>
static void ConnectMamNotifications(IServiceProvider services)
{
    var notifier = services.GetRequiredService<Notifier>();
    var mam = services.GetRequiredService<MamClient>();
    var throttle = new NotificationThrottle();
    mam.Refused += refusal =>
    {
        // A 403 is raised once per pause. A 406 pauses nothing and comes with every call, and a 429 may come every minute,
        // so those are told at most once in a while.
        var (key, interval, notification) = refusal.Kind switch
        {
            MamRefusalKind.Forbidden => ("mam_refused.403", TimeSpan.FromMinutes(30), AccountNotificationRules.MamRefused(refusal.At, refusal.PausedUntil ?? refusal.At)),
            MamRefusalKind.Blocked => ("mam_refused.406", TimeSpan.FromMinutes(30), AccountNotificationRules.MamBlocked(refusal.At)),
            _ => ("rate_limited", TimeSpan.FromHours(1), AccountNotificationRules.RateLimited(refusal.At)),
        };
        if (throttle.Allow(key, interval, refusal.At))
        {
            notifier.Publish(notification);
        }
    };

    var hitAndRuns = services.GetRequiredService<HitAndRunWatcher>();
    mam.AccountStateRead += (previous, current) => hitAndRuns.Enqueue(current.State);
}

// Exposed so Fetchshelf.Web.Tests can reference WebApplicationFactory<Program>.
public partial class Program
{
    /// <summary>How far apart jobs that are due first run after start.</summary>
    private static readonly TimeSpan StartupStagger = TimeSpan.FromSeconds(30);
}
