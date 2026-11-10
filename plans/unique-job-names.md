# Job and library names: reject duplicates and clashes — implementation plan

## Context

A block's `name` becomes its job name (`AutoDownloaderJobNames`, now in `src/Fetchshelf.Web/Jobs/JobNames.cs`):
the `name` when set, otherwise `autograb[i]`, `snatchlist[i]` or `hardcover_list[i]`. `JobRegistry.Register` stores
jobs in a dictionary and overwrites an existing entry (`_jobs[name] = …`), and nothing checks names when the
configuration is loaded. So:

- **Two blocks with the same name** — two `[[autograb]]`s, or an autograb and a Hardcover list — become one job.
  The block registered last replaces the other, which never runs, while both blocks' schedulers trigger the
  survivor. Nothing says so.
- **A block named like a built-in job** (`Link`, `Clean`, `Download`, `Audiobookshelf`) replaces that job. An
  autograb called `Download` means queued torrents are never sent to qBittorrent.
- **A name like a generated one** (`name = "autograb[1]"` on the first block, while the second has no name) clashes
  the same way.
- **The name is also the selection's `GrabberName`**, which `max_active_downloads` and the per-block
  `unsat_buffer`/`wedge_buffer` look up (`AutograbberRunner`, `SafetyBuffers`). Two blocks sharing a name share
  those counts and buffers; `SafetyBuffers` even has a rule for it ("the largest of their buffers applies").
  The Search page's own downloads use the grabber name `Manual search`, so an autograb with that name applies its
  buffers to manual downloads.
- **`[[library]]` names are not checked either.** `[opds] libraries` and the Torrents and Events page filters
  refer to libraries by name (`LinkerName`); two libraries with one name can't be told apart there.

## Change

1. **Job names move to Data.** `JobNames` and `AutoDownloaderJobNames` move from `Fetchshelf.Web/Jobs/JobNames.cs`
   to `Fetchshelf.Data/Configuration/JobNames.cs` and become public, so the loader can compute the same names the
   host registers. `Program.cs`, the Config page, the Search page and `ConfigSummary` use them from there. The
   grabber name `Manual search` becomes a constant next to them (`JobNames.ManualSearchGrabber`), used by the
   Search page instead of the literal.

2. **`ConfigValueValidator.ValidateNames`**, run from `ConfigLoader.Load` with the other value checks:
   - Compute every block's job name in registration order: `[[autograb]]`, then `[[snatchlist]]`, then
     `[[hardcover_list]]`.
   - Reject a name used twice, naming both blocks:
     `Invalid configuration: the [[autograb]] 'Bookmarks' and the [[hardcover_list]] 'Bookmarks' have the same name; every autograb, snatchlist and Hardcover list needs its own.`
   - Reject a block `name` equal to `Link`, `Clean`, `Download`, `Audiobookshelf` or `Manual search`:
     `… 'Download' is the name of a built-in job; choose another name.`
   - Reject a block `name` of the generated form `autograb[N]`, `snatchlist[N]` or `hardcover_list[N]`, whether or
     not it clashes today: adding or removing an unnamed block would make it clash later.
   - Reject two `[[library]]` blocks with the same `name`. Unnamed libraries stay allowed.
   - Names are compared ignoring case. Two blocks called `Bookmarks` and `bookmarks` are technically different jobs
     today, but they look the same on the Jobs and Events pages; refusing them is simpler than explaining them.
   - Leading and trailing spaces in a name are rejected too (`' Bookmarks'`), for the same reason.

3. **`SafetyBuffers`** loses its "largest buffer of several autograbs with one name" rule: names are unique now. It
   keeps falling back to the global buffers for a grabber name no autograb has (manual downloads, renamed or
   removed blocks).

4. **`JobRegistry.Register`** throws `InvalidOperationException` when a name is registered twice. With the
   validation above this can't happen from configuration; the throw makes any future registration bug loud
   instead of silent. Covered by a Core test.

## Tests

- `tests/Fetchshelf.Data.Tests` (new `JobNameConfigTests`, or added to `ConfigValidationTests`): duplicate names
  within `[[autograb]]`, across autograb/snatchlist/Hardcover list, differing only in case, each reserved name,
  generated-looking names, leading/trailing spaces, duplicate library names; unnamed blocks and unnamed libraries
  still load; the example configurations still load.
- `tests/Fetchshelf.Core.Tests/JobRegistryTests`: registering a name twice throws.
- `SafetyBuffersTests`: the shared-name case is removed; the fallback cases stay.
- `ConfigCheckTests` and the Web registration tests keep passing with the moved names.

## Documentation

`docs/configuration.md`:
- The `name` row of the shared filter table: unique across `[[autograb]]`, `[[snatchlist]]` and
  `[[hardcover_list]]`, not one of the built-in job names (listed), and not of the form `autograb[1]`.
- `[[library]]` `name`: unique.
- The **Validation** bullet mentions names.

## Release notes

Under **Fixed**: two blocks with the same name, or a block named after a built-in job, silently replaced each other
and one never ran. Under **Upgrading**: such a configuration is now refused at startup with a message naming both
blocks; run `--check-config` first. Renaming a block means torrents it queued earlier no longer count against its
`max_active_downloads` and fall back to the global buffers, as for any renamed block. Written at release time.

## Out of scope

Names for `[[tag]]` rules, which are only labels in the log.
