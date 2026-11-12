# Make more of a freeleech window — implementation plan

## Context

Now and then everything on MAM is free for a while: a site-wide freeleech event, or the account's own personal
freeleech period (bought with bonus points or given as a reward). During such a window, every download costs no
ratio, so an autograb with `cost = "free"` could reasonably take more than usual: more search pages (`max_pages`)
and more waiting selections (`max_active_downloads`). The unsatisfied-snatch budget still applies; every download
becomes an unsatisfied snatch whatever it costs.

Fetchshelf doesn't know when a window is on:

- The account answer (`jsonLoad.php`, see `tests/Shared/Mam/MamAccountFixture.cs`, made from a real answer of
  2026-11-11) has no freeleech field.
- Search results carry per-torrent flags: `free` (freeleech, site-wide or per torrent) and `personalFree`
  (`MamTorrentRecord.IsFree`, `IsPersonalFreeleech`). During a site-wide event every result is `free`; during a
  personal window every result is `personalFree`.

## Stage 1: check first (no code)

- Whether MAM's API exposes the windows directly: the personal freeleech's end time, and site-wide events (a field
  in `jsonLoad.php` with other parameters, or another endpoint), from MAM's API documentation.
- If not, how reliable the search flags are: during the next window, record whether *every* result of an ordinary
  search (newest first, all categories) carries the flag.

Stage 2 uses a direct field if there is one; otherwise the detection below.

## Stage 2

### 1. Knowing a window is on

`FreeleechWindow` (Core, pure): from the last account read (if stage 1 finds a field) or from the last full search
page of any job (at least 50 results, every one `personalFree`, or every one `free`). The state is held by
`MamClient` next to the last account state, with when it was last seen, and expires 30 minutes after it was last
confirmed: a window is never assumed from old data.

### 2. Settings

On `[[autograb]]` with `cost = "free"` only:

| Setting | Default | Description |
|---|---|---|
| `freeleech_max_pages` | unset | `max_pages` while a freeleech window is on. |
| `freeleech_max_active_downloads` | unset | `max_active_downloads` while a freeleech window is on. |

Unset means no change during a window. A block with another cost refuses them at load.

### 3. Using them

`AutograbberRunner` reads the window at the start of a run and uses the two values instead of the normal ones; the
run's log line and summary say "freeleech window on: up to 10 pages". The Account page shows the window and until
when it was last confirmed.

A notification `freeleech_window` (opt-in) when a window is first seen.

## Tests

- `FreeleechWindowTests`: all-free pages, a short page, mixed flags, expiry.
- `AutograbberRunnerTests`: the larger page count and cap while a window is on, the normal ones otherwise.
- Config tests: the settings only with `cost = "free"`.

## Documentation

- `docs/configuration.md`, `[[autograb]]`: the two settings, and how a window is recognised.
- `docs/user-guide.md`, Account page: the window line.

## Release notes (stage 2)

- Added: during a site-wide or personal freeleech window, free autograbs can search more pages and keep more
  downloads waiting (`freeleech_max_pages`, `freeleech_max_active_downloads`).
- Upgrading: nothing to do.

## Out of scope

- Changing `unsat_buffer` during a window: the unsatisfied-snatch limit applies whatever a download costs.
- Buying a personal freeleech with bonus points (see `plans/bonus-points.md`).
