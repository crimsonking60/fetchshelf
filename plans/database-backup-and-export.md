# Database backups while running, before upgrades, and a library export — implementation plan

## Context

The user guide's advice ("Backups and moving to a new machine") is to stop the container and copy `/config` and
`/database`, because a copy of `data.db` taken while Fetchshelf writes to it can be unusable, and with
`database_wal = true` recent changes live in `data.db-wal`. The upgrade section asks for a backup before every
upgrade and calls it "the only way back". Nothing in Fetchshelf takes one:

- `DatabaseMigrator.Migrate` runs pending EF Core migrations and the data upgrades (`user_version`) at startup,
  in place, with no copy taken first.
- There is no scheduled backup, so most installs have none.

SQLite's online backup API copies a consistent database while it is in use, WAL or not.
`Microsoft.Data.Sqlite` exposes it as `SqliteConnection.BackupDatabase(destination)`.

The library itself can only be exported with the `fs` command-line tool.

## Change

### 1. `DatabaseBackup` (Data)

- `CreateAsync(string dataFile, string backupFolder, string reason, CancellationToken)`: opens a connection to
  `data.db` and one to `<backupFolder>/data-<UTC yyyyMMdd-HHmmss>-<reason>.db.partial`, runs `BackupDatabase`, runs
  `PRAGMA integrity_check` on the copy, and renames it to `.db`. A failed check deletes the copy and throws.
- `Prune(backupFolder, keep)`: deletes the oldest `data-*.db` beyond `keep`, never anything else in the folder.
- The copy has no WAL file: it is complete on its own.

### 2. Settings

`[backup]`, optional:

| Setting | Default | Description |
|---|---|---|
| `folder` | (required) | Where backups go. Relative paths are relative to the data file's folder, so `folder = "backups"` is `/database/backups` in Docker. |
| `interval` | `1440` | Minutes between scheduled backups, `0` to 43200. `0`: only before upgrades and from the Jobs page. |
| `keep` | `7` | Scheduled backups kept, `1` to `365`. |
| `before_upgrade` | `true` | Back up before applying migrations or data upgrades, when there are any. Kept apart from `keep`: the last 3. |

The folder must not be the data file's own folder, so backups can't be mistaken for `data.db` and its `-wal`/`-shm`
files when copying or restoring; that is an error at load. A subfolder is fine, though a folder on another disk is
safer, which the docs say.

### 3. The Backup job

- Registered as `Backup`, scheduled by `interval`, and on the Jobs page with **Run now**.
- Summary: "Backed up 4.2 MiB to data-20261111-031500-scheduled.db; 7 kept".
- Notifications: a failed run is `job_failing` like any job.

### 4. Before an upgrade

In startup, before `DatabaseMigrator.Migrate`: when `dbContext.Database.GetPendingMigrations()` is not empty or the
data version is behind `CurrentDataVersion`, and `before_upgrade` is on, a backup named `…-before-<version>.db` is
taken. If it fails, startup stops with the error and says how to start without it (`before_upgrade = false`):
upgrading without the promised copy would be worse than not starting.

### 5. Library export

On the Torrents page, **Export** downloads the current filtered list as CSV or JSON: MAM id, title, authors,
narrators, series and number, media type, language, format, size, library, folder, added, linked. Written by a pure
`LibraryExport` class (Data) streaming rows, so 10,000 torrents don't load at once.

## Tests

- `DatabaseBackupTests`: a backup taken while another connection writes (WAL on and off) opens and passes
  `integrity_check`; names; `Prune` keeps the newest and touches no other file; a failure leaves no `.db`.
- Startup: with a pending migration (a database from an older test fixture), a `before` backup appears before
  migrating; with none, nothing.
- Config tests: `folder` equal to the data file's folder is refused; ranges.
- `LibraryExportTests`: CSV quoting, JSON shape, the filter applied.

## Documentation

- `docs/configuration.md`: `[backup]`.
- `docs/user-guide.md`, "Backups and moving to a new machine": with `[backup]`, backups are taken while running and
  before upgrades; restoring is stopping Fetchshelf and putting a backup in place as `data.db` (and removing
  `data.db-wal`/`-shm`). The upgrade section points to it. The Torrents page: **Export**.
- `docs/database-setup.md`: the backup folder should be on another disk or mount than the database if possible.

## Release notes

- Added: the optional Backup job copies the database safely while Fetchshelf runs, keeps the last N, and backs up
  before every upgrade that changes the database.
- Added: export of the Torrents page's list as CSV or JSON.
- Upgrading: nothing to do; add a `[backup]` table to turn backups on.

## Out of scope

- Backing up `config.toml`, the libraries or the downloads.
- Restoring from the UI.
