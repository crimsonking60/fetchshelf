# Bonus points: spend them on upload credit before they are wasted — implementation plan

## Context

MAM accounts earn bonus points while seeding, up to a cap; points above it are lost. Upload credit bought with
points is what raises the ratio budget, which since the formula fix (PR #32) is often 0 for accounts that don't
seed a lot, so `cost = "ratio"` downloads wait. "The ratio budget is 0 and 80,000 points are available" is a
situation Fetchshelf can see (`MamAccountState.BonusPoints` is read already and shown on the Account page) and
could resolve.

Buying is a write action on the account, through an endpoint Fetchshelf doesn't use today. The plan is therefore in
two stages, and the second is only built if the first says it may be.

## Stage 1: check first (no code)

1. **The endpoint.** MAM has had a JSON purchase endpoint (`/json/bonusBuy.php`, with a spend type and an amount);
   for wedges it is no longer offered (wedges are bought by the `fl` flag on the download, which Fetchshelf uses).
   Find out from MAM's current API documentation (the forum's API thread, linked from the preferences page where
   `mam_id` is made) which purchases it still offers: upload credit, VIP; their parameters and amounts; and what the
   answer looks like on success, on too few points and when refused.
2. **The rules.** Whether automating purchases with a `mam_id` session is allowed. If it isn't clearly allowed, the
   plan stops here and only the reminder in "Out of scope" is built.
3. **The cap.** What the point cap is today, so the default threshold can sit below it.

The answers go into this plan before stage 2 starts.

## Stage 2: the Bonus job

### Settings

Optional `[bonus]` table; nothing is bought without it.

| Setting | Default | Description |
|---|---|---|
| `search_interval` | `720` | Minutes between runs, `60` to 43200. |
| `buy_upload_above` | (required) | Buy upload credit only while points are above this. |
| `keep_points` | `0` | Never take points below this. |
| `upload_step` | the smallest amount MAM sells | How much upload credit one purchase buys. |
| `max_per_run` | `1` | Purchases one run makes at most. |
| `only_when_ratio_budget_below` | unset | A size: buy only while the ratio budget is below it. Unset: buy whenever points are above `buy_upload_above`. |
| `dry_run` | `false` | Decide and log, buy nothing. |

`keep_points` must be below `buy_upload_above`. VIP purchases are added only if stage 1 finds them offered, as
`buy_vip_below_days`, needing the VIP expiry in the account answer.

### Deciding (Core, pure)

`BonusPlanner.Plan(points, ratioBudgetBytes, settings, price)` → the purchases to make, each a spend type and amount,
or none with the reason ("points 41,250 are below buy_upload_above 50,000"). Never more than `max_per_run`, never
below `keep_points`.

### Buying

- `MamClient.BuyAsync(spendType, amount)`, under `MamAccessGate` as `Bonus`, through the client's normal request path
  (403/406/429 handling, no retry: a purchase is not repeated on a lost answer).
- After a purchase the account state is read again, and the run checks the points went down by the price; if not,
  it stops and says so.
- Each purchase is an event (`EventKind.BonusPurchase`, amount and points before and after) and is logged at
  Information. A dry run logs what it would buy.
- Notification `bonus_spent` (opt-in) with what was bought.

### Shown

- Account page: points, the threshold, what the next run would do, and the last purchase.
- Events page: the purchases.

## Tests (stage 2)

- `BonusPlannerTests`: thresholds, `keep_points`, `max_per_run`, the ratio-budget condition, the reasons.
- `MamClientTests`: the purchase request and the parsed answers from stage 1's recorded examples; no retry.
- `BonusRunnerTests`: gate taken; dry run buys nothing; a purchase writes the event and re-reads the account; points
  that didn't go down stop the run.
- Config tests for ranges and the `keep_points` < `buy_upload_above` rule.

## Documentation

- `docs/configuration.md`: `[bonus]`, with a warning that it spends points without asking.
- `docs/user-guide.md`: what the job does and when it is worth turning on.

## Release notes (stage 2)

- Added: the optional Bonus job buys upload credit with bonus points above a configured threshold.
- Upgrading: nothing to do; nothing is bought without a `[bonus]` table.

## Out of scope

- Buying wedges with points.
- A reminder without buying ("80,000 points could buy N GB of upload") on the Account page: small, and worth doing
  even if stage 1 says no to automatic purchases.
