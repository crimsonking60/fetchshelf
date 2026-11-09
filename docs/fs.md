# fs(1)

## NAME

**fs** - show what a Fetchshelf installation knows, read from its database

## SYNOPSIS

```
fs [COMMON OPTIONS] COMMAND [OPTIONS] [ARGUMENTS]

fs torrents   [QUERY...] [--status S] [--problem P] [--library L] [--category C] [--media-type T]
                         [--language L] [--author TEXT] [--series TEXT]
fs torrent    ID
fs files      [QUERY...] [torrent filters] [--missing | --present]
fs events     [QUERY...] [--kind K] [--torrent ID]
fs selected   [QUERY...] [--state S] [--grabber NAME]
fs errors     [--stage S]
fs duplicates
fs lists
fs list-items [QUERY...] [--list ID] [--status S] [--all]
fs stats
fs sql        QUERY
```

## DESCRIPTION

**fs** reads a Fetchshelf database and prints what it holds: the torrents and where they stand, the files
linked into the libraries, the event log, the download queue, errors, skipped duplicates and the Hardcover
lists. It shows the same records as the web interface's pages, in a form suited to a terminal, a shell
pipeline or another script.

**fs** only reads. It opens the database read-only, so it is safe to run while Fetchshelf is running, and it
never talks to MAM, qBittorrent or Audiobookshelf. What it shows is what Fetchshelf last recorded: a torrent's
qBittorrent state, for example, is the one Fetchshelf saw on its last check.

It needs Python 3.8 or later and nothing else. It is a single file, `utils/fs`; copy it anywhere on your
`PATH`.

## FINDING THE DATABASE

The database is, in this order:

1. the file given with `--db FILE`;
2. `$FETCHSHELF_DB`;
3. `$FETCHSHELF_DATA_FILE`, the variable Fetchshelf itself reads, so **fs** finds the database on its own
   where Fetchshelf runs;
4. `data.db` in the current directory.

With the Docker setup, the database is `data.db` in the folder mounted at `/database`. The paths stored in it
are the paths inside the container; see `--path-map`.

## OUTPUT

Every listing command prints rows with columns, in one of these formats (`--format`, `-f`):

| Format | What it is |
| --- | --- |
| `table` | Aligned columns with a header, shortened to fit the terminal. Times in local time, sizes as `1.2 GB`, info-hashes shortened to 10 characters. The default when the output is a terminal. |
| `tsv` | One row per line, columns separated by tabs, no header (add one with `--header`). Times in UTC as `2026-11-09T14:03:10Z`, sizes in bytes, lists joined with `; `. Tabs and line breaks inside a value become spaces. The default when the output goes to a pipe or a file. |
| `csv` | Comma-separated with a header (leave it out with `--no-header`), quoted as needed. Values as in `tsv`, except that line breaks are kept. |
| `json` | One JSON array of objects. Times as UTC strings, sizes and ids as numbers, lists as arrays, missing values as `null`. |
| `jsonl` | One JSON object per line, as in `json`. |

`json` and `jsonl` include every column unless `--columns` says otherwise; the other formats show a short
default set. `fs COMMAND --columns help` lists a command's columns with a description of each.

Values that are like names of states use lowercase words joined by dashes, as in `metadata-only`,
`comic-book` and `removed-from-mam`. When filtering, case, dashes and underscores don't matter, so
`--media-type ComicBook` and `--media-type comic_book` both work.

## COMMON OPTIONS

These work with every command, before or after the command name.

`--db FILE`
: The database file. See [FINDING THE DATABASE](#finding-the-database).

`-f`, `--format FORMAT`
: `table`, `tsv`, `csv`, `json` or `jsonl`. See [OUTPUT](#output).

`--json`
: The same as `--format json`.

`-c`, `--columns LIST`
: The columns to show, separated by commas, in that order. `all` shows every column. `help` lists the
  command's columns and exits.

`-q`, `--ids`
: Print only each row's identifier, one per line, for feeding to another command. The identifier is the
  info-hash for `torrents` and `events`, the path for `files`, the MAM id for `selected` and `duplicates`, the
  key for `errors`, the list id for `lists` and the book id for `list-items`.

`-n`, `--count`
: Print only how many rows match.

`-s`, `--sort COLUMN`
: Sort by this column instead of the command's default order. Rows without a value come last.

`-r`, `--reverse`
: Reverse the order.

`-l`, `--limit N`
: Show at most `N` rows, after sorting.

`--since WHEN`, `--until WHEN`
: Only rows from `WHEN` on, or from before `WHEN`. `WHEN` is a date (`2026-11-01`), a date and time
  (`2026-11-01T18:00`), both in local time unless they carry an offset, or a time ago: `30m`, `12h`, `7d`,
  `2w`. Which time a command compares is listed under [COMMANDS](#commands).

`--header`
: Print a header line in `tsv` output.

`--no-header`
: Leave out the header line in `table` and `csv` output.

`-0`, `--null`
: End each `tsv` or `--ids` line with a NUL character instead of a newline, for `xargs -0` and paths with
  unusual characters.

`-w`, `--wide`
: Don't shorten table columns to fit the terminal.

`--full-ids`
: Show whole info-hashes in tables.

`--path-map FROM=TO`
: Show paths that start with `FROM` as starting with `TO` instead. Repeat it for several mappings; the
  longest matching `FROM` wins. Use it when the database comes from a container and **fs** runs outside it,
  so the paths it prints, and the ones `files --missing` checks, are the ones on the host:
  `--path-map /media=/srv/media`.

`-h`, `--help`
: Show the options of **fs** or of a command.

## COMMANDS

### torrents

Lists the torrents Fetchshelf has a record of, the rows of the **Torrents** page. Sorted by title. Aliases:
`t`. `--since` and `--until` compare the time the torrent was first recorded.

`QUERY`
: Text that must appear in the title, an author, a narrator, a series or the info-hash. Several words are
  matched as one phrase.

`--status STATUS`
: Where the torrent stands with the library, as the **Status** filter on the **Torrents** page:

  | Status | Meaning |
  | --- | --- |
  | `linked` | Its files are in a library. |
  | `waiting` | Not linked yet: still downloading, or no library accepts it. |
  | `replaced` | Retired by the Clean job for a better copy. |
  | `unlinked` | Taken out of the library with **Unlink**. |
  | `metadata-only` | A book recorded with `metadata_only_add`, never downloaded. |
  | `not-linked` | Any of the above except `linked`. |

`--problem PROBLEM`
: `mismatch` (the folder no longer matches the metadata or configuration), `not-in-client` (qBittorrent
  didn't have it on Fetchshelf's last check), or `any`.

`--library NAME`, `--category NAME`, `--media-type TYPE`, `--language LANGUAGE`
: Only torrents in this library, with this qBittorrent category, of this media type (`audiobook`, `ebook`,
  `manga`, `comic-book`, `musicology`, `radio`, `periodical-ebook`, `periodical-audiobook`), or in this
  language.

`--author TEXT`, `--series TEXT`
: Only torrents with `TEXT` in an author's name or a series name.

Each filter option can be repeated, or given several values separated by commas, to allow any of them:
`--status replaced,unlinked`. Different options must all match.

Columns: `id`, `mam_id`, `title`, `authors`, `narrators`, `series`, `media_type`, `language`, `status`,
`library`, `category`, `client_status`, `mismatch`, `format`, `file_types`, `size`, `linked_size`, `files`,
`added`, `unlinked`, `replaced`, `replaced_by`, `abs_id`, `path`, `url`. The default set is `id`, `mam_id`,
`title`, `authors`, `status`, `library` and `added`.

### torrent ID

Shows everything about one torrent: its fields, the files linked into the library, its events, its errors,
and any torrents skipped as duplicates of it. Alias: `show`.

`ID` is an info-hash, the start of one (at least 4 characters, as shown in tables), `mam:` and a MAM id for a
metadata-only record, or a MAM id. When it matches more than one torrent, **fs** lists them and exits with
status 2.

In `table` format the output is laid out for reading. In `tsv` format it is `name<TAB>value` lines, with one
`file`, `event` or `error` line for each of those. In `json` format it is one object with every torrent
column plus `description`, `linked_files`, `events`, `errors`, `duplicates_skipped` and `metadata`, the full
metadata as Fetchshelf stores it.

### files

Lists the files Fetchshelf linked into the libraries, one full path per line. It takes the same filters as
`torrents` and only looks at linked torrents unless `--status` says otherwise. Sorted by path.

`--missing`
: Only files that are no longer on disk.

`--present`
: Only files that are on disk.

Columns: `path`, `size`, `exists`, `id`, `mam_id`, `title`, `library`. Only `path` is shown by default.
`exists` is checked only with `--missing`, `--present`, or when the column is asked for. The files are those
Fetchshelf linked itself; `cover.jpg`, `metadata.json` and `metadata.opf` aren't included.

### events

Shows the event log, the rows of the **Events** page, newest first. Alias: `log`. `--since` and `--until`
compare the time of the event.

`QUERY`
: Text in the torrent's title, the event's details or the info-hash.

`--kind KIND`
: `grabbed`, `linked`, `cleaned`, `updated`, `removed-from-mam` or `unlinked`.

`--torrent ID`
: Only this torrent's events. `ID` is as for `torrent`; a MAM id also finds events of a torrent whose record
  was removed.

Columns: `event_id`, `time`, `kind`, `id`, `mam_id`, `title`, `grabber`, `cost`, `wedge`, `library`, `path`,
`files_removed`, `changes`, `detail`. `detail` sums up the kind's own fields on one line. `title` is empty
when Fetchshelf no longer has a record of the torrent. The default set is `time`, `kind`, `mam_id`, `title`
and `detail`.

### selected

Shows the download queue, the rows of the **Selected** page, oldest first. Alias: `queue`. `--since` and
`--until` compare the time the torrent was selected.

`QUERY`
: Text in the title or an author's name.

`--state STATE`
: `queued` (waiting for the Download job), `downloading` (added to qBittorrent, not finished), `removed`
  (gone from the queue: finished, removed by hand, or replaced by a better pick), or `all`. The default is
  `queued,downloading`, what the **Selected** page shows.

`--grabber NAME`
: Only entries selected by this autograb, list or `Manual search`.

Columns: `selected_id`, `mam_id`, `title`, `authors`, `media_type`, `state`, `cost`, `grabber`, `category`,
`tags`, `size`, `added`, `started`, `removed`, `url`. The download link is never shown.

### errors

Shows the errors on the **Errors** page, newest first. `--since` and `--until` compare the time of the error.

`--stage STAGE`
: `grabber`, `linker` or `cleaner`.

Columns: `error_id`, `stage`, `key`, `title`, `error`, `time`. `key` is the MAM id for the grabber and the
info-hash for the linker and cleaner.

### duplicates

Shows the torrents skipped because the library already had an equal or better copy, the rows of the
**Duplicates** page, newest first. Alias: `dupes`. `--since` and `--until` compare the time it was skipped.

Columns: `duplicate_id`, `mam_id`, `title`, `authors`, `media_type`, `size`, `duplicate_of`,
`duplicate_of_title`, `added`, `url`.

### lists

Shows the Hardcover lists Fetchshelf has read, with how many books each has. Sorted by name.

Columns: `list_id`, `name`, `type`, `books`, `done`, `removed`, `added`, `updated`. `books` counts the books
on the list now, `done` those marked done on the **Lists** page, `removed` those no longer on the list.

### list-items

Shows the books on the lists, in list order, with what was found for each format. Alias: `books`. Books
marked done or no longer on the list are left out unless `--all` is given. `--since` and `--until` compare
the time the book first appeared on the list.

`QUERY`
: Text in the title, an author's name or the series.

`--list ID`
: Only this list, by its id or the end of it: `--list want_to_read`.

`--status STATUS`
: Books where either format has this status: `selected` (a torrent was selected), `wanted` (found, but it
  costs more than the list allows), `not-wanted` (found, but the list prefers the other format), `existing`
  (the library has it), `not-found`, or `none` (not looked for yet).

`--all`
: Include books marked done and books no longer on the list.

Columns: `list_id`, `position`, `external_id`, `title`, `authors`, `series`, `prefer`, `audio_status`,
`audio_mam_id`, `ebook_status`, `ebook_mam_id`, `searched`, `added`, `done`, `removed`, `url`. A format the
list doesn't allow has an empty status.

### stats

Sums up the installation: the database file, its size and schema version, torrents by status, media type
and library, linked files and their size, problems, the queue, errors, duplicates, events and lists. Alias:
`summary`.

In `table` and `tsv` format it prints `name value` pairs, with dotted names for the breakdowns
(`by_status.linked`); in `json` format one object with nested objects for the breakdowns. Always exits with
status 0.

### sql QUERY

Runs `QUERY` against the database and prints the result in the chosen format, in the order the query
returns it unless `--sort` is given. The database is read-only, so statements that change it fail. Quote the
query, or the shell splits it.

The tables are `Torrents`, `Events`, `SelectedTorrents`, `ErroredTorrents`, `DuplicateTorrents`, `Lists`
and `ListItems`. Times are stored as text such as `2026-11-09 14:03:10.1234567+00:00`, and most details
about a book are in the `MetadataJson` column, which SQLite's JSON functions can read:
`json_extract(MetadataJson, '$.Authors[0]')`. The schema can change between releases; the other commands
are the stable interface.

## EXIT STATUS

| Status | Meaning |
| --- | --- |
| 0 | Something was found or printed. `--count` and `stats` always exit with 0. |
| 1 | Nothing matched: an empty listing, or no torrent for `torrent ID`. |
| 2 | An error: a bad option or value, an ambiguous `ID`, a missing or unreadable database, or a failing `sql` query. The message goes to standard error. |

As with `grep`, status 1 isn't an error. In a script with `set -e`, write `fs ... || true` where an empty
result is fine.

## ENVIRONMENT

`FETCHSHELF_DB`
: The database file, when `--db` isn't given.

`FETCHSHELF_DATA_FILE`
: The database file Fetchshelf itself uses, read when neither `--db` nor `FETCHSHELF_DB` is set.

## EXAMPLES

The torrents taken out of the library with **Unlink**:

```sh
FETCHSHELF_DB=/srv/fetchshelf/database/data.db fs torrents --status unlinked
```

Everything about one torrent, by MAM id or the start of its info-hash:

```sh
fs torrent 1019554
fs torrent 0c18cc6982
```

Library files that have gone missing, for a database from a container that mounts `/srv/media` as `/media`:

```sh
fs files --missing --path-map /media=/srv/media
```

The total size of the files linked into the `audiobooks` library:

```sh
fs files --library audiobooks -c size | paste -sd+ | bc
```

Torrents qBittorrent no longer has, with their titles, as CSV for a spreadsheet:

```sh
fs torrents --problem not-in-client -c mam_id,title,authors,library -f csv > gone.csv
```

What happened in the last day:

```sh
fs events --since 1d
```

Open the MAM page of every book still queued:

```sh
fs selected --state queued -c url | xargs -n1 xdg-open
```

The ten largest audiobooks:

```sh
fs torrents --media-type audiobook -s size -r -l 10 -c size,title,authors
```

Authors with the most linked books, with `jq`:

```sh
fs torrents --status linked -f jsonl | jq -r '.authors[]' | sort | uniq -c | sort -rn | head
```

Warn when there are errors, for a cron job:

```sh
n=$(fs errors --count)
[ "$n" -eq 0 ] || echo "Fetchshelf has $n errors"
```

Books on the Want to Read shelf that MAM didn't have:

```sh
fs list-items --list want_to_read --status not-found
```

Torrents per library, with SQL:

```sh
fs sql "SELECT LinkerName AS library, COUNT(*) AS torrents FROM Torrents GROUP BY 1 ORDER BY 2 DESC"
```

## NOTES

The `id` column in tables is shortened to 10 characters, enough to tell torrents apart and accepted by
`fs torrent` and `--torrent`. Use `--full-ids`, another format, or `--ids` for whole info-hashes.

Status, mismatch and qBittorrent state are what Fetchshelf recorded on its last Link run or client check. To
bring them up to date, run the Link job, or press **Check qBittorrent** on the **Torrents** page, before
running **fs**.

## SEE ALSO

The [user guide](user-guide.md) describes the pages whose records **fs** shows. The
[configuration reference](configuration.md) describes libraries, categories, lists and `metadata_only_add`.
