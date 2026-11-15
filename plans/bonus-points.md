# Bonus points: spend them before they are wasted — implementation plan

## Context

MAM accounts earn bonus points while seeding, up to a cap; points above it are lost. Points buy upload credit
(which raises the ratio budget, often 0 for accounts that don't seed a lot, so `cost = "ratio"` downloads wait),
freeleech wedges (which the Download job spends on `wedge` costs) and VIP. Fetchshelf already reads the points
(`MamAccountState.BonusPoints`, shown on the Account page) but spends none of them.

Until now this was done outside Fetchshelf by a shell script run from cron every two hours: it read the points,
bought a wedge once a day, optionally topped up VIP, and bought upload credit in 100 and 50 GB steps while the
points stayed above a buffer of 55,000. The Bonus job replaces it.

## Stage 1: what MAM's purchase endpoint does

Taken from the script and its log of over a year of runs (about 1,500 purchases), not from new requests to MAM.

- **Endpoint:** `GET /json/bonusBuy.php/?spendtype=<type>&…&_=<milliseconds>` with the `mam_id` session, answering
  JSON.
  - Upload credit: `spendtype=upload&amount=<GB>`. Amounts 1, 5, 20, 50 and 100 all succeeded. 500 points per GB.
  - Wedge: `spendtype=wedges&source=points`. 50,000 points each. Answer:
    `{"success":true,"type":"wedge","amount":1,"method":"points","seedbonus":9421.16,"FLleft":369}`. (Wedges are also
    spent with the `fl` flag on a download, which Fetchshelf already uses; buying them with points still works.)
  - VIP: `spendtype=VIP&duration=max`, topping VIP up to the most MAM allows. Never used by the script with its
    setting off, so its answer and price are not known from the log.
- **Answers:** `success` says whether it worked; on success, `seedbonus` holds the points left. A refusal is
  `{"success":false,"error":"…"}`, with no `seedbonus`.
- **Rate limit:** `{"success":false,"error":"Rate Limit Exceeded."}` came 8 times, and about 95 purchases failed in
  runs that bought several in quick succession. Purchases are spaced out, and a refusal ends the run's purchases.
- **The cap:** 99,999 points.
- **The rules:** automated purchases with the `mam_id` session have been made by the script for over a year. The
  maintainer accepts that as allowed.

## Stage 2: the Bonus job

### Settings

Optional `[bonus]` table; nothing is bought without it.

| Setting | Default | Description |
|---|---|---|
| `search_interval` | `120` | Minutes between runs: `0` (only from the Jobs page) or `60` to 43200. |
| `keep_points` | `0` | Upload credit and VIP are only bought with points above this. A wedge may use them: keeping 55,000 keeps a wedge's worth plus 5,000. |
| `upload_amounts` | `[100, 50, 20, 5, 1]` | GB of upload credit one purchase buys, tried largest first, each bought as often as the points above `keep_points` allow. `[]` buys none. Whole numbers from 1 to 100. |
| `wedge_every` | `0` | Hours between wedge purchases; `0` buys none. A wedge needs 50,000 points. Up to 8760. |
| `vip` | `false` | Top VIP up to the most MAM allows, once a run, when the points above `keep_points` are at least 15,000. |
| `max_purchases_per_run` | `10` | Purchases one run makes at most, `1` to `100`. |
| `dry_run` | `false` | Decide and log, buy nothing. |

The plan's earlier `buy_upload_above` and `upload_step` are replaced by `keep_points` and `upload_amounts`, which
say the same more simply. `only_when_ratio_budget_below` is dropped: points above the cap are lost anyway, so
holding back upload credit only saves points `keep_points` can already keep.

Order within a run, as the script did: a wedge when one is due, then VIP, then upload credit. A wedge is due when
`wedge_every` hours (less ten minutes, so a schedule drifting by a few minutes doesn't skip a day) have passed since
the last wedge bought with points, as the events record.

### Deciding (Core, pure)

`BonusPlanner.Next(policy, state)` → the next purchase (kind, amount, price) or none with the reason ("the 41,250
points leave nothing above keep_points 55,000"). `BonusPlanner.PlanRun` repeats it, assuming each purchase goes
through at its price, for a dry run and for the Account page's "the next run would buy".

### Buying

- `MamClient.BuyWithBonusPointsAsync(purchase)`, under `MamAccessGate` as `Bonus`, through the client's normal request
  path (403/406/429 handling), with no retry: a purchase is never sent twice. Purchases are at least 10 seconds
  apart.
- The answer's `seedbonus` must be lower than the points before; if it isn't, the run stops and fails.
- A refusal ends the run's purchases: a rate limit as a note, anything else as a failure, except a refused VIP top-up,
  which is logged and the run goes on to upload credit.
- Each purchase is an event (`EventKind.BonusPurchase`: what, how much, points before and after) and is logged at
  Information. A dry run logs what it would buy.
- Notification `bonus_spent` (opt-in) for each purchase.
- The account is read again at the end of a run that bought anything, so the Account page shows the new numbers.

### Shown

- Account page: a Bonus points section with the settings, what the next run would buy, and the last purchases.
  Without `[bonus]`, what the points could buy.
- Config page: the `[bonus]` settings.
- Events page: the purchases.

## Tests (stage 2)

- `BonusPlannerTests`: `keep_points`, the order (wedge, VIP, upload), the largest amount first, wedge due times,
  `max_purchases_per_run`, VIP's reserve, the reasons, `PlanRun`.
- `MamClientBonusTests`: the request URLs, the parsed answers from the recorded examples, a refusal, no retry.
- `BonusRunnerTests`: gate taken; dry run buys nothing; a purchase writes the event; points that didn't go down stop
  the run; a rate limit ends the run's purchases; a refused VIP goes on to upload credit; the last wedge is found in
  the events.
- `BonusConfigTests`: defaults, ranges.

## Documentation

- `docs/configuration.md`: `[bonus]`, with a warning that it spends points without asking, and the `bonus_spent`
  notification event.
- `docs/user-guide.md`: what the job does and when it is worth turning on.
- `README.md`: VIP purchases on the untested list.
- `docs/config-examples/5-everything.toml`: a `[bonus]` table.

## Release notes (stage 2)

- Added: the optional Bonus job buys wedges, VIP and upload credit with bonus points.
- Upgrading: nothing to do; nothing is bought without a `[bonus]` table. A block named `Bonus` must be renamed.
