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
var notificationsOn = NotificationServices.NotificationsOn(settings);

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
    FetchshelfJobs.RegisterOrganizerJobs(app.Services, settings, dataFilePath, backupFolder);
    FetchshelfJobs.RegisterAutoDownloaderJobs(app.Services, settings);
}

if (notificationsOn)
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

// Exposed so Fetchshelf.Web.Tests can reference WebApplicationFactory<Program>.
public partial class Program;
