# Link and tag-rule details: hardlink fallback, the lookup backlog, and a tag-rule preview — implementation plan

## Context

Three smaller weaknesses around linking and tag rules.

- **`hardlink_or_copy` copies on any error.** `FileLinker.TryOrFallback` catches every `IOException` from the
  hardlink, and `CreateHardLink` turns every errno into one. The fallback is meant for "this file system can't
  hardlink here" — different devices (`EXDEV`), or a file system without hardlinks (`EPERM`, `EOPNOTSUPP`/`ENOTSUP`,
  `EMLINK` at the link limit). On a full disk (`ENOSPC`), a missing source (`ENOENT`), a permission problem
  (`EACCES`) or an existing destination (`EEXIST`) the copy fails too, or worse, half-succeeds, and the Link error
  then names the copy's problem, not the real one. `hardlink_or_symlink` has the same fallback.
- **The Link job's backlog is only in the log.** `LinkRunner` looks up at most `MaxLookupsPerRun = 200` torrents on
  MAM per run, and waits a day before asking again about one MAM didn't know (`NotOnMamRecentlyAsync`). Importing a
  large existing qBittorrent shows "Nothing new to link" or a few linked per run on the Jobs page, while hundreds wait;
  the counts (`_lookupsDeferred`, `_waitingForRetry`) are logged and then dropped.
  `LinkRunSummary(int Linked, int Failed)` doesn't carry them.
- **A tag rule is applied to existing torrents without a preview.** The Config page's **Apply** on a `[[tag]]` rule
  (`RetroactiveTagApplier.ApplyAsync`) sets the category and tags in qBittorrent on every matching torrent at once.
  A rule written too broadly re-categorizes the library, and since the category decides the library, that can move
  books on the next Link run. Autograbs have a dry run; this has none.

(The fourth point of the review's list, a partial copy taken for a finished file, was fixed with the link
collisions work, PR #38: copies go through a temporary name.)

## Change

### 1. Fall back only when hardlinking is impossible

- `CreateHardLink` throws `HardLinkNotPossibleException : IOException` for `EXDEV` (18), `EPERM` (1), `EMLINK` (31)
  and `EOPNOTSUPP` (95); other errnos stay a plain `IOException` with the errno and its name (`ENOSPC`, …) in the
  message.
- `TryOrFallback` catches only `HardLinkNotPossibleException`. Any other failure is a Link error with the real cause.
- The errno numbers are Linux's; they live in one table with names, used for the message as well.

### 2. The backlog in the run's result

- `LinkRunSummary(int Linked, int Failed, int WaitingForLookup, int WaitingForMam)`: torrents left for the next runs
  because of the per-run limit, and those MAM didn't know, waiting a day.
- `JobOutcomes.Link` adds them to the line ("3 torrents linked; 450 more to look up in the next runs; 12 MAM didn't
  know, asked again daily") and to the counts (`waiting_for_lookup`, `waiting_for_mam`), so the Jobs page and its
  history show them.
- A run that only has a backlog isn't "Nothing new to link".

### 3. A preview before applying a tag rule

- `RetroactiveTagApplier.PreviewAsync(rule)`: the same matching as `ApplyAsync`, without touching qBittorrent,
  returning each matching torrent with its current category and tags (from the owning client) and what they would
  become. Torrents whose category and tags already are what the rule sets are counted, not listed.
- The Config page's **Apply** becomes **Preview**, which shows the list (title, current → new category, tags added)
  and an **Apply to these N torrents** button that runs `ApplyAsync`. Long lists show the first 200 and the count.
- A rule that would change the category of a linked book says so on its row: "category decides the library; the book
  may be flagged *Different library*".

## Tests

- `FileLinkerTests`: a hardlink failing with `EXDEV` copies; with `ENOSPC` (an injected `hardLink` that throws the
  plain exception) does not copy and reports the errno name. The existing injection point (`hardLink` delegate)
  makes this a unit test.
- `LinkRunnerTests`: more torrents than the limit leaves the rest in `WaitingForLookup`; a torrent MAM didn't know
  counts in `WaitingForMam`.
- `JobOutcomesTests`: the line and counts with and without a backlog.
- `RetroactiveTagApplierTests`: preview lists matches with current and new values and changes nothing in
  qBittorrent; apply after preview changes the same torrents.
- `ConfigPageTests`: Preview then Apply.

## Documentation

- `docs/configuration.md`, `[[library]]` `method`: when `hardlink_or_copy` copies and when it reports an error.
- `docs/user-guide.md`, Jobs page: what the Link job's backlog numbers mean; Config page: Preview before Apply.

## Release notes

- Changed: `hardlink_or_copy` and `hardlink_or_symlink` fall back only when a hardlink isn't possible (another disk,
  a file system without hardlinks). A full disk or a permission problem is now reported as itself.
- Added: the Link job's result says how many torrents are still waiting to be looked up on MAM.
- Changed: applying a `[[tag]]` rule to existing torrents from the Config page first shows what it would change.
- Upgrading: nothing to do.

## Out of scope

- Raising `MaxLookupsPerRun` or making it a setting: the limit protects MAM; the change is to show it.
