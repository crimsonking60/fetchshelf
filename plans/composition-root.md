# Split Program.cs into a reusable host — implementation plan

## Context

`src/Fetchshelf.Web/Program.cs` is 596 lines of top-level statements: command-line modes (`--healthcheck`,
`--check-config`, `--update-search-title`), paths, logging, every service registration, job registration and
scheduling, notification wiring, the shutdown hooks and the middleware. Runners are built by factory lambdas that
pass settings positionally:

```csharp
builder.Services.AddScoped(sp => new DownloadRunner(
    sp.GetRequiredService<FetchshelfDbContext>(), …, settings.MinRatio, settings.AddTorrentsStopped, …));
```

Adding a setting to a runner means editing the lambda, and two arguments of the same type in the wrong order
compile. The same file decides job names (`JobNames`, `AutoDownloaderJobNames`) and which jobs chain to which.

Step 2 of `plans/5-Windows-version.md` ("Make the host reusable") needs exactly this split: `FetchshelfPaths`,
`FetchshelfApp.Build`/`RunAsync` and `FetchshelfJobs`. This plan does that step now, on its own, with no change in
behaviour, and adds what the Windows plan doesn't: one registration method per area and option records for the
runners.

## Change

Each step is its own commit and leaves the build, the tests and the running app unchanged.

### 1. Paths

`Hosting/FetchshelfPaths.cs`: `Resolve(Func<string, string?> getEnv, string? defaultDirectory)` returns the config
file, data file and log folder, as `Program.cs` works them out today (`FETCHSHELF_CONFIG_FILE` or
`ConfigLoader.ResolveDefaultConfigFilePath()`, `FETCHSHELF_DATA_FILE` or `data.db`, `FETCHSHELF_LOG_DIR`). Pure and
tested; `defaultDirectory` is `null` here and is what the Windows plan later sets.

### 2. One registration method per area

Extension methods on `IServiceCollection` in `Fetchshelf.Web/Hosting/`, each taking `FetchshelfSettings` (and the
paths where needed), moved out of `Program.cs` in this order:

| Method | Holds |
|---|---|
| `AddFetchshelfData(paths)` | data protection, instance id and antiforgery cookie, `DbContextFactory`, scoped context, `EventStore`, `LibraryQueries`, `IJobRunStore` |
| `AddFetchshelfNotifications(settings)` | `Notifier`, `NotificationDelivery`, the watchers, the hosted sender, `IJobRunListener` |
| `AddMam(settings)` | `MamClientOptions`, the `mam` HTTP client, `MamClient`, `MamAccessGate`, `IMetadataLookup` |
| `AddQBittorrent()` | the `qbittorrent` HTTP client, factory, presence cache |
| `AddAudiobookshelf(settings)`, `AddHardcover(settings)` | as today, only when configured |
| `AddOrganizer(settings)` | `FormatPreferenceLists`, `LinkRunner`, `Cleaner`, `ClientStatusChecker` |
| `AddAutoDownloader(settings)` | tag rules, `SelectionRunner`, the autograb, snatchlist, list, follow and upgrade runners, `SafetyBuffers`, `DownloadRunner`, `RetroactiveTagApplier` |
| `AddFetchshelfJobs(settings)` | `IJobRegistry` and the `IntervalJobScheduler`s |
| `AddFetchshelfWeb()` | Razor Pages options, health checks, `AccountSummaryBuilder` |

### 3. Option records for the runners

Where a runner takes plain settings values, it takes one record instead, registered once as a singleton:

- `DownloadRunnerOptions(double MinRatio, bool AddTorrentsStopped, TimeSpan? FinishAfterCancel = null)`.
- `LinkRunnerOptions` for `exclude_narrator_in_library_dir`, the libraries and the format lists it is handed.
- The same for any other runner that `Program.cs` builds with positional settings values; runners whose only
  arguments are services keep `AddScoped<T>()` and constructor injection.

The factory lambdas go; each runner is `services.AddScoped<DownloadRunner>()`. Optional constructor parameters
used only by tests (`delay`, `timeProvider`) stay, registered or defaulted as they are now.

### 4. Jobs

`Hosting/FetchshelfJobs.cs`: `RegisterOrganizerJobs`, `RegisterAutoDownloaderJobs` and `ConnectMamNotifications`
move here unchanged, with the chaining between jobs (Link → Clean, a selection → Download). `JobNames` and
`AutoDownloaderJobNames` move with them.

### 5. The app

`Hosting/FetchshelfApp.cs`:

- `Build(string[] args, FetchshelfAppOptions options)` — from `CreateBuilder` to the middleware, returning the app
  or the configuration error. `builder.Build()` stays on the main path, which `WebApplicationFactory` relies on.
- `RunAsync(string[] args)` — today's flow: the command-line modes, logging, migrations, `LoadLastRunsAsync`, the
  shutdown hooks, `RunAsync`.

`Program.cs` becomes `return await FetchshelfApp.RunAsync(args);` plus `public partial class Program;`.

### 6. The Windows plan

`plans/5-Windows-version.md` step 2 is reduced to what is left for Windows: `defaultDirectory`, the content root and
the log hint on the Error page.

## Tests

- `FetchshelfPathsTests`: every combination of the three variables, and the defaults.
- `CompositionTests` (exists): every runner and page model resolves from the container for a configuration with
  everything on, and for a minimal one. Extended to assert that `DownloadRunnerOptions` carries `min_ratio` and
  `add_torrents_stopped` from the config.
- The whole suite passes unchanged after each step; that is the main test of a move like this.

## Documentation

- `docs/contributors-guide.md`, "How the code is organized": where registrations live, and that a new setting for a
  runner goes into its options record.
- `CLAUDE.md` (local): "adding a new scheduled job means registering it in `Program.cs`" becomes
  `Hosting/FetchshelfJobs.cs`.

## Release notes

Nothing user-visible. Under the next release's internal changes: "`Program.cs` split into `Hosting/`".

## Out of scope

- Any change in what is registered or when jobs run.
- The Windows-specific parts of the Windows plan.
