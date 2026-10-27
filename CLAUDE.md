# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Fetchshelf is a self-hosted service that connects the private book tracker MyAnonamouse (MaM) with
qBittorrent and, optionally, Audiobookshelf. It is a C#/.NET reimplementation of **MLM**
("MyAnonamouse Library Manager"), an abandoned Rust application, rebuilt from its behavioral
specification in `Fetchshelf-spec.md`. That spec is the ground truth for *what the app must do* — setting
names, external API behavior, and business rules (ratio/unsat/wedge safety, folder naming, dedup
logic, etc.) are all defined there, not invented locally. When implementing or changing behavior,
check `Fetchshelf-spec.md` first.

Fetchshelf has two independent, optional halves:

1. **Auto-downloader** — finds torrents on MaM matching user-defined rules and adds them to
   qBittorrent, while protecting the account's ratio, unsatisfied-snatch slots, and freeleech-wedge
   supply.
2. **Library organizer** — watches qBittorrent for finished torrents and links the wanted files into
   a clean `Author/Series/Title` folder tree suitable for Audiobookshelf and similar tools.

The project is under active, incremental development with no release yet. Target deployment is
single-user, self-hosted, no authentication (trusted network or reverse proxy), Docker-first.

## Commands

```
dotnet build                          # build (analyzers + TreatWarningsAsErrors run here — this is also the lint gate)
dotnet test                           # run all tests
dotnet test --filter "FullyQualifiedName~SeriesParserTests"   # run one test class
dotnet test --filter "FullyQualifiedName~SeriesParserTests.Parses_SingleEntry"  # run one test
dotnet format --verify-no-changes     # format check (CI gate; use `dotnet format` without the flag to fix)
```

Run the web host locally:

```
dotnet run --project src/Fetchshelf.Web
```

Config and data file locations are controlled by `FETCHSHELF_CONFIG_FILE` (defaults to a
platform-appropriate config path, else `config.toml`) and `FETCHSHELF_DATA_FILE` (defaults to
`data.db`). `example-config.toml` is a working example to copy from. Logging is controlled by
`FETCHSHELF_LOG` (level) and `FETCHSHELF_LOG_DIR` (enables a rolling file sink; console/stderr sink is
always on).

Docker: `docker compose up --build` (see `docker-compose.yaml`); the container mounts `/config` and
`/data` and remaps to `PUID`/`PGID` via `docker-entrypoint.sh`.

There is no separate lint step — Roslyn analyzers plus `.editorconfig` severities run during `dotnet
build`, with `TreatWarningsAsErrors` set in `Directory.Build.props`, so a clean build is the lint gate.

## Architecture

### Project graph

A multi-project solution split along two independent axes: **Core vs. Data vs. Web** (domain logic /
persistence+orchestration / HTTP host) and **base vs. AutoDownloader** (library-organizer concerns vs.
auto-downloader concerns, which are optional and independent per the spec). Dependencies point one
way:

```
Fetchshelf.Core                 domain logic, no I/O: metadata normalization/dedup, series parsing,
                                 folder-name/library-match logic, in-memory job registry
Fetchshelf.Core.AutoDownloader   auto-downloader domain logic: cost policy, selection budget/ranking,
                                 torrent filter matching (depends on Core)

Fetchshelf.Clients.Mam          typed HttpClient for MaM (search, snatch, cookie/account state)
Fetchshelf.Clients.QBittorrent  typed HttpClient for qBittorrent's Web API
Fetchshelf.Clients.Abs          typed HttpClient for Audiobookshelf (depends on Core)
Fetchshelf.Clients.Goodreads    Goodreads RSS/HTML list parsing (AngleSharp)
Fetchshelf.Clients.Notion       Notion list integration

Fetchshelf.Data                 EF Core DbContext/migrations/entities, config loading (TOML via
                                 Tomlyn → converted to YAML text → parsed by YamlDotNet, see
                                 ADR-0005), EventStore, library organizer runners (LinkRunner,
                                 Cleaner, AbsMatcher). Depends on Core, Core.AutoDownloader, and the
                                 Mam/QBittorrent/Abs clients.
Fetchshelf.Data.AutoDownloader  auto-downloader runners that need persistence + clients:
                                 AutograbberRunner, SelectionRunner, DownloadRunner, list importers.
                                 Depends on Data, Core.AutoDownloader, and all client projects.

Fetchshelf.Web                  ASP.NET Core host: Program.cs wires up DI, Razor Pages under
                                 Pages/<Area>/, background job scheduling.
```

One test project per `src` project (`tests/<ProjectName>.Tests`), added as each area lands — not
every `src` project necessarily has a matching test project yet.

### Config pipeline

Settings are TOML (`config.toml`) only; setting *names* are taken verbatim from `Fetchshelf-spec.md` §4 since
that's part of the user-facing contract carried over from the original Rust app, but the file format
and parsing pipeline are a local reimplementation detail. `ConfigLoader` (in
`Fetchshelf.Data/Configuration/`) parses TOML via Tomlyn into a generic object model, converts it to
plain dictionaries/lists/scalars, re-serializes as YAML text, and runs it through YamlDotNet
(`.IgnoreUnmatchedProperties()` is **not** used — unknown keys must be rejected per spec §4). See
`docs/adrs/ADR-0005-config-file-format.md` for the full rationale and its update history before
changing this pipeline — the YAML-text round-trip looks redundant but is intentional (avoids a
hand-rolled Tomlyn model-binding/validation layer).

### Background jobs

Every background job (interval-based or on-demand) is registered by name in `IJobRegistry`
(`Fetchshelf.Core/Jobs/`), an in-memory, non-persisted registry mapping job name → async delegate +
last-run record. `IntervalJobScheduler` (`Fetchshelf.Web/Jobs/`) is a thin `BackgroundService` that
calls `IJobRegistry.TriggerAsync(name)` on a configured interval; an interval of `0` means "UI-trigger
only" (spec §4.1) and the scheduler exits immediately without looping. The Home page's "run now"
buttons call `TriggerAsync` directly — adding a new scheduled job means registering it in
`Program.cs` and, if it should run on an interval, adding an `IntervalJobScheduler` hosted service for
it there too. One runner class per job (e.g. `LinkRunner`, `AutograbberRunner`, `SelectionRunner`,
`DownloadRunner`) lives in `Fetchshelf.Data`/`Fetchshelf.Data.AutoDownloader` and does the actual work;
the job registry entry is just a named pointer to it.

`MamAccessGate` (`Fetchshelf.Clients.Mam`) is a singleton semaphore ensuring at most one MaM-talking
job (autograbber, list import, snatchlist import) runs at a time (spec §5.1) — any new job that calls
`MamClient` must acquire this gate around those calls.

### Persistence and events

SQLite via EF Core (`FetchshelfDbContext`), with migrations applied at application startup (safe here
specifically because deployment is single-instance/single-user — see ADR-0003's caveat before
assuming this generalizes). `EventStore` is an append-only log (spec §3.3): it exposes no update
operation, by design — once an event is written its fields never change. Don't add an `UpdateAsync`
to it; if a workflow needs to record a correction, append a new event instead.

### Web UI

Server-rendered ASP.NET Core Razor Pages (not an SPA — ADR-0002), with htmx for in-place updates
(partial views like `Pages/Jobs/_JobListPartial.cshtml`, polling for live job status). Pages are
organized one folder per spec area under `Pages/` (`Torrents/`, `Selected/`, `Errors/`, `Events/`,
`Lists/`, `Config/`), matching `Fetchshelf-spec.md` §10's own page list — when adding a page, follow
that mapping for spec traceability. The Search page (`Fetchshelf-spec.md` §10.4) is the exception: it
lives at `Pages/Index.cshtml` and doubles as the application's home page, while the former Home page
(the background-job list, §10.1) now lives at `Pages/Jobs/`. No authentication layer exists or is
planned for v1 (ADR-0004); don't add one speculatively.

### Testing conventions

xUnit, `[Fact]`/`[Theory]`+`[InlineData]`. Tests for `Fetchshelf.Core`/`Fetchshelf.Core.AutoDownloader`
are pure unit tests against domain logic (no I/O, no DB). Favor the same style for new tests:
construct the pure object/function under test directly and assert behavior, rather than reaching for
mocks of EF Core or HTTP clients where the logic can be isolated in a Core-layer type instead.

## Working notes

- `Fetchshelf-spec.md` uses "MUST/SHOULD" for behavior that has to be reproduced and gives setting names in
  `code font` — treat both as binding. If an implementation choice isn't dictated by the spec, it's
  this reimplementation's own call.
- `Directory.Build.props` enables nullable reference types, implicit usings, and
  `TreatWarningsAsErrors` solution-wide — new projects inherit this automatically; don't override it
  per-project without reason.

## Dos and don'ts

- Do not put references to MLM or Fetchshelf-spec.md in comments, documentation or other files.
