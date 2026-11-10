# Downloads: a network hiccup must not drop a selection — implementation plan

## Context

`DownloadRunner.FetchTorrentFileAsync` (`src/Fetchshelf.Data.AutoDownloader/DownloadRunner.cs`) fetches a
selection's `.torrent` file. Except for `MamAccountBlockedException`, **any** exception counts as a stale download
link: it looks the torrent up again by MAM id, retries once with the link from the lookup, and if that fails too,
or the lookup finds nothing, removes the selection from the queue and records "Download link is stale and could
not be refreshed." on the Errors page (`RemoveStaleSelectionAsync`). **Retry download** there puts it back; nothing
brings it back on its own.

What the bare `catch` actually catches:

| Failure | What it should mean | What happens now |
|---|---|---|
| Connection refused or reset, DNS failure, timeout (`HttpRequestException`, `TaskCanceledException` from the 30 s client timeout) | Try again later | Counted as stale; two in a row drop the selection |
| HTTP 5xx | Try again later | Same |
| HTTP 429 (`MamRateLimitedException`) | Stop the run, keep the queue | The lookup that follows is also rate limited and throws, which ends the run; the selection survives by luck |
| HTTP 403 (`MamForbiddenException`) | Stop the run, keep the queue | Counted as stale; the refresh then usually fails the same way |
| The run being cancelled (shutdown) | Stop | Counted as stale |
| MAM no longer has the torrent (the lookup by id finds nothing) | Remove, and say so | "Download link is stale" |
| MAM answers, but not with a torrent file | Stale link: refresh it | Not caught here at all: the bytes go on to `TorrentFileInfoHash.Compute`, whose `FormatException` the run's outer `catch` turns into "stays queued for the next run" — so a link that really is stale is retried forever and never refreshed |

The last row is the case the refresh logic was written for: a download hash goes stale after a while, and MAM then
answers with something that isn't a torrent file (bencode) at all.

`MamClient` itself never retries a failed request, so one dropped connection fails a whole autograb, snatchlist,
Hardcover or Link run as well.

## Change

### 1. Classify what came back

`FetchTorrentFileAsync` becomes explicit about the cases above:

- **Account-wide stops** — `MamAccountBlockedException`, `MamRateLimitedException`, `MamForbiddenException` — and
  cancellation are rethrown untouched. The run loop already stops on the first three and leaves the rest of the
  queue; cancellation ends the run.
- **Transient** — `HttpRequestException` (no response, or a 5xx status), and a `TaskCanceledException` that is the
  client's timeout rather than the run's cancellation — is rethrown as well. The run loop's existing
  `catch (Exception)` logs "Could not download X: … It stays queued for the next run" and moves on, which is the
  right outcome. The message names the cause ("MAM did not answer in time", "the connection failed").
- **Not a torrent file** — the response is a success, but `TorrentFileInfoHash` can't read it, or MAM answers with
  another 4xx status — is a stale link, and only this case refreshes it:
  - Look the torrent up by MAM id. A transient failure of the lookup is rethrown as above.
  - The lookup finds nothing: MAM no longer has the torrent. The selection is removed with the error
    "MAM no longer has this torrent (it may have been deleted or merged)." Not restorable: there is nothing to
    retry. Logged at Warning.
  - The lookup returns the same link as before: removed as today ("Download link is stale and could not be
    refreshed."), restorable.
  - A new link: stored on the selection and fetched once. Not a torrent file again: removed as today. Transient
    failure: stays queued with the new link.

The bencode check moves into the fetch: a helper `TorrentFileInfoHash.TryCompute(bytes, out hash)` (returns false
instead of throwing), so the hash computed there is the one used for the duplicate check, and the later
`TorrentFileInfoHash.Compute` call goes.

The second fetch, with the wedge, goes through the same classification. A transient failure there leaves the
selection queued; MAM may already have spent the wedge if the request reached it, which is the same risk as today
and is stated in the log line ("a wedge may have been spent").

### 2. One retry for connection-level failures in `MamClient`

`MamClient.SendAsync` retries a request **once**, after 2 seconds, when:

- it got no response (`HttpRequestException` with no status code), or
- it timed out (`TaskCanceledException` while the caller's token is not cancelled), or
- MAM answered 502, 503 or 504.

Not retried: 429, 403, 406 and every other status, which already have their own handling; and **torrent downloads
with a wedge** (`&fl`), since a request that reached MAM may have spent the wedge before the connection dropped.
`SendAsync` gets a `retryTransient` parameter, `true` by default and `false` for wedge downloads. The retry goes
through the same rate-limit and politeness rules as any request, and the delay uses the client's `TimeProvider` and
delay hook so tests don't wait. Each retry is logged at Information level: `MAM request failed (connection reset),
trying again in 2 s`.

One retry is a deliberate limit: it covers a dropped connection or a restarting proxy without letting a run hang
when MAM is really down.

### 3. Errors page text

The Download job's error rows say which case it was. The existing "Download link is stale and could not be
refreshed." stays for that case; the new "MAM no longer has this torrent" row has no **Retry download** button.
`ErroredTorrentViewModel.CanRestore` already keys off the stored restore payload, so the new row is recorded without
one.

## Tests

`tests/Fetchshelf.Clients.Mam.Tests`:

- A request whose first attempt throws `HttpRequestException`, times out, or gets 503 is sent again and succeeds; a
  second failure is thrown.
- 429, 403, 406 and 404 are not retried.
- A wedge download is not retried.
- The run's own cancellation is not retried.

`tests/Fetchshelf.Data.AutoDownloader.Tests/DownloadRunnerTests.cs`:

- A connection failure on both attempts leaves the selection queued (not removed, no error row), and the run goes
  on to the next one.
- A 200 response that isn't a torrent file refreshes the link and starts the torrent with the new one.
- The same, but the refreshed link isn't a torrent file either: removed, restorable error row (as today).
- The lookup finds nothing: removed with "MAM no longer has this torrent", not restorable.
- The lookup returns the same link: removed as stale.
- 403 during the fetch stops the run and leaves every selection queued.
- The existing stale-link tests are adjusted where they relied on any exception counting as stale.

## Documentation

`docs/user-guide.md`, the Errors page section: a download whose link went stale is taken out of the queue and can
be retried; one MAM no longer has is taken out with that reason; a connection problem leaves the download queued
for the next run. The troubleshooting section's MAM entry: Fetchshelf tries a failed connection once more before
giving up on a run.

## Release notes

Under **Fixed**: two connection failures in a row while fetching a torrent file took the selection out of the
queue as a "stale link"; a link that really was stale was retried forever instead of refreshed; a torrent MAM no
longer has was reported as a stale link. Under **Changed**: MAM requests that fail at the connection level are
tried once more. Under **Upgrading**: nothing to do; selections dropped by the old behaviour are still on the Errors
page with **Retry download**. Written at release time.

## Out of scope

- Retrying qBittorrent, Audiobookshelf and Hardcover requests.
- Telling apart, from MAM's answer alone, *why* a link is stale.
