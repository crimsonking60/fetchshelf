using Fetchshelf.Clients.Abs;
using Fetchshelf.Clients.Hardcover;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Opds;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web;
using Fetchshelf.Web.Jobs;
using Fetchshelf.Web.Opds;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;

if (args.Contains(HealthProbe.Argument))
{
    Environment.ExitCode = await HealthProbe.RunAsync(
        Environment.GetEnvironmentVariable("FETCHSHELF_CONFIG_FILE") ?? ConfigLoader.ResolveDefaultConfigFilePath(),
        TimeSpan.FromSeconds(5));
    return;
}

if (args.Contains(ConfigCheck.Argument))
{
    Environment.ExitCode = ConfigCheck.Run(
        Environment.GetEnvironmentVariable("FETCHSHELF_CONFIG_FILE") ?? ConfigLoader.ResolveDefaultConfigFilePath(), Console.Out);
    return;
}

Log.Logger = FetchshelfLogging
    .Build(Environment.GetEnvironmentVariable("FETCHSHELF_LOG_DIR"), Environment.GetEnvironmentVariable("FETCHSHELF_LOG"))
    .CreateLogger();
Log.Information("Fetchshelf {Version} starting", AppVersion.Current);

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog(dispose: true);

var configFilePath = Environment.GetEnvironmentVariable("FETCHSHELF_CONFIG_FILE") ?? ConfigLoader.ResolveDefaultConfigFilePath();
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

var dataFilePath = Environment.GetEnvironmentVariable("FETCHSHELF_DATA_FILE") ?? "data.db";
var dataFileDirectory = Path.GetDirectoryName(Path.GetFullPath(dataFilePath));
if (!string.IsNullOrEmpty(dataFileDirectory))
{
    Directory.CreateDirectory(dataFileDirectory);
}

// Keep the keys that encrypt antiforgery cookies next to the database: the default location is inside the
// container, so a new container (every image update) would otherwise reject the cookie browsers still hold.
builder.Services.AddDataProtection()
    .SetApplicationName("Fetchshelf")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataFileDirectory ?? ".", "keys")));

// The two cookies those keys encrypt are named after this installation, so a second Fetchshelf on the same host
// (browsers share cookies across ports) can't overwrite them and fail every form with HTTP 400.
var instanceId = InstanceId.LoadOrCreate(dataFileDirectory ?? ".");
builder.Services.AddAntiforgery(options => options.Cookie.Name = $".Fetchshelf.Antiforgery.{instanceId}");
builder.Services.Configure<Microsoft.AspNetCore.Mvc.CookieTempDataProviderOptions>(
    options => options.Cookie.Name = $".Fetchshelf.TempData.{instanceId}");

builder.WebHost.UseUrls(ListenAddress.Build(settings));

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<IJobRegistry, JobRegistry>();
builder.Services.AddHttpClient("qbittorrent");
builder.Services.AddSingleton<QBittorrentClientFactory>();
builder.Services.AddSingleton<QBittorrentPresenceCache>();
builder.Services.TryAddSingleton(TimeProvider.System);
if (settings.Opds is not null)
{
    builder.Services.AddSingleton<OpdsCatalogCache>();
    // Covers read out of the books live next to the database, like the form keys, so they survive a new container.
    builder.Services.AddSingleton(sp => new OpdsCoverCache(
        Path.Combine(dataFileDirectory ?? ".", "covers"), sp.GetRequiredService<ILogger<OpdsCoverCache>>()));
}

builder.Services.AddSingleton(new MamClientOptions { MamId = settings.MamId });
builder.Services.AddHttpClient("mam").ConfigureHttpClient((sp, client) =>
{
    var mamOptions = sp.GetRequiredService<MamClientOptions>();
    client.Timeout = mamOptions.Timeout;
    client.DefaultRequestHeaders.UserAgent.ParseAdd(mamOptions.UserAgent);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false }); // MamClient sets the mam_id cookie itself
// A single AddDbContextFactory registration backs both the scoped FetchshelfDbContext (for
// EventStore/LinkRunner/Cleaner) and the short-lived contexts DatabaseHealthCheck creates itself -
// registering AddDbContext separately as well would conflict over DbContextOptions' lifetime.
builder.Services.AddDbContextFactory<FetchshelfDbContext>(options => options.UseSqlite($"Data Source={dataFilePath}"));
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<FetchshelfDbContext>>().CreateDbContext());
builder.Services.AddScoped<EventStore>();
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
    builder.Services.AddScoped(sp => new AbsMatcher(
        sp.GetRequiredService<FetchshelfDbContext>(), sp.GetRequiredService<AbsClient>(), sp.GetRequiredService<ILogger<AbsMatcher>>(),
        absSettings.FetchshelfToAbsMapping()));
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
};
builder.Services.AddSingleton(formatPreferenceLists);
builder.Services.AddScoped(sp => new LinkRunner(
    sp.GetRequiredService<FetchshelfDbContext>(),
    sp.GetRequiredService<EventStore>(),
    sp.GetRequiredService<IMetadataLookup>(),
    settings.Library,
    formatPreferenceLists,
    settings.ExcludeNarratorInLibraryDir,
    sp.GetService<AbsClient>(),
    sp.GetRequiredService<ILogger<LinkRunner>>()));
builder.Services.AddScoped(sp => new Cleaner(
    sp.GetRequiredService<FetchshelfDbContext>(), sp.GetRequiredService<EventStore>(), formatPreferenceLists,
    sp.GetService<AbsClient>(), sp.GetRequiredService<ILogger<Cleaner>>()));

var tagRules = settings.Tag
    .Select(t => new TagRule { Filter = SharedFilterConfigMapper.ToTorrentFilter(t), Category = t.Category, Tags = t.Tags })
    .ToList();
builder.Services.AddScoped(sp => new SelectionRunner(
    sp.GetRequiredService<FetchshelfDbContext>(), sp.GetRequiredService<EventStore>(), tagRules, formatPreferenceLists));
builder.Services.AddScoped(sp => new AutograbberRunner(
    sp.GetRequiredService<MamClient>(),
    sp.GetRequiredService<MamAccessGate>(),
    sp.GetRequiredService<SelectionRunner>(),
    sp.GetRequiredService<FetchshelfDbContext>(),
    sp.GetRequiredService<ILogger<AutograbberRunner>>()));
builder.Services.AddScoped(sp => new SnatchlistImporter(
    sp.GetRequiredService<FetchshelfDbContext>(), sp.GetRequiredService<MamClient>(), sp.GetRequiredService<MamAccessGate>(),
    sp.GetRequiredService<ILogger<SnatchlistImporter>>()));
if (settings.Hardcover is not null)
{
    builder.Services.AddScoped(sp => new HardcoverListImporter(
        sp.GetRequiredService<FetchshelfDbContext>(),
        sp.GetRequiredService<MamClient>(),
        sp.GetRequiredService<MamAccessGate>(),
        sp.GetRequiredService<SelectionRunner>(),
        sp.GetRequiredService<IHardcoverBookSource>(),
        formatPreferenceLists,
        logger: sp.GetRequiredService<ILogger<HardcoverListImporter>>()));
}

builder.Services.AddScoped(sp => new DownloadRunner(
    sp.GetRequiredService<FetchshelfDbContext>(),
    sp.GetRequiredService<EventStore>(),
    sp.GetRequiredService<MamClient>(),
    sp.GetRequiredService<MamAccessGate>(),
    QBittorrentConnections.Build(settings, sp.GetRequiredService<QBittorrentClientFactory>()).Select(c => c.Client).ToList(),
    settings.MinRatio,
    settings.AddTorrentsStopped,
    new SafetyBuffers(
        settings.UnsatBuffer, settings.WedgeBuffer,
        settings.Autograb.Where(a => a.Name is not null).Select(a => new AutograbBuffers(a.Name!, a.UnsatBuffer, a.WedgeBuffer)).ToList()),
    sp.GetRequiredService<ILogger<DownloadRunner>>()));
builder.Services.AddScoped(sp => new RetroactiveTagApplier(
    sp.GetRequiredService<FetchshelfDbContext>(),
    sp.GetRequiredService<MamClient>(),
    sp.GetRequiredService<MamAccessGate>(),
    QBittorrentConnections.Build(settings, sp.GetRequiredService<QBittorrentClientFactory>()).Select(c => c.Client).ToList()));

if (settings.BackgroundJobsEnabled)
{
    // AddSingleton<IHostedService>, not AddHostedService: with a factory lambda, AddHostedService
    // de-duplicates by implementation type, so only the first IntervalJobScheduler would be kept.
    builder.Services.AddSingleton<IHostedService>(sp =>
        new IntervalJobScheduler(sp.GetRequiredService<IJobRegistry>(), JobNames.Link, TimeSpan.FromMinutes(settings.LinkInterval)));

    if (settings.Audiobookshelf is { } scheduledAbsSettings)
    {
        builder.Services.AddSingleton<IHostedService>(sp => new IntervalJobScheduler(
            sp.GetRequiredService<IJobRegistry>(), JobNames.Audiobookshelf, TimeSpan.FromMinutes(scheduledAbsSettings.Interval)));
    }

    for (var i = 0; i < settings.Autograb.Count; i++)
    {
        var jobName = AutoDownloaderJobNames.Autograb(settings.Autograb[i], i);
        var intervalMinutes = settings.Autograb[i].SearchInterval ?? settings.SearchInterval;
        if (intervalMinutes > 0)
        {
            builder.Services.AddSingleton<IHostedService>(sp =>
                new IntervalJobScheduler(sp.GetRequiredService<IJobRegistry>(), jobName, TimeSpan.FromMinutes(intervalMinutes)));
        }
    }

    for (var i = 0; i < settings.Snatchlist.Count; i++)
    {
        var jobName = AutoDownloaderJobNames.Snatchlist(settings.Snatchlist[i], i);
        var intervalMinutes = settings.Snatchlist[i].SearchInterval ?? settings.SearchInterval;
        if (intervalMinutes > 0)
        {
            builder.Services.AddSingleton<IHostedService>(sp =>
                new IntervalJobScheduler(sp.GetRequiredService<IJobRegistry>(), jobName, TimeSpan.FromMinutes(intervalMinutes)));
        }
    }

    for (var i = 0; i < settings.HardcoverList.Count; i++)
    {
        var jobName = AutoDownloaderJobNames.HardcoverList(settings.HardcoverList[i], i);
        var intervalMinutes = settings.HardcoverList[i].SearchInterval ?? settings.SearchInterval;
        if (intervalMinutes > 0)
        {
            builder.Services.AddSingleton<IHostedService>(sp =>
                new IntervalJobScheduler(sp.GetRequiredService<IJobRegistry>(), jobName, TimeSpan.FromMinutes(intervalMinutes)));
        }
    }

    if (settings.SearchInterval > 0)
    {
        builder.Services.AddSingleton<IHostedService>(sp =>
            new IntervalJobScheduler(sp.GetRequiredService<IJobRegistry>(), JobNames.Download, TimeSpan.FromMinutes(settings.SearchInterval)));
    }
}

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AddPageRoute("/Torrents/Detail", "torrents/{id}");
    options.Conventions.AddPageRoute("/Torrents/Edit", "torrents/{id}/edit");
    options.Conventions.AddPageRoute("/Search/Detail", "search/{mamId:long}");
});
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

using (var startupScope = app.Services.CreateScope())
{
    DatabaseMigrator.Migrate(
        startupScope.ServiceProvider.GetRequiredService<FetchshelfDbContext>(), settings.DatabaseWal, message => Log.Information("{Upgrade}", message));
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
    RegisterOrganizerJobs(app.Services, settings);
    RegisterAutoDownloaderJobs(app.Services, settings);
}

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

app.Run();

static void RegisterOrganizerJobs(IServiceProvider services, FetchshelfSettings settings)
{
    var jobRegistry = services.GetRequiredService<IJobRegistry>();
    var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
    var qbittorrentFactory = services.GetRequiredService<QBittorrentClientFactory>();

    jobRegistry.Register(JobNames.Link, async ct =>
    {
        using var scope = scopeFactory.CreateScope();
        var linkRunner = scope.ServiceProvider.GetRequiredService<LinkRunner>();
        var failed = 0;
        foreach (var (connection, client) in QBittorrentConnections.Build(settings, qbittorrentFactory))
        {
            failed += (await linkRunner.RunAsync(connection, client, ct)).Failed;
        }

        await jobRegistry.TriggerAsync(JobNames.Clean, ct);

        if (failed > 0)
        {
            throw new JobCompletedWithErrorsException(
                $"{failed} {(failed == 1 ? "torrent" : "torrents")} could not be linked; see the Errors page.");
        }
    }, "Moves finished downloads from qBittorrent into your library folders.");

    jobRegistry.Register(JobNames.Clean, async ct =>
    {
        using var scope = scopeFactory.CreateScope();
        var cleaner = scope.ServiceProvider.GetRequiredService<Cleaner>();
        var summary = await cleaner.RunAsync(QBittorrentConnections.Build(settings, qbittorrentFactory), ct);
        if (summary.Failed > 0)
        {
            throw new JobCompletedWithErrorsException(
                $"{summary.Failed} superseded {(summary.Failed == 1 ? "copy" : "copies")} could not be retired; see the Errors page.");
        }
    }, "Retires superseded duplicate torrents once a newer copy is linked.");

    if (settings.Audiobookshelf is not null)
    {
        jobRegistry.Register(JobNames.Audiobookshelf, async ct =>
        {
            using var scope = scopeFactory.CreateScope();
            var matcher = scope.ServiceProvider.GetRequiredService<AbsMatcher>();
            var summary = await matcher.RunAsync(ct);
            if (summary.Failed > 0)
            {
                throw new JobCompletedWithErrorsException(
                    $"{summary.Failed} {(summary.Failed == 1 ? "book" : "books")} could not be matched in Audiobookshelf; see the log.");
            }
        }, "Matches linked torrents to their Audiobookshelf library item.");
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
            var selectedCount = await runner.RunAsync(entry, settings.UnsatBuffer, ignoreTorrents, ct);
            if (selectedCount > 0 && settings.DownloadAfterSelection)
            {
                await jobRegistry.TriggerOrQueueAsync(JobNames.Download, ct);
            }
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
            var summary = await importer.RunAsync(entry, ct);
            if (summary.Failed > 0)
            {
                throw new JobCompletedWithErrorsException(
                    $"{summary.Failed} snatch list {(summary.Failed == 1 ? "row" : "rows")} could not be imported; see the log.");
            }
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
            if (summary.Selected > 0 && settings.DownloadAfterSelection)
            {
                await jobRegistry.TriggerOrQueueAsync(JobNames.Download, ct);
            }

            if (summary.Failed > 0)
            {
                throw new JobCompletedWithErrorsException(
                    $"{summary.Failed} {(summary.Failed == 1 ? "book" : "books")} could not be searched for; see the log.");
            }
        }, "Searches MAM for the books on a Hardcover shelf or list and queues them for download.");
    }

    jobRegistry.Register(JobNames.Download, async ct =>
    {
        using var scope = scopeFactory.CreateScope();
        var downloadRunner = scope.ServiceProvider.GetRequiredService<DownloadRunner>();
        var summary = await downloadRunner.RunAsync(ct);
        if (summary.Failed > 0)
        {
            throw new JobCompletedWithErrorsException(
                $"{summary.Failed} {(summary.Failed == 1 ? "torrent" : "torrents")} could not be downloaded; see the log.");
        }
    }, "Sends queued, selected torrents to qBittorrent.");
}

// Exposed so Fetchshelf.Web.Tests can reference WebApplicationFactory<Program>.
public partial class Program;
