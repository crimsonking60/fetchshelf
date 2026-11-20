# Database setup

Fetchshelf keeps everything it remembers (torrents, selections, events and errors) in one SQLite database
file. This page covers where that file lives, what kind of disk it needs, and the `database_wal` setting.

## Where the database is

The `FETCHSHELF_DATA_FILE` environment variable sets the database's path. Without it, Fetchshelf uses
`data.db` in its working directory. The Docker image sets it to `/database/data.db`, so the database is in
the `/database` volume, separate from the configuration in `/config`.

The same folder also holds a few things Fetchshelf keeps beside the database: a `keys` folder with the keys
that protect the web interface's forms, an `instance-id` file, and a `covers` folder of cover images cached for
the OPDS catalog. Keep them with the database when moving it; the covers are rebuilt when missing.

## Local disk or network share

Keep the database on a disk that is local to the machine Fetchshelf runs on. A NAS that runs Fetchshelf in
its own Docker counts as local: the database is on the NAS's own disks. SQLite can't lock files reliably
across a network share (NFS or SMB), so a database reached over the network is at risk even with the default
settings.

## `database_wal`

With `database_wal = true` in the configuration file, SQLite uses its write-ahead log, which keeps the web
interface responsive while a long job writes to the database. The setting is off by default. It is read at
startup, and turning it off again switches an existing database back to SQLite's normal mode.

While it is on, SQLite keeps two working files next to the database: `data.db-wal` and `data.db-shm`. Leave
them in place, and stop Fetchshelf before copying the database as a backup, so the copy is complete. The
[`[backup]`](configuration.md#backup) section's backups don't need that: they are complete copies taken while
Fetchshelf runs, without `-wal` or `-shm` files.

**Never set `database_wal = true` when the database is on a network share. The write-ahead log depends on
memory shared through the `data.db-shm` file, which doesn't work across a network, and it WILL lead to
corruption of the database.**

See [`database_wal`](configuration.md#top-level-settings) in the configuration reference.

## Where the backups go

The [`[backup]`](configuration.md#backup) section's `folder` can't be the database's own folder, so a backup is
never mistaken for `data.db` when the folder is copied or restored. A subfolder such as `backups` works, but a
folder on another disk or mount is better: a backup on the same disk is lost with the database when that disk
fails.
