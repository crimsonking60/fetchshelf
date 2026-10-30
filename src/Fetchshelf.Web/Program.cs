using Fetchshelf.Clients.Abs;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.Notion;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web;
using Fetchshelf.Web.Jobs;
using Microsoft.EntityFrameworkCore;
using Serilog;

if (args.Contains(HealthProbe.Argument))
{
    Environment.ExitCode = await HealthProbe.RunAsync(
        Environment.GetEnvironmentVariable("FETCHSHELF_CONFIG_FILE") ?? ConfigLoader.ResolveDefaultConfigFilePath(),
        TimeSpan.FromSeconds(5));
    return;
}

Log.Logger = FetchshelfLogging
    .Build(Environment.GetEnvironmentVariable("FETCHSHELF_LOG_DIR"), Environment.GetEnvironmentVariable("FETCHSHELF_LOG"))
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog(dispose: true);

var configFilePath = Environment.GetEnvironmentVariable("FETCHSHELF_CONFIG_FILE") ?? ConfigLoader.ResolveDefaultConfigFilePath();
var settings = ConfigLoader.Load(configFilePath);
var dataFilePath = Environment.GetEnvironmentVariable("FETCHSHELF_DATA_FILE") ?? "data.db";
var dataFileDirectory = Path.GetDirectoryName(Path.GetFullPath(dataFilePath));
if (!string.IsNullOrEmpty(dataFileDirectory))
{
    Directory.CreateDirectory(dataFileDirectory);
}

builder.WebHost.UseUrls(ListenAddress.Build(settings));

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<IJobRegistry, JobRegistry>();
builder.Services.AddHttpClient("qbittorrent");
builder.Services.AddSingleton<QBittorrentClientFactory>();
builder.Services.AddSingleton(new MamClientOptions { MamId = settings.MamId });
builder.Services.AddHttpClient("mam").ConfigureHttpClient((sp, client) =>
{
    var mamOptions = sp.GetRequiredService<MamClientOptions>();
    client.Timeout = mamOptions.Timeout;
    client.DefaultRequestHeaders.UserAgent.ParseAdd(mamOptions.UserAgent);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false }); // MamClient sets the mam_id cookie itself
// A single AddDbContextFactory registration backs both the scoped FetchshelfDbContext (for
// EventStore/LinkRunner/Cleaner) and MamCookieStore's singleton-safe short-lived contexts -
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

builder.Services.AddHttpClient("notion");
builder.Services.AddScoped(sp => new NotionClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("notion")));

if (settings.Audiobookshelf is { } absSettings)
{
    builder.Services.AddSingleton(new AbsClientOptions { BaseUrl = absSettings.Url, Token = absSettings.Token });
    builder.Services.AddHttpClient("abs");
    builder.Services.AddSingleton(sp => new AbsClient(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("abs"), sp.GetRequiredService<AbsClientOptions>()));
    builder.Services.AddScoped(sp => new AbsMatcher(sp.GetRequiredService<FetchshelfDbContext>(), sp.GetRequiredService<AbsClient>()));
}

var formatPreferenceLists = new FormatPreferenceLists
{
    AudioTypes = settings.AudioTypes,
    EbookTypes = settings.EbookTypes,
    MusicTypes = settings.MusicTypes,
    RadioTypes = settings.RadioTypes,
};
builder.Services.AddScoped(sp => new LinkRunner(
    sp.GetRequiredService<FetchshelfDbContext>(),
    sp.GetRequiredService<EventStore>(),
    sp.GetRequiredService<IMetadataLookup>(),
    settings.Library,
    formatPreferenceLists,
    settings.ExcludeNarratorInLibraryDir,
    sp.GetService<AbsClient>()));
builder.Services.AddScoped(sp => new Cleaner(
    sp.GetRequiredService<FetchshelfDbContext>(), sp.GetRequiredService<EventStore>(), formatPreferenceLists,
    sp.GetService<AbsClient>()));

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
builder.Services.AddScoped(sp => new NotionListImporter(
    sp.GetRequiredService<FetchshelfDbContext>(),
    sp.GetRequiredService<MamClient>(),
    sp.GetRequiredService<MamAccessGate>(),
    sp.GetRequiredService<SelectionRunner>(),
    sp.GetRequiredService<NotionClient>(),
    logger: sp.GetRequiredService<ILogger<NotionListImporter>>()));
builder.Services.AddScoped(sp => new DownloadRunner(
    sp.GetRequiredService<FetchshelfDbContext>(),
    sp.GetRequiredService<EventStore>(),
    sp.GetRequiredService<MamClient>(),
    QBittorrentConnections.Build(settings, sp.GetRequiredService<QBittorrentClientFactory>()).Select(c => c.Client).ToList(),
    settings.MinRatio,
    settings.AddTorrentsStopped));
builder.Services.AddScoped(sp => new RetroactiveTagApplier(
    sp.GetRequiredService<FetchshelfDbContext>(),
    sp.GetRequiredService<MamClient>(),
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
            sp.GetRequiredService<IJobRegistry>(), JobNames.AbsMatcher, TimeSpan.FromMinutes(scheduledAbsSettings.Interval)));
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

    for (var i = 0; i < settings.NotionList.Count; i++)
    {
        var jobName = AutoDownloaderJobNames.NotionList(settings.NotionList[i], i);
        var intervalMinutes = settings.NotionList[i].SearchInterval ?? settings.ImportInterval;
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
});
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

using (var startupScope = app.Services.CreateScope())
{
    DatabaseMigrator.Migrate(startupScope.ServiceProvider.GetRequiredService<FetchshelfDbContext>());
}

if (args.Contains("--update-search-title"))
{
    using var maintenanceScope = app.Services.CreateScope();
    var maintenanceDbContext = maintenanceScope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
    Log.Information("Update search title started");
    await TitleMaintenanceRunner.RunAsync(maintenanceDbContext);
    Log.Information("Update search title done");
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
        foreach (var (connection, client) in QBittorrentConnections.Build(settings, qbittorrentFactory))
        {
            await linkRunner.RunAsync(connection, client, ct);
        }

        await jobRegistry.TriggerAsync(JobNames.Clean, ct);
    }, "Moves finished downloads from qBittorrent into your library folders.");

    jobRegistry.Register(JobNames.Clean, async ct =>
    {
        using var scope = scopeFactory.CreateScope();
        var cleaner = scope.ServiceProvider.GetRequiredService<Cleaner>();
        await cleaner.RunAsync(QBittorrentConnections.Build(settings, qbittorrentFactory), ct);
    }, "Retires superseded duplicate torrents once a newer copy is linked.");

    if (settings.Audiobookshelf is not null)
    {
        jobRegistry.Register(JobNames.AbsMatcher, async ct =>
        {
            using var scope = scopeFactory.CreateScope();
            var matcher = scope.ServiceProvider.GetRequiredService<AbsMatcher>();
            await matcher.RunAsync(ct);
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
            var selectedCount = await runner.RunAsync(entry, settings.UnsatBuffer, settings.WedgeBuffer, ignoreTorrents, ct);
            if (selectedCount > 0)
            {
                await jobRegistry.TriggerAsync(JobNames.Download, ct);
            }
        }, "Searches MaM for new matches and queues them for download.");
    }

    for (var i = 0; i < settings.Snatchlist.Count; i++)
    {
        var entry = settings.Snatchlist[i];
        var jobName = AutoDownloaderJobNames.Snatchlist(entry, i);
        jobRegistry.Register(jobName, async ct =>
        {
            using var scope = scopeFactory.CreateScope();
            var importer = scope.ServiceProvider.GetRequiredService<SnatchlistImporter>();
            await importer.RunAsync(entry, ct);
        }, "Imports your MaM snatchlist so those torrents count as owned.");
    }

    for (var i = 0; i < settings.NotionList.Count; i++)
    {
        var entry = settings.NotionList[i];
        var jobName = AutoDownloaderJobNames.NotionList(entry, i);
        jobRegistry.Register(jobName, async ct =>
        {
            using var scope = scopeFactory.CreateScope();
            var importer = scope.ServiceProvider.GetRequiredService<NotionListImporter>();
            var selectedCount = await importer.RunAsync(entry, settings.UnsatBuffer, settings.WedgeBuffer, ignoreTorrents, ct);
            if (selectedCount > 0)
            {
                await jobRegistry.TriggerAsync(JobNames.Download, ct);
            }
        }, "Checks a Notion list for entries and queues matches for download.");
    }

    jobRegistry.Register(JobNames.Download, async ct =>
    {
        using var scope = scopeFactory.CreateScope();
        var downloadRunner = scope.ServiceProvider.GetRequiredService<DownloadRunner>();
        await downloadRunner.RunAsync(ct);
    }, "Sends queued, selected torrents to qBittorrent.");
}

internal static class JobNames
{
    public const string Link = "Link";
    public const string Clean = "Clean";
    public const string Download = "Download";
    public const string AbsMatcher = "AbsMatcher";
}

/// <summary>Job names for per-entry `[[autograb]]`/`[[snatchlist]]` jobs: the entry's own `name` if set, else `autograb[&lt;index&gt;]`/`snatchlist[&lt;index&gt;]`</summary>
internal static class AutoDownloaderJobNames
{
    public static string Autograb(AutogrebEntry entry, int index) =>
        string.IsNullOrWhiteSpace(entry.Name) ? $"autograb[{index}]" : entry.Name;

    public static string Snatchlist(SnatchlistEntry entry, int index) =>
        string.IsNullOrWhiteSpace(entry.Name) ? $"snatchlist[{index}]" : entry.Name;

    public static string NotionList(NotionListEntry entry, int index) =>
        string.IsNullOrWhiteSpace(entry.Name) ? $"notion_list[{index}]" : entry.Name;
}

// Exposed so Fetchshelf.Web.Tests can reference WebApplicationFactory<Program>.
public partial class Program;
