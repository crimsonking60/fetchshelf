# Notifications: tell a phone or chat when something needs attention — implementation plan

Builds on three plans that are done: `plans/done/job-runs.md` (run outcomes, kept runs), `plans/done/account-page.md`
(the snatch summary and `MamClient`'s pause state) and `plans/done/job-failures.md` (`JobFailureState`, the failing
state the registry keeps per job). The job-runs plan left out a hook that tells other code when a run has finished;
step 0 below adds it, and the job events are built on it.

## Context

Fetchshelf only reports through its web pages and its log. Nothing reaches the user when:

- MAM refuses `mam_id` (HTTP 403) or blocks the account (HTTP 406). Every MAM job then fails until `mam_id` is
  replaced, and that is only on the Jobs page.
- A job starts failing: the qBittorrent password changed, a library disk filled up, the Hardcover token expired.
- Something worth knowing happens: a book was grabbed, a book was linked into the library, MAM removed a stored
  torrent.

A self-hosted service run in the background is checked rarely, so problems sit unnoticed for days. Most such
services send notifications to ntfy, Gotify, Discord or a generic webhook. Each of these is one HTTP POST with a
different body.

## Change

### 0. The run-finished hook

`JobRegistry` records each finished run in `RunOnceAsync`: it sets `RegisteredJob.LastRun`, moves `Failing` on with
`JobFailureState.Next`, and queues the run for the store. Nothing outside the registry learns of it. The hook goes
right there, so it sees every run the same way the Jobs page does.

1. **What a listener gets.** A record in `Fetchshelf.Core/Jobs`:

   ```csharp
   public sealed record JobRunFinished(
       string JobName,
       JobRunRecord Run,              // the run that just ended
       JobRunRecord? PreviousRun,     // the job's LastRun before it; null for its first run ever
       JobFailure? FailingBefore,     // the job's Failing before this run
       JobFailure? FailingAfter);     // and after it
   ```

   With both failing states in it, a listener needs no history of its own: the job started failing when
   `FailingBefore` is null and `FailingAfter` isn't, and stopped when it is the other way round. `PreviousRun` covers
   what the failing state doesn't, such as a run finishing with errors after one that didn't.

2. **The listener interface.** `IJobRunListener` in `Fetchshelf.Core/Jobs`, with one method,
   `void RunFinished(JobRunFinished finished)`. It is synchronous and must not wait on anything: it is called on the
   job's own thread before the job counts as idle. The notifier's listener only puts the event in its queue (step 4).

3. **The registry calls the listeners.** `JobRegistry` takes `IEnumerable<IJobRunListener>? listeners` as one more
   optional constructor parameter, after `classifyFailure`. In `RunOnceAsync`, after `LastRun` and `Failing` are
   set and the run is handed to the writer, it calls each listener in turn with the run, the `LastRun` and `Failing`
   it read before setting them, and the new `Failing`.
   - **Every finished run is reported**: success, finished with errors, failure, cancelled and interrupted. Listeners
     decide what matters to them.
   - **A listener that throws is logged as a warning and skipped**; the other listeners are still called, and the
     run is recorded as it was. A listener can't fail a job.
   - **Runs restored at startup are not reported.** `LoadLastRunsAsync` sets `LastRun` and `Failing` from the store
     without calling the listeners, so a restart doesn't repeat what was sent before it. A job that was failing
     before the restart and fails again after it is still failing: no new `job_failing`.

4. **`Program.cs` passes the listeners in.** The registry's factory gets them with
   `sp.GetServices<IJobRunListener>()`, so any service registered as an `IJobRunListener` hears every run. The
   notifier registers one (step 5). `FetchshelfWebAppFixture`'s registry passes none.

5. **Nothing else changes.** `IJobRegistry` stays as it is: listeners are given at construction, not added later,
   so the set can't change while jobs run, and no locking is needed around it.

### 1. Configuration

Repeatable `[[notify]]` blocks:

```toml
[[notify]]
name = "Phone"
url = "https://ntfy.sh/fetchshelf-x7k2"
format = "ntfy"
events = ["mam_refused", "job_failing", "job_recovered", "grabbed"]

[[notify]]
name = "Home Assistant"
url = "http://homeassistant:8123/api/webhook/fetchshelf"
events = ["all"]
```

| Setting | Default | Meaning |
|---|---|---|
| `name` | `notify[i]` | A label for the log and the Config page. |
| `url` | (required) | Where the message is POSTed. Treated as a secret (topics and webhook URLs often are one): shown only as set on the Config page and in `--check-config`. |
| `url_file` | unset | A file holding the URL, instead of `url`, like `mam_id_file`. |
| `format` | `"json"` | `json`, `ntfy`, `gotify`, `discord` or `apprise`. |
| `token` | unset | Sent as `Authorization: Bearer …` (ntfy access tokens, Apprise API). Gotify's app token goes in its URL (`?token=`), as Gotify documents. |
| `events` | (required) | Which events to send, or `["all"]`. |

Validation, at startup and in `--check-config`:
- an unknown event or format;
- a `url` that isn't `http` or `https`;
- both `url` and `url_file`, or neither;
- an empty `events` list;
- a duplicate `name`.

A top-level `web_url` (unset by default), such as `"https://fetchshelf.example.net"`, is the address messages
link to. Without it, messages carry no links: the address the browser uses isn't known to the server.

### 2. Events

| Event | Sent when | Text, for example |
|---|---|---|
| `mam_refused` | MAM answers 403 or 406, once per pause (not once per job) | "MAM refused `mam_id` (HTTP 403). MAM jobs are paused until 14:32. Replace `mam_id` and restart." |
| `rate_limited` | MAM answers 429, at most once an hour | "MAM asked Fetchshelf to slow down (HTTP 429); MAM calls are paused for a minute." |
| `job_failing` | A job starts failing: `FailingBefore` is null and `FailingAfter` isn't | "Job Link failed: the connection to qBittorrent failed." |
| `job_recovered` | A job stops failing with a run that succeeded or finished with errors: `FailingBefore` is set, `FailingAfter` is null (a cancelled run also ends the failing state, but is no recovery and sends nothing) | "Job Link works again, after 6 failed runs since 08:30." |
| `job_errors` | A run finished with errors after one that didn't (`PreviousRun`) | "Link: 2 torrents could not be linked; see the Errors page." |
| `grabbed` | A torrent is sent to qBittorrent (`EventKind.Grabbed`) | "Grabbed *The Way of Kings* by Brandon Sanderson (Autograb Freeleech, freeleech)." |
| `linked` | A torrent is linked into a library (`EventKind.Linked`) | "Linked *The Way of Kings* into Audiobooks." |
| `removed_from_mam` | `mark_removed` marks a stored torrent (`EventKind.RemovedFromMam`) | "MAM no longer has *Book* by *Author*." |
| `hit_and_run` | An account read shows a higher *Not Seeding - H&R - Not Yet Satisfied* count (`snatch_summary.inactHnr`) than the last count seen, which is kept in the database | "MAM counts 1 hit-and-run (Not Seeding - H&R - Not Yet Satisfied). Seed it again to avoid a penalty." |
| `budget_spent` | The Download job leaves selections queued because the ratio budget or the selection slots are spent, after a run that left none | "3 selections wait: the ratio budget left is 0 B, less than the 1.2 GiB the first of them needs (min_ratio 2)." |

- **Only changes are sent**, not states. A job that fails every 10 minutes for a day sends `job_failing` once and
  `job_recovered` once.
- `hit_and_run` compares each account read with the last count seen, which is **kept in the database**, so a
  hit-and-run that appears while Fetchshelf is down is still sent after the restart. The jobs read the account on
  every run, so a new hit-and-run is noticed within one autograb, Hardcover list, snatchlist or Download interval.
  It uses the snatch summary from `plans/done/account-page.md`, which is built first or with it.
  - A higher count than the stored one sends the event, and the new count is stored.
  - A lower count is stored without sending anything, so a later rise is noticed.
  - The same count sends and stores nothing.
  - With no stored count yet (the first read after the upgrade, or after a `[[notify]]` block first asks for
    `hit_and_run`), the stored count counts as 0: an existing hit-and-run is sent once, then not again.
  - The count is only compared and stored while some `[[notify]]` block wants `hit_and_run`. Turning the event on
    later then reports what is there at that point, once.
  - The count is stored when the notification is queued, not when it is delivered. A message lost to a delivery
    failure isn't re-sent on every read; the Account page shows the count in any case.
- `mam_refused` and `rate_limited` come from `MamClient`, which raises `Refused` when it answers 403 (with the
  pause it starts), 406 or 429. A 403 that finds calls already paused raises nothing, which is "once per
  pause". A 406 pauses nothing and comes with every call, and a 429 may come every minute, so `Program.cs`
  tells those at most once in 30 minutes and once an hour. A 403 on a torrent download pauses nothing and is
  not reported.
- **One MAM refusal is one message, not one per job.** Every MAM job fails when MAM refuses `mam_id`. A
  `job_failing` or `job_recovered` for a failure of kind `MamRefused`, `MamBlocked` or `MamRateLimited` is marked
  as superseded by `mam_refused` or `rate_limited`, and a block that wants that event isn't sent it; a block that
  doesn't want it still is. (`Notification.SupersededBy`, checked in `Notifier.Receives`.)
- **Grabs and links within 30 seconds of each other are sent as one message**: "Grabbed 4 torrents: *A*, *B*, *C*
  and 1 more." A Hardcover list run selecting 20 books then sends one message, not 20.

### 3. Formats

| Format | Request |
|---|---|
| `json` | `{"event": "grabbed", "title": "…", "message": "…", "url": "…", "time": "…", "fields": {…}}`, with `fields` holding the event's data (MAM id, job name, counts). For Home Assistant, n8n, Node-RED and custom scripts. |
| `ntfy` | The message as the body, with `Title`, `Tags` (`warning` for failures, `books` for grabs) and `Click` (the link) headers, and `Priority: high` for `mam_refused` and `job_failing`. |
| `gotify` | `{"title", "message", "priority"}`, with priority 8 for failures and 4 otherwise, and `extras.client::notification.click.url` for the link. |
| `discord` | `{"content": null, "embeds": [{"title", "description", "url", "color"}]}`, red for failures. |
| `apprise` | `{"title", "body", "type"}` for an Apprise API server, which forwards to any of Apprise's services. |

Each format is a pure function from a notification to an HTTP request (`NotificationFormats`, Core), so tests
need no network.

### 4. Delivery

- **A `Notifier` service** (`Fetchshelf.Data/Notifications`, singleton) has one entry point, `Publish(Notification)`,
  which only puts the notification in a bounded in-memory queue (200; the oldest is dropped, with a log line,
  when full), and only when some block would receive it. A hosted `NotificationSender` (`Fetchshelf.Web`, only
  registered when a `[[notify]]` block exists) takes from the queue, merges bursts (`NotificationBatcher`: 30
  seconds of quiet, 5 minutes at most), and puts each message in the queue of every block that receives it
  (200 each, oldest dropped). Each block has a worker of its own that sends its messages in order through
  `NotificationDelivery`, so a service that is down holds up only its own messages. A message leaves its
  block's queue only when it is being sent. Publishing never waits on the network and never fails the job
  that published. When Fetchshelf stops, what is queued or held gets 5 seconds (by the injected clock) to be
  sent, the message being sent included; the log counts what is lost.
- **Requests** use their own named `HttpClient` with a 10-second timeout. A failure to connect, a 5xx or a 429
  is retried once after 10 seconds; then the message is dropped and logged as a warning ("Notify Phone: could
  not send *grabbed*: …"). A 4xx other than 429 (a refused token, a wrong address) is not retried. The log line
  never holds the URL, which is a secret. Delivery failures are not themselves notified.
- **Notifications are not stored.** One lost in a restart is lost; the Events page and the job history hold the
  same facts.
- **The one value that is stored** is the last hit-and-run count seen, in a small key-value table
  `NotificationState` (`Key`, `Value`, `UpdatedAt`; migration `AddNotificationState`) under the key
  `hit_and_run.inactHnr`. It is a table rather than a column elsewhere so later "notify when this changes" values
  have a place. Job transitions don't need it: the registry restores each job's last run and failing state from
  `JobRuns` at startup, and the hook hands both to the notifier.

### 5. Where events come from

- **`EventStore.AppendAsync`** publishes `grabbed`, `linked` and `removed_from_mam` after the event is saved, and
  only then: an event rolled back with its job never notifies. `EventStore` takes an optional `INotificationSink`
  (Core interface); tests pass none. The torrent's title and authors come from the event's torrent, read in the
  same scope.
- **`JobRegistry`**, through the run-finished hook (step 0). The notifier registers an `IJobRunListener` that turns
  each `JobRunFinished` into `job_failing`, `job_recovered` or `job_errors`, or nothing, and publishes it.
- **`MamClient`** raises `Refused(kind, at, pausedUntil)` and `AccountStateRead(previous, current)`; `Program.cs`
  connects both to the notifier (`ConnectMamNotifications`). The account read starts a `HitAndRunWatcher`
  check, which keeps its own failures.
- **`DownloadRunner`** reports whether selections were left queued for lack of budget, and why, in its run summary
  (`DownloadRunSummary` gains `LeftQueued` and `LimitedBy`, shown in the job's result and as the count
  `left_queued`). `BudgetSpentWatcher` sends `budget_spent` when a run leaves some queued after one that left
  none. How many the last run left is kept in `NotificationStates` (`budget_spent.left_queued`), and only a run
  that got as far as the budget changes it, so neither a restart nor a failed or cancelled run in between sends
  it again. `LimitedBy` describes each limit as it stood when the first selection was held back by it.
- **`HitAndRunWatcher`** gets each account read through `Enqueue`, which only hands it over; a hosted
  `HitAndRunCheck` does the comparing. The MAM event handlers run on the thread of the MAM call, so each only
  queues.

### 6. Trying it

The **Config** page lists each `[[notify]]` block (name, format, events; the URL as set) with **Send a test
message**, which sends "Fetchshelf test message from *host*" to that block straight away and shows the HTTP
status or error.

## Tests

- The run-finished hook, with a recording listener:
  - A run is reported with its job name, the run, the previous run, and the failing state before and after it.
  - A job's first run ever has no previous run.
  - A failure after a success gives `FailingBefore` null and `FailingAfter` set; a success after it, the other way
    round.
  - Cancelled and interrupted runs are reported too.
  - A listener that throws is logged; the next listener is still called, and the run's `LastRun` and stored record
    are unaffected.
  - `LoadLastRunsAsync` reports nothing; the first run after it gets the restored run and failing state as its
    "before".
  - A registry without listeners runs as before.
- Config:
  - A full block loads, with its defaults.
  - Each validation rule rejects with a message naming the block.
  - `url_file` is read, and the URL is masked in `--check-config` output.
- `NotificationFormats`, for each format and event: the request's method, URL, headers and body.
  - Snapshot-style tests on the JSON bodies.
  - The ntfy headers and priorities.
- Transition rules as a pure function over a `JobRunFinished`:
  - failing after success → `job_failing`;
  - failing after failing → nothing;
  - success after failure → `job_recovered` with the count and since when;
  - finished with errors after failure → `job_recovered`;
  - cancelled after failure → nothing;
  - errors appearing → `job_errors`, errors again → nothing;
  - a job failing before a restart that fails again after it → nothing, since the restored state says it was
    already failing.
- Grouping: four `grabbed` within 30 seconds give one message; one 40 seconds later is its own.
- `NotificationSender` with a fake HTTP handler:
  - Only blocks wanting the event get it.
  - A 500 is retried once.
  - A timeout is logged and dropped.
  - A full queue drops the oldest.
- `EventStore` with a recording sink: a saved event is published, and an append whose save fails publishes
  nothing.
- `MamClient`: a 403 raises `PauseStarted` once while the pause lasts.
- `hit_and_run`, against SQLite:
  - stored 0, read 1 → sent, 1 stored;
  - stored 1, read 1 → nothing;
  - stored 2, read 1 → nothing sent, 1 stored, then a read of 2 sends;
  - nothing stored, read 1 → sent once, and a restart with the stored 1 sends nothing;
  - nothing stored, read 0 → nothing sent;
  - no block wanting `hit_and_run` → nothing compared or stored.
- Migration test: `AddNotificationState` on a database from before it.
- Web test: **Send a test message** posts to the block's URL and shows the answer.

## Documentation

- `docs/configuration.md`: a `[[notify]]` section with the table, the formats, the events, the five targets each
  with an example block, and `web_url`.
- `docs/user-guide.md`: a short "Getting notified" section (what is sent, the test button, what happens when
  delivery fails).
- `docs/config-examples/5-everything.toml`: a `[[notify]]` block.

## Release notes

**Added:** notifications. **Upgrading:** a database migration adds a small table for the last hit-and-run count; no
going back. Written at release time.

## Out of scope

- Email: no SMTP client is wanted for one feature. ntfy, Gotify and Apprise all forward to email.
- Per-event formatting templates. The texts are fixed; `json` serves anyone who wants their own.
- Quiet hours and digests ("one message a day with everything grabbed").
- Leaving out Search page downloads from `grabbed`: they are grabs like any other.
