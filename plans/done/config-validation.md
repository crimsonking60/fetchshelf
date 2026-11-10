# Configuration: catch every mistake at startup, and check a file without starting — implementation plan

## Context

`ConfigLoader.Load` (`src/Fetchshelf.Data/Configuration/ConfigLoader.cs`) rejects unknown keys, missing
required keys, conflicting keys and unknown enum-like values, and Fetchshelf then refuses to start with
"Cannot start with <file>: Invalid configuration: …". Several kinds of mistake get past it:

1. **No range checks on numbers.** Every numeric setting takes any value the type holds.
   - `min_ratio = 0` or negative: the ratio budget becomes unlimited or nonsense, so the ratio protection is
     silently off. TOML also allows `inf` and `nan`.
   - A negative interval silently means "UI only" (`IntervalJobScheduler` returns for `interval <= 0`).
   - An interval above about 71,500 minutes makes `Task.Delay` throw in the scheduler's background service,
     which by default stops the whole host.
   - `web_port = 70000` fails when Kestrel binds, with a framework error.
   - Negative buffers, `max_pages = 0`, `max_active_downloads = 0`, `min_seeders` above `max_seeders`,
     `min_size` above `max_size`, `uploaded_after` after `uploaded_before`: accepted, and the block silently
     selects nothing or everything.
2. **Environment overrides parse with `int.Parse`, `double.Parse`, `bool.Parse` and `Enum.Parse` directly**
   (`EnvironmentOverrides`). `FETCHSHELF_CONF_search_interval=ten` throws a `FormatException` that nothing
   catches: Fetchshelf crashes with a stack trace instead of the usual one-line message, and the message
   doesn't say which variable was wrong.
3. **Filter values are checked late.** The shared torrent filter's names and values (media types, categories,
   languages, flags, sizes, dates) are converted by `SharedFilterConfigMapper.ToTorrentFilter` only when an
   `[[autograb]]`, `[[snatchlist]]` or `[[hardcover_list.grab]]` job runs. A misspelled category is found at the
   first run, up to `search_interval` minutes after start, as a failed job. `docs/configuration.md` documents
   this as a known limitation.
4. **Three values crash at startup instead of being rejected.**
   - `[[tag]]` filters are converted in `Program.cs` after the `try` around `ConfigLoader.Load`, so a bad value
     there throws an unhandled `ConfigurationException`.
   - A size the mapper can't read (`min_size = "5 gigs"`) throws `FormatException` from `MamSizeParser`, not
     `ConfigurationException`, wherever it is converted.
   - `ignore_torrents` is converted with `long.Parse` in `RegisterAutoDownloaderJobs`; `ignore_torrents =
     ["12a"]` crashes the host after the database migration has run.
5. **`[search] wedge_over` is parsed on every Search page request**, so a bad value makes every search fail
   instead of stopping startup.
6. **No way to check a file without starting the app.** Editing `config.toml` means restarting, and a mistake
   means the service is down until it is fixed.

Reloading the configuration without a restart is not part of this plan: settings are captured in singletons
and factory lambdas throughout `Program.cs`, and that is a much larger change. Duplicate and reserved job names
are a separate problem with its own plan.

## Part 1: Range checks

A new `ValidateRanges(settings)` step in `ConfigLoader.Load`, after `ApplyEnvironmentOverrides` (so overridden
values are checked too) and before the existing validations. Each message names the setting and, inside a
block, the block (`the [[autograb]] 'Bookmarks'`, or `the 2nd [[autograb]] section` when it has no `name`),
in the same "Invalid configuration: …" form as today.

| Setting | Rule | Why |
|---|---|---|
| `min_ratio` | finite and > 0 | 0 or below switches the ratio protection off |
| `search_interval`, `link_interval`, `[audiobookshelf] interval`, per-block `search_interval` | 0 to 43,200 (30 days) | 0 already means UI only; the upper bound stays well inside what `Task.Delay` accepts |
| `unsat_buffer`, `wedge_buffer`, global and per autograb | ≥ 0 | |
| `max_active_downloads` | ≥ 1 | 0 would block the block for good; leave it unset for no cap |
| `max_pages`, autograb and snatchlist | ≥ 1 | |
| `web_port` | 1 to 65,535 | |
| `min_seeders`, `max_seeders`, `min_leechers`, `max_leechers`, `min_snatched`, `max_snatched` | ≥ 0, and min ≤ max when both are set | |
| `max_authors` | ≥ 1 | |
| `min_size`, `max_size` | readable sizes (Part 3), and min ≤ max | |
| `uploaded_after`, `uploaded_before` | readable dates (Part 3), and after ≤ before | |
| `ignore_torrents` | each a positive whole number | today `long.Parse` crashes the host |

`ignore_torrents` stays a list of strings in the file (documented that way); the check makes the later
`long.Parse` in `Program.cs` safe. The shared filter's numeric fields are checked in one place for every block
type, including `[[tag]]`.

The rules are a reasoned guess at what makes sense, not taken from anywhere. The upper bound on intervals and
`max_active_downloads ≥ 1` are the two that could reject a value someone uses today; both are called out under
Upgrading.

## Part 2: Environment overrides that say what is wrong

Replace the bare `int.Parse`/`double.Parse`/`bool.Parse`/`Enum.Parse` calls in `EnvironmentOverrides` with small
helpers (`ParseInt`, `ParseDouble`, `ParseBool`, `ParseLinkMethod`) that take the variable's name and throw
`ConfigurationException`:

```
Invalid configuration: FETCHSHELF_CONF_search_interval is 'ten'; expected a whole number.
Invalid configuration: FETCHSHELF_CONF_add_torrents_stopped is 'yes'; expected true or false.
Invalid configuration: FETCHSHELF_CONF_link_method is 'hard'; expected one of hardlink, hardlink_or_copy, hardlink_or_symlink, copy, symlink, no_link.
```

Accepted forms stay as today (`bool.Parse` is case-insensitive; numbers use the invariant culture). The list of
link methods comes from the enum, in the config file's spelling. Range checks for overridden values come from
Part 1, since it runs after the overrides.

## Part 3: Every filter checked at startup

A new `ValidateFilters(settings)` step in `ConfigLoader.Load`, after `ApplyGlobalLanguages` (so the languages
each block inherits are checked in the block they end up in). It calls `SharedFilterConfigMapper.ToTorrentFilter`
for every `[[autograb]]`, `[[snatchlist]]`, `[[hardcover_list]]` `grab` block and `[[tag]]`, and rethrows a
failure with the block named:

```
Invalid configuration: the [[autograb]] 'Bookmarks' has an unrecognized category name 'Fantasyy'.
```

Changes that go with it:

- `SharedFilterConfigMapper` catches `FormatException` from `MamSizeParser.ParseBytes` and throws
  `ConfigurationException` ("unrecognized size '5 gigs', expected a number and a unit such as 500 MiB or 1.5 GB"),
  so a bad size is reported like every other bad value.
- `[search] wedge_over` is parsed once in `ConfigLoader` with the same size rule, and stored as bytes next to the
  string (`SearchSettings.WedgeOverBytes`). The Search page reads the stored value instead of parsing on every
  request.
- The runners keep converting their filter on each run: by then it can't fail, and holding converted filters on
  the settings objects would spread the change across every runner for no gain.
- `Program.cs`'s `[[tag]]` conversion can't throw any more either, so it needs no `try` of its own.

## Part 4: `--check-config`

A new command-line flag, handled at the top of `Program.cs` like `--healthcheck`, before logging, the web host
and the database are set up:

```
docker exec fetchshelf dotnet /app/Fetchshelf.Web.dll --check-config
```

It loads the file `FETCHSHELF_CONFIG_FILE` names (or the default) through `ConfigLoader.Load`, with the same
environment overrides the running app has, so `docker exec` into the running container checks exactly what a
restart would load. It contacts nothing (no MAM, qBittorrent, Audiobookshelf or Hardcover) and doesn't open
the database. A file that does not exist is reported as missing instead of being created, unlike a normal start.

On success it prints what it understood and exits with 0; on failure it prints the same message startup would
and exits with 1. The summary, as plain text:

```
Configuration OK: /config/config.toml

MAM: mam_id set (from mam_id_file); background jobs enabled
Safety: min_ratio 2, unsat_buffer 10, wedge_buffer 0
qBittorrent: http://qbittorrent:8080 (1 path mapping)
Libraries:
  audiobooks  category audiobooks  -> /library/audiobooks  hardlink_or_copy, metadata.json
  ebooks      category ebooks      -> /library/ebooks      hardlink_or_copy, metadata.json, metadata.opf
Jobs:
  Link             every 10 min
  Clean            after Link
  Download         every 30 min, and after selections
  Bookmarks        autograb bookmarks, cost free, every 30 min
  autograb[1]      autograb new, cost wedge, UI only (search_interval 0)
  Want to read     Hardcover want_to_read, 2 grab blocks, every 30 min
  Audiobookshelf   every 10 min
Tag rules: 3
OPDS: libraries ebooks
```

Secrets are never printed: `mam_id`, passwords and tokens show only whether they are set and where they came
from. The job names come from the existing `JobNames` and `AutoDownloaderJobNames`, so the summary matches the
Jobs page; that means `ConfigCheck` lives in `Fetchshelf.Web` and the two helpers move from the bottom of
`Program.cs` into their own file there. The summary is built by a pure function over `FetchshelfSettings`
(`ConfigSummary.Build`) so it can be tested without running the host.

## Tests

`tests/Fetchshelf.Data.Tests`, following the existing per-area config test files (`ConfigLoaderTests`,
`AutograbConfigTests`, `SharedFilterConfigMapperTests`, …):

- One theory per range rule: the boundary values pass, one step past them is rejected, and the message names
  the setting and the block.
- `min_ratio` of `inf` and `nan` are rejected.
- A value set by an environment override that breaks a range rule is rejected.
- Each override helper: a bad value gives a `ConfigurationException` naming the variable and the expected form;
  good values parse as before.
- A bad category, language, flag, media type, size and date in each block type (`[[autograb]]`, `[[snatchlist]]`,
  `[[hardcover_list]]` grab, `[[tag]]`) is rejected at load, naming the block. An inherited top-level language
  is checked in the blocks it reaches.
- A bad `wedge_over` is rejected at load; a good one is stored in bytes.
- `ignore_torrents = ["12a"]` is rejected at load.
- The example configurations in `docs/config-examples/` still load (`ExampleConfigTests` covers this).

`tests/Fetchshelf.Web.Tests`:

- `ConfigSummary.Build` for a configuration with each kind of block: job names and intervals as the Jobs page
  shows them, "UI only" for interval 0, no secret values in the text.
- `--check-config` end to end through the same entry point the container uses: exit code 0 and the summary for a
  good file, 1 and the error for a bad one, nothing created for a missing file, and no database file created.
- The Search page uses the stored `wedge_over` bytes (the existing Search page tests keep passing).

## Documentation

- `docs/configuration.md`:
  - The **Validation** bullet lists the range checks, and that environment overrides are checked the same way.
  - The **Filter values are checked when they are first used** bullet goes: every filter is checked at startup.
  - The tables for the settings in Part 1 state the allowed range where it isn't obvious (`min_ratio`,
    intervals, `max_active_downloads`, `max_pages`).
  - A short **Checking a configuration** section for `--check-config`, with the `docker exec` line and the
    exit codes.
- `docs/user-guide.md`: where it says to restart after editing `config.toml` (the **Changes need a restart**
  bullet, the setup steps, and the `docker compose restart fetchshelf` row in the command table), suggest
  running `--check-config` first. The troubleshooting section mentions it for a container that won't start.

## Release notes

Under **Changed**: mistakes in filters, sizes, dates, `ignore_torrents` and numeric settings stop Fetchshelf at
startup with a message naming the setting, instead of failing a job later or crashing. Under **Added**:
`--check-config`. Under **Upgrading**: a configuration that has worked so far can now be refused, if it has an
interval above 30 days, a negative interval, buffer or seeder bound, `max_active_downloads = 0`, `min_ratio` of
0 or below, or a filter value that was wrong all along (that block's job was already failing on every run).
Run `--check-config` against the new image before switching:

```
docker run --rm --entrypoint dotnet -v /path/to/config:/config:ro ghcr.io/crimsonking60/fetchshelf:<version> /app/Fetchshelf.Web.dll --check-config
```

Written at release time, not in this change.

## Order of work

One feature branch, one PR, a commit per part in the order above: 2 and 4 depend on nothing, but 4's summary
is most useful once 1 and 3 make "OK" mean the file will really start.

## Out of scope

- Reloading the configuration without a restart.
- Duplicate and reserved job and library names.
- Remembering job results across restarts and running jobs soon after start, which together make restarts
  cheaper.
- Checking that qBittorrent, Audiobookshelf or Hardcover can be reached, or that `library_dir` exists: those
  are about the environment, not the file, and the `/health` endpoint and the Torrents page's **Check qBittorrent**
  already cover qBittorrent.
