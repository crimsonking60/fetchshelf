# Download clients other than qBittorrent: Transmission, Deluge, rTorrent — implementation plan

## Context

qBittorrent is the only download client. `QBittorrentClient` is used directly by twelve source files: the Download
job adds torrents and checks every client for one it already has; the Link job lists finished torrents, their files,
category, tags, save path and comment (where the MAM id is); the Clean job and Unlink apply `on_cleaned` /
`on_unlinked` (category and tags); `ClientStatusChecker` reads states; the Search page's presence cache lists
everything; `RetroactiveTagApplier` sets categories and tags. The operations used are:

| Operation | qBittorrent |
|---|---|
| list torrents (hash, name, save path, category, tags, progress, state, comment) | `ListTorrentsAsync`, `GetTorrentAsync`, `HashExistsAsync` |
| list a torrent's files | `ListFilesAsync` |
| add a torrent file (category, tags, stopped, save path once `plans/download-client-and-save-path.md` is done) | `AddTorrentAsync` |
| set the category; add and remove tags | `SetCategoryAsync`, `EnsureCategoryExistsAsync`, `AddTagsAsync`, `RemoveTagsAsync` |
| start, stop, delete with data | `StartAsync`, `StopAsync`, `DeleteWithDataAsync` |

Libraries pick up torrents by **category** or save path. Not every client has categories:

| Client | Category | Tags | Comment (MAM id) | API |
|---|---|---|---|---|
| Transmission | none; *labels* (a list) | labels | `comment` field | JSON-RPC over HTTP, `X-Transmission-Session-Id` handshake |
| Deluge | one *label* (Label plugin) | none | `comment` | JSON-RPC through deluge-web (`/json`), session cookie |
| rTorrent | `d.custom1` (what ruTorrent shows as label) | none built in | not exposed by default | XML-RPC, usually behind a web server path |

## Approach

One client at a time, Transmission first: of the three it is the only one that can express both a category and tags
(with labels). Deluge and rTorrent follow the same steps as separate pull requests, each
only if asked for.

### 1. The seam

- `IDownloadClient` in a new `Fetchshelf.Clients.Downloads` project (no I/O itself), with the operations above in
  client-neutral terms: `DownloadTorrent` (hash, name, save path, category, tags, progress, state as
  `DownloadState` — downloading, seeding, stopped, error, checking — and comment), `DownloadFile`.
- `Capabilities`: `HasCategories`, `HasTags`, `HasComment`. The configuration check uses them (below).
- `QBittorrentClient` implements it; its own types map to the neutral ones. Every user of `QBittorrentClient` moves to
  `IDownloadClient`, and `QBittorrentConnection` becomes `DownloadClientConnection` with a `Kind`. This is a large,
  mechanical refactor and is its own commit with no behaviour change; the tests pass with only names changed.
- `[[qbittorrent]]` stays exactly as it is in the configuration.

### 2. Transmission

- `Fetchshelf.Clients.Transmission`: `TransmissionClient : IDownloadClient` over its RPC (`torrent-get`,
  `torrent-add` with `metainfo` as base64, `torrent-set` with `labels`, `torrent-start`/`-stop`/`-remove`), handling
  the 409 session-id handshake and Basic authentication.
- Category and tags as labels: the category is a label of its own and every tag another label; Fetchshelf tells its
  category label apart by matching it against the configured categories (`[[library]]`, `[[tag]]`, the blocks), so
  a label that is a configured category is the category. Two configured categories on one torrent is reported as an
  error for that torrent by the Link job. `on_cleaned`'s category replaces the category label and keeps the others.
- Needs an RPC version with `labels` (Transmission 3.00 and later; **check first** the exact `rpc-version`), read
  with `session-get` at startup; an older one is refused with the version found.
- Configuration:

  ```toml
  [[transmission]]
  name = "transmission"
  url = "http://transmission:9091/transmission/rpc"
  username = "admin"
  password = "…"
  [transmission.path_mapping]
  "/downloads" = "/data/torrents"
  ```

  with `on_cleaned`, `on_unlinked`, `on_invalid_torrent` as in `[[qbittorrent]]`. Names share one namespace with
  `[[qbittorrent]]` names, for `client = …` in the blocks.

### 3. Deluge (later)

`DelugeClient` over deluge-web's JSON-RPC (`auth.login`, `web.update_ui`, `core.add_torrent_file`,
`label.set_torrent`). Category = the Deluge label; tags are not supported: a block or `[[tag]]` rule that sets tags
for a Deluge client is refused at load (`Capabilities.HasTags`). Needs the Label plugin enabled; checked at startup.

### 4. rTorrent (later)

`RTorrentClient` over XML-RPC (`d.multicall2`, `load.raw_start_verbose`, `d.custom1.set`). Category = `custom1`; no
tags; the MAM id isn't in a field rTorrent exposes, so torrents are looked up on MAM by info hash, as the Link job
already does for torrents without a comment.

## Tests

- Per client: request building and answer parsing from recorded examples (session handshake, login, errors), with a
  fake HTTP handler like the qBittorrent client tests.
- `DownloadClientCapabilitiesTests` and config tests: tags on a client without tags; a category label clash.
- The Link, Download, Clean and Unlink tests run once with the qBittorrent fake and once with a Transmission fake
  where they test client behaviour (a test helper picks the client).

## Documentation

- `docs/configuration.md`: `[[transmission]]` (and later `[[deluge]]`, `[[rtorrent]]`), what each client can and
  can't do (the capability table above), how labels stand in for categories.
- `docs/user-guide.md`: the supported clients.
- `README.md` and `docs/maintainer-guide.md`: the test setup for each client.

## Release notes (Transmission step)

- Added: Transmission as a download client, with `[[transmission]]` blocks. Labels stand in for qBittorrent's
  categories and tags.
- Upgrading: nothing to do; `[[qbittorrent]]` blocks work as before.

## Out of scope

- Moving torrents between clients.
- Clients without a remote API (Tixati, BiglyBT without its plugin).
