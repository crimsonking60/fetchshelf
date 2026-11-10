# Issue #5: Windows version — implementation plan

## Context

Fetchshelf ships only as a Linux Docker image. Issue #5 asks for a native Windows build that runs as a
system tray app. Decisions already made:

- **Delivery:** a self-contained zip (`Fetchshelf-<version>-win-x64.zip`) attached to every GitHub release.
  Unpack anywhere, run `Fetchshelf.exe`. No installer for now.
- **Data folder:** `%LOCALAPPDATA%\Fetchshelf` holds `config.toml`, `data.db`, `keys\` and `logs\`, unless the
  `FETCHSHELF_*` environment variables say otherwise.
- **Tray menu:** Open Fetchshelf (also on double-click), Open data folder, Open log, Start at login (toggle),
  Exit.

What stands in the way today (found by reading the code):

- Hardlinks call the POSIX `link()` in libc (`src/Fetchshelf.Data/Organizer/FileLinker.cs:103`). On Windows that
  throws `DllNotFoundException`, which the `hardlink_or_copy` fallback does not catch.
- `Program.cs` is one block of top-level statements ending in a blocking `app.Run()`; nothing can start or stop
  the host from a tray UI. Config and data default to the working directory, which for an app started at login
  is `C:\Windows\System32`. Without `FETCHSHELF_LOG_DIR` there is no log file, and a tray app has no console.
- Path comparisons assume Linux: case-sensitive, `/` only, and raw string prefixes (`PathMapper.cs:12`,
  `LibraryMatcher.cs:48`, `LibraryMismatchState.cs:27,32`, `AbsMatcher.cs:130-134`,
  `LibraryFileRemover.cs:32`, `LinkFilePlacer.cs:13,24`).
- Folder names are cleaned with `Path.GetInvalidFileNameChars()` (`FolderNameComputer.cs:78-82`). On Linux that
  is only `\0` and `/`, so names with `:`, `?` or a trailing `.` are invalid on Windows.
- CI and releases run only on Ubuntu; the release has no files attached.
- The docs say Docker only, and `configuration.md` claims a "platform-appropriate config path" that the code
  doesn't have.

## Approach

### 1. Project layout: two targets in `Fetchshelf.Web`

`src/Fetchshelf.Web/Fetchshelf.Web.csproj` targets `net10.0;net10.0-windows`, with the tray code in
`src/Fetchshelf.Web/Windows/`, compiled for the Windows target only. A separate exe project referencing
Fetchshelf.Web would lose the web app's static files (CSS, htmx): the Web SDK doesn't pass a root project's
`wwwroot` on to projects that reference it, and `MapStaticAssets()` looks for the manifest named after the
running app.

- Windows target: `OutputType=WinExe`, `UseWindowsForms=true` (for `NotifyIcon`), `AssemblyName=Fetchshelf`,
  `ApplicationIcon=Windows\fetchshelf.ico`, `RootNamespace=Fetchshelf.Web`.
- `EnableWindowsTargeting=true`, so `dotnet build` on Linux also compiles the Windows code and the gates catch its
  errors. `NETSDK1074` (only Windows can embed the icon and version) must not fail Linux builds, so it isn't
  treated as an error there.
- `Program.cs` (top-level) stays for `net10.0`; the Windows target has its own `[STAThread] Main` in
  `Windows/WindowsProgram.cs`. Tests stay on `net10.0`, so `WebApplicationFactory<Program>` is unaffected.
- Consequences: `dotnet run --project src/Fetchshelf.Web -f net10.0`; the Dockerfile publishes with
  `-f net10.0`.

### 2. Make the host reusable (no change on Linux)

Move `Program.cs` into `src/Fetchshelf.Web/Hosting/`:

- `FetchshelfPaths.Resolve(getEnv, defaultDirectory)`: pure and tested. It reads `FETCHSHELF_CONFIG_FILE`,
  `FETCHSHELF_DATA_FILE` and `FETCHSHELF_LOG_DIR`. `defaultDirectory` is `null` on Linux (today's defaults) and
  `%LOCALAPPDATA%\Fetchshelf` on Windows, where the log folder defaults to `logs\`. An empty `FETCHSHELF_LOG_DIR`
  still means no log file, as in Docker.
- `FetchshelfApp.Build(args, options)`: everything from `CreateBuilder` to the middleware, with an optional
  content root (Windows: `AppContext.BaseDirectory`) and the log hint for the Error page.
  - Returns either the app or the configuration error.
  - Keeps `builder.Build()` on the main path, which `WebApplicationFactory` relies on.
- `FetchshelfApp.RunAsync(args)`: today's flow. It handles `--healthcheck`, logging, `--update-search-title` and
  then runs the web server.
- `FetchshelfJobs`: the job registration, `JobNames` and `AutoDownloaderJobNames`, moved out of `Program.cs`.
- `Program.cs` becomes `return await FetchshelfApp.RunAsync(args); public partial class Program;`.
- `ConfigLoader.EnsureConfigFileExists(path, initialContent)` becomes public and reports whether it created the
  file.
- `Pages/Error.cshtml`: the "`docker logs fetchshelf`" hint comes from the host instead of being written into
  the page.

### 3. Paths and folder names

New pure helpers in `Fetchshelf.Core/Organizer/`:

- `PathText`:
  - `IsSameOrUnder`, `AreSame` and `StartsWithPrefix`, which treat `/` and `\` as equal, ignore trailing
    separators and take the string comparison as a parameter.
  - `PathText.Comparison` is `OrdinalIgnoreCase` on Windows and `Ordinal` elsewhere.
  - Tests run both comparisons on Linux.
- `FileNameRules`: `Posix` (exactly today's behaviour) and `Windows`.
  - The Windows rules remove ``<>:"/\|?*`` and control characters, strip trailing dots and spaces, and add `_`
    to reserved names (`CON`, `NUL`, `COM1`, … also with an extension).
  - `FileNameRules.Current` is chosen by OS. `FolderNameComputer.Compute` takes the rules as an optional
    parameter.
  - Docker users get exactly the same folder names as today, so there are no new mismatch flags. The same book
    gets different folder names on Linux and Windows; moving a database between the two isn't supported.

Use them in:

- `PathMapper.cs:12`: a prefix match, as documented.
- `LibraryMatcher.cs:48`: `IsSameOrUnder`. This also fixes a small Linux bug: `download_dir = "/dl/audio"`
  wrongly matched `/dl/audiobooks`. It goes in the release notes.
- `LibraryMismatchState.cs`, `AbsMatcher.cs:117,130-134` and `LibraryFileRemover.cs:32`.
- `LinkFilePlacer.cs`: split on both separators instead of converting and splitting.

Documented limits for now:

- folder names that differ only by case share a folder on Windows;
- paths over 260 characters need Windows' long-path setting;
- Linux Fetchshelf with a Windows qBittorrent keeps today's behaviour.

### 4. Hardlinks and symlinks (`src/Fetchshelf.Data/Organizer/FileLinker.cs`)

- On Windows, `CreateHardLinkW` from kernel32. Its argument order is new link first, the reverse of `link()`.
  It uses full paths, with the `\\?\` prefix for long paths. Linux keeps `link()`.
- Failures become `IOException` naming the Windows error: different volume, not NTFS, access denied, too many
  links. That way `hardlink_or_copy` and `hardlink_or_symlink` fall back as documented. `TryOrFallback` also
  catches `UnauthorizedAccessException`.
- When a symlink is refused (error 1314, privilege not held), the message names the fixes: Developer Mode, or
  `link_method = "hardlink_or_copy"`.
- The default `link_method` stays `hardlink`. The docs recommend `hardlink_or_copy` on Windows when downloads
  and library might be on different volumes or a network share.

### 5. The tray app (`src/Fetchshelf.Web/Windows/`)

- **`WindowsProgram.Main`:**
  1. Resolve the paths with the `%LOCALAPPDATA%\Fetchshelf` default.
  2. Run the single-instance check.
  3. Create `config.toml` from `WindowsDefaults.InitialConfig` on first run.
  4. Set up logging and `FetchshelfApp.Build`.
  5. On a configuration error, show the startup error dialog.
  6. `StartAsync`; if the port is taken, show a dialog naming `web_port`.
  7. `Application.Run(new TrayApplicationContext(...))`.
- **`WindowsDefaults.InitialConfig`:** `mam_id = ""` and `web_host = "127.0.0.1"`, with a comment on how to
  allow other machines.
  - There's no Windows Firewall prompt on first start, and a Fetchshelf without login isn't reachable on public
    Wi-Fi by default.
  - The built-in default (`0.0.0.0`) is unchanged.
- **`SingleInstance`:** a named mutex per data file. Starting it again only opens the browser and exits.
- **`BrowserUrl`** (cross-platform, tested): turns `web_host`/`web_port` into an address the browser can open.
  `0.0.0.0` becomes `localhost`, and IPv6 addresses get brackets.
- **`TrayApplicationContext`:**
  - `NotifyIcon` with the tooltip "Fetchshelf <version>".
  - Menu: **Open Fetchshelf** (default, also on double-click), **Open data folder**, **Open log** (the newest
    log file), **Start at login** (checkable), **Exit**.
  - On first run, a notification with the address, and the browser opens once.
  - **Exit** and Windows sign-out stop the host off the UI thread (`StopAsync`, 15 s), flush the log, remove the
    icon and end the app. If the host stops by itself, the tray app ends too.
- **`StartAtLogin`:** the value `Fetchshelf` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, pointing
  at the exe. If the folder was moved, the value is updated at start.
- **`StartupErrorDialog`:** a `TaskDialog` with the configuration error and **Open config file**, **Open log
  folder** and **Close**.
- **Icon:** `Windows/fetchshelf.ico` at 16–256 px, made once from `docs/images/logo-*.svg` and committed. It
  becomes the exe's icon and the tray icon.

### 6. Tests that pass on Windows too

- Shared `tests/Shared/PlatformFacts.cs`, linked into the test projects: `[PosixFact]`, `[WindowsFact]`, and
  `[SymlinkCapableFact]` (checked once at run time).
- Tests that check pure string logic pass `Ordinal` or `FileNameRules.Posix` explicitly, so they give the same
  result everywhere. Expected paths use `Path.Combine` where that makes sense.
- SQLite keeps database files open in its connection pool, which stops Windows from deleting the temp folders.
  The database and web fixtures clear the pool when they're disposed (`SqliteConnection.ClearAllPools()`).
- New `[WindowsFact]` tests:
  - `CreateHardLinkW` on the same volume;
  - case-insensitive matching;
  - `FileNameRules.Current` is the Windows rules.

### 7. CI and packaging

- **`checks.yml`:** the Ubuntu job compiles both targets. A new `windows-latest` job builds and runs all tests.
  The format check stays on Linux.
- **`utils/publish-windows.ps1`:**
  - Runs `dotnet publish src/Fetchshelf.Web -c Release -f net10.0-windows -r win-x64 --self-contained`, with
    `-p:Version` or `-p:InformationalVersion` mapped the same way the Dockerfile does it.
  - Zips the result as a top-level `Fetchshelf\` folder.
  - Publishes as a plain folder: no single-file exe and no trimming, which EF Core and Razor don't support.
  - x64 only; Windows on ARM runs it through emulation.
- **`develop-image.yml`:** a `windows-zip` job on `windows-latest` uploads `Fetchshelf-develop-<sha7>-win-x64.zip`
  as a workflow artifact, kept for 30 days, so develop builds can be tested.
- **`release.yml`:** reading the version becomes its own job. A `windows-zip` job builds the zip, and the release
  job creates the GitHub release with the zip attached, so a release never exists without it.
- **`Dockerfile`:** `dotnet publish … -f net10.0`.

### 8. Documentation

- **`README.md`:** Getting started has two routes, Docker and Windows. The Untested list gets "the Windows build"
  until you confirm it.
- **`docs/user-guide.md`:**
  - A new "Installing on Windows" section covers:
    - the download and the SmartScreen warning for an unsigned exe (More info → Run anyway);
    - the tray menu, the data folder and Start at login;
    - `web_host` and the firewall;
    - which link method to use;
    - `path_mapping` examples for a qBittorrent on a NAS (UNC path or drive letter).
  - Updating: exit from the tray and replace the folder.
  - Backups: copy `%LOCALAPPDATA%\Fetchshelf`.
  - Troubleshooting: wherever it says `docker logs`, add "or **Open log** in the tray menu".
- **`docs/configuration.md`:** the real rule for where the files are (Docker, Windows, else the working
  directory), case-insensitive matching on Windows, and `path_mapping` between `/` and `\`.
- **`docs/contributors-guide.md`:**
  - Windows works as a development platform with the .NET 10 SDK.
  - `dotnet run -f net10.0` / `-f net10.0-windows`.
  - The `Windows/` folder.
  - Fix the stated `FETCHSHELF_CONFIG_FILE` default.
- **`docs/maintainer-guide.md`:** releases include the zip, and checking a release includes it.
- **`CLAUDE.md`:** the commands with `-f`, the config-path sentence, and "Docker and Windows tray" instead of
  "Docker-first".
- **`RELEASES.md`:** written on the release branch as usual. It mentions the `download_dir` matching fix.

## Order of work

Each step is one commit on `develop`, with build, tests and format passing.

1. **Spike** (throwaway, not merged):
   - add the two targets and cross-publish `win-x64` from Linux;
   - check the output has `Fetchshelf.exe`, `Fetchshelf.staticwebassets.endpoints.json` and the `wwwroot` files;
   - check that `dotnet test` and `dotnet format` still work;
   - you run it once on Windows: the page loads with its styles.

   If static files or Razor break, the fallback is moving the pages and `wwwroot` into a Razor class library.
2. `PathText`, `FileNameRules` and the path fixes, with tests.
3. Windows hardlinks and the wider fallback in `FileLinker`, with tests.
4. Tests made cross-platform, and the Windows job in `checks.yml`.
5. The `Program.cs` refactor (`FetchshelfPaths`, `FetchshelfApp`, `FetchshelfJobs`) and the Error-page hint.
6. The two targets in the csproj, the Dockerfile `-f net10.0`, and the command updates.
7. The tray app: program, tray menu, single instance, start at login, startup dialog, icon, first config.
8. `publish-windows.ps1`, the develop artifact and the release workflow.
9. Documentation.

The first release with the zip follows the normal release branch and pull requests.

## Risks

- **Static files and Razor under the Windows target**, with the renamed assembly: the spike settles this first.
- **The `Program.cs` refactor and the web tests:** `builder.Build()` stays on the main path, and no catch-all is
  wrapped around it.
- **Deadlocks between the tray's UI thread and the host:** the host is stopped from a background task.
- **Unsigned exe:** SmartScreen and some antivirus products warn. This is documented; code signing can come
  later.
- **Hardlinks** fail across volumes, on FAT/exFAT and often on network shares. The docs say so, and
  `hardlink_or_copy` falls back.
- **Behaviour change on Linux:** `download_dir` now matches only whole folders. It's mentioned in the release
  notes.

## Verification

- **Linux, local:**
  - `dotnet build` (both targets), `dotnet test`, `dotnet format --verify-no-changes`;
  - `docker compose up --build` starts and `/health` answers;
  - `dotnet run --project src/Fetchshelf.Web -f net10.0` works.
- **New unit tests, run on Linux:**
  - `FetchshelfPaths.Resolve` and `BrowserUrl`;
  - `PathText` with both comparisons and both separators;
  - `FileNameRules.Windows` with `:`, `?`, trailing dots, `CON` and `nul.txt`;
  - `FolderNameComputer` with both rule sets, with the Posix output unchanged.
- **Windows CI:** the full test run on `windows-latest`, including the Windows-only hardlink tests.
- **Manual test on Windows 10 and 11** with the develop zip:
  - First start creates `%LOCALAPPDATA%\Fetchshelf` with `config.toml`, `data.db`, `keys` and `logs`. The
    notification shows, and the browser opens with styles and htmx working.
  - Double-click opens the UI. A second start only opens the browser.
  - Start at login survives a reboot and a moved folder.
  - A broken `config.toml` shows the dialog, and its buttons work. A taken port shows the dialog too.
  - **Exit** removes the icon, and the log ends cleanly.
  - With `127.0.0.1` there's no firewall prompt; with `0.0.0.0` there is one.
  - With a local qBittorrent on the same NTFS volume, the Link job makes a hardlink (`fsutil hardlink list`).
    `hardlink_or_copy` across volumes copies.
  - A title with `:` and a trailing `...` gives a valid folder, and no mismatch flag on the next run.
  - A qBittorrent on a NAS, reached with `path_mapping` to a UNC path, links correctly.
