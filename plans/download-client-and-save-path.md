# Choose the qBittorrent and the save path per block — implementation plan

## Context

`DownloadRunner.AddToQBittorrentAsync` sends every download to `qbClients[0]`, the first `[[qbittorrent]]` block,
with a category and tags and nothing else. So:

- With two qBittorrents (one per media type, or one behind a VPN for MAM and one not), downloads go to whichever is
  written first, and reordering the blocks silently moves them.
- A download's folder is whatever qBittorrent's category or default save path says. Putting audiobooks on another
  disk needs a category path set up in qBittorrent by hand.

Everything else already handles several clients: the Download job checks every client for a torrent it already
has, the Link job runs per connection with that connection's `path_mapping`, and the Clean job finds the client that
holds a torrent by its hash (`QBittorrentConnectionResolver`). A library already matches by save path
(`download_dir`) as well as category.

`[[qbittorrent]]` has no name today, so nothing can refer to one.

## Change

### 1. Naming a qBittorrent

- `name` in `[[qbittorrent]]`, optional, unique among them (ignoring case), checked at load like library names.
  Required only when some block refers to it.

### 2. Settings

`client` and `save_path`, both optional, in `[[autograb]]`, `[[hardcover_list]]`, `[[tag]]`, `[follow]` and
`[upgrade]` (and `[[file_list]]` once built):

| Setting | Description |
|---|---|
| `client` | The `name` of the `[[qbittorrent]]` that receives this block's downloads. Unset: the first one, as today. |
| `save_path` | The folder qBittorrent saves the download in, as qBittorrent sees it. Unset: qBittorrent decides, from the category or its default. |

- Resolution follows the category's: the block's own value, else the first matching `[[tag]]` rule that sets one,
  else the default. `TagRuleResolver.Resolve` returns them with the category and tags.
- At load: a `client` naming no `[[qbittorrent]]` is an error; `save_path` must be absolute.
- The Search page's download form gets a qBittorrent choice next to the category when there is more than one
  (remembered like the category).

### 3. Stored with the selection

`SelectedTorrentEntity.ClientName` and `SavePath` (migration, both nullable), written by `SelectionRunner` with the
category. The Selected page shows them when set.

### 4. Downloading

- `QBittorrentClient.AddTorrentAsync` gains `savePath`: when set it sends `savepath` and `autoTMM=false`, since
  qBittorrent ignores the save path for torrents in automatic management.
- `DownloadRunner` gets the clients by name (`IReadOnlyList<(string? Name, QBittorrentClient Client)>`) and picks the
  selection's. A selection naming a client no longer configured (the config changed after it was queued) goes to the
  first client, with a warning naming both.
- The Grabbed event's details include the client name when there is more than one.

### 5. Linking

No change: the Link job already reads each connection's torrents and maps their save paths with that connection's
`path_mapping`. The configuration reference says that a `save_path` is matched by a library's `download_dir` after
mapping, so a block with its own `save_path` needs a library that accepts that path (or its category).

## Tests

- Config tests: duplicate qBittorrent names; an unknown `client`; a relative `save_path`; precedence block → tag rule
  → default.
- `TagRuleResolverTests`: client and save path from the first matching rule that sets them.
- `SelectionRunnerTests`: the selection stores the resolved client and save path.
- `QBittorrentClientTests`: `savepath` and `autoTMM=false` are sent only with a save path.
- `DownloadRunnerTests`: a selection for the second client is added there; an unknown name goes to the first with a
  warning; the check for an existing torrent still looks in every client.
- `IndexPageTests`: the client choice shows only with two or more qBittorrents.

## Documentation

- `docs/configuration.md`: `name` in `[[qbittorrent]]`; `client` and `save_path` in each block and in `[[tag]]`, with
  the precedence; a two-client example in "How the pieces fit together".
- `docs/user-guide.md`, Search page: the qBittorrent choice.

## Release notes

- Added: `client` and `save_path` on autograbs, lists, `[follow]`, `[upgrade]` and `[[tag]]` rules choose which
  qBittorrent receives a download and where it saves it. `[[qbittorrent]]` blocks can have a `name`.
- Upgrading: nothing to do; without the new settings, downloads go to the first `[[qbittorrent]]` as before.

## Out of scope

- Download clients other than qBittorrent: `plans/other-download-clients.md`. `client` names a block of any kind
  once that exists.
- Moving a torrent that is already downloading to another client or path.
