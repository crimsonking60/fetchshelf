# UI usability: grouped menu, wider tables, a structured Jobs page

Written 2026-11-19. Not a redesign: the Razor Pages, htmx and the theme tokens in `themes.css` stay. The changes are
to `_Layout.cshtml`, `site.css`, a handful of table partials and the Jobs page.

## Where the UI stands

Checked from the code and from screenshots at 820 px wide (iPad portrait) against the live instance.

- **Menu.** `_Layout.cshtml` builds one flat list of up to 14 entries (Search, Jobs, Account, Statistics, Selected,
  Torrents, Series, Following, Events, Errors, Library check, Duplicates, Lists, Config). At 820 px "Duplicates" is already
  cut off, and the rest is reachable only by scrolling the bar sideways, which nobody discovers. Below 52 rem the header
  stops being sticky. Order follows the order the pages were added, not how often they are used.
- **Tables.** Torrents at 820 px: the Title column breaks mid-word ("Obelis/k Gate", "Seaso/n"), categories break as
  "fetchshel/f-audioboo/ks". Cause: `th, td { overflow-wrap: anywhere }` lets every column shrink to a few characters
  while Authors, Series, Media type, Language, Category and Client status each take a share of the width. Eleven columns
  (Torrents), nine (Selected, Search results) and eight (Series) is more than a table can carry at that width.
  The page explanation, the filters (two rows of selects) and the action buttons also push the first row of data
  below the fold on a 1180 px high screen.
- **Jobs.** One table in the order Download, Link, Clean, then everything else alphabetically (`PriorityJobOrder`).
  Autograb jobs ("BM 1 Free", "BM 2 Small ratio", ...) are mixed in with Bonus, Follow and the rest, so the list has
  no structure. The Job column wraps ("Down/load", "Clea/n") because the Description column takes the space. The
  description repeats the same sentence for every autograb job. "Next run in" shows "908m 52s", which nobody can read.
- **General.** Page explanations are 4–5 lines on every page and sit above the content on every visit. Checkboxes and
  buttons in tables are about 1 rem, too small for touch. Active filters are a row of selects, with no sign of which are
  set. Bulk-action buttons sit below a long table, out of sight until the last row is reached.

## 1. Menu

### Principles

1. The daily path is one tap: Search, Torrents, Selected, Jobs.
2. Related pages share a menu; a menu is a native `<details>`/popover, so it works on touch and with a keyboard without
   a framework.
3. What needs attention (Errors, Library check findings, Following finds) shows on the bar even when its page is inside
   a menu: the badge moves up to the menu's button.
4. The dynamic entries (Account, Following, Lists, Library check) are added to a group when configured, and a group with
   no entries is not rendered at all.

### Groups and order

```
 [▮▮▮ Fetchshelf]  Search  Torrents  Selected  Jobs  Errors⁽³⁾  Library ▾  Insight ▾ ........ Account ▾  Config
```

| Position | Entry | Shown when |
|---|---|---|
| Bar | Search | always (home) |
| Bar | Torrents | always |
| Bar | Selected | always |
| Bar | Jobs | always |
| Bar | Errors + count badge | always; the badge only when there are errors |
| Library ▾ | Series | always |
| Library ▾ | Following + badge | `[follow]` configured |
| Library ▾ | Lists | any `hardcover_list`, `file_list` or `bookmarks_list` |
| Library ▾ | Duplicates | always |
| Library ▾ | Library check + badge | the last check has findings |
| Insight ▾ | Events | always |
| Insight ▾ | Statistics | always |
| Right edge | Account | a MAM account is configured; shows ratio, e.g. "Ratio 1375" as the label |
| Right edge | Config | always |

Why this grouping: Search, Torrents, Selected and Jobs are the work loop (find, queue, watch it happen). Series,
Following, Lists, Duplicates and Library check all answer "what is in the library and what is missing". Events and
Statistics are looking back. Errors is a to-do list, not history, so it stays on the bar.
Account is moved to the right edge and doubles as the account strip, so the ratio is visible from every page and the strip
at the top of the Jobs page is not needed. Config is rare, so it goes last.

The Library and Insight buttons carry the sum of their children's badges (a dot with the number), and the entry inside the
open menu carries its own. `aria-current` on a child lights its group's button too.

### Implementation

- A single `NavModel` built in one place (a `NavItems` class in `Fetchshelf.Web`, with the conditions currently inline in
  `_Layout.cshtml`), returning groups of entries. The layout only renders it. Unit test: the list for a few settings
  combinations (nothing configured; follow only; lists only; everything).
- Markup: `<nav>` with plain links for bar entries and `<details class="nav-menu"><summary>Library</summary><div
  class="nav-panel">…</div></details>` for groups. A small `nav.js` (about 20 lines) closes an open menu on an outside
  click or Escape and closes the others when one opens. Without the script the menus still open and close.
- Width: at 820 px the bar needs about 640 px (4 entries, Errors, two menus, Account, Config), so it fits on one row on
  an iPad in portrait. Below 52 rem the same bar keeps working with the labels of the right-edge entries hidden and
  only the icon/ratio shown. Mobile stays out of scope; the bar must simply not break there.
- The header becomes sticky at every width (the `position: static` rule below 52 rem is dropped), since it is now one row
  of fixed height. `.download-feedback`'s `top: 3.25rem` follows from a `--header-height` custom property rather than a
  literal.
- Touch targets: nav links and summaries get at least 2.75 rem height.

## 2. Tables and horizontal space

### Rules for all tables

1. Remove `overflow-wrap: anywhere` as the default for `th, td`. Wrapping happens at word boundaries
   (`overflow-wrap: break-word` only on the cells that can hold paths or ids: `.break` class).
2. A column is one of three kinds, set by classes on the cell: `.col-fit` (`width: 1%; white-space: nowrap`: dates,
   sizes, status, buttons), `.col-flex` (takes the remainder, wraps at words: Title) and `.col-clip` (one line with an
   ellipsis and the full text in `title`/tooltip: category, tags, grabber, paths).
3. Merge columns that always read together instead of giving each its own header. Fewer, richer cells use the width
   better than many narrow ones.
4. Tighter cell padding on data tables (`0.35rem 0.6rem`) and a slightly smaller table font (0.8125 rem) on tables with
   seven or more columns. Row height is kept at 2.25 rem minimum for touch.
5. `main` grows on wide screens already (`--content-max-width`); keep that. Under 64 rem the page padding drops from
   1.25 to 0.75 rem so the table gets the extra 16 px.
6. Below 52 rem a table keeps scrolling sideways as today, but the first column is sticky so the title stays visible.

### Page by page

**Torrents** (10 columns → 6):

| Column | Content |
|---|---|
| ☐ | checkbox |
| Title | title, with the "(unlinked)", collection and upgrade notes as small badges below it; authors and series on a second line in muted text, each still a link (replaces the Authors and Series columns) |
| Format | media type · language as one cell, each still a filter link |
| Category | clipped with a tooltip |
| Status | client status, plus seeding note and "removed from MAM" below; a mismatch is a warning badge here instead of its own column, shown only when there is one |
| (actions) | Edit |

The bulk-action bar becomes a sticky bar at the bottom of the viewport that appears once at least one row is ticked
("3 selected: Clean, Refresh, Relink, Unlink, Remove, Set category…"), so the buttons are in reach no matter how long
the page is. "Select all" says how many it applies to (the page, not every match).

**Search results** (8 → 6): Title with authors under it; Format+Quality as one cell (media type label with the quality pill);
Status; Size; Cost; Action. Status badges stop stacking: qBittorrent and Snatched become small icons with tooltips
when a row has more than two. The two buttons (Select, Wedge select) become one button plus a small "wedge" toggle
button next to it; this halves the width of the last column.

**Selected** (9 → 6): Title, Cost+Category+Tags, Grabber, Created/Started as one two-line time cell, actions.

**Series** (8 → 6): Series with authors below it, In the library / Missing as one "7 of 9" cell with a thin progress bar,
Media types, Following, action.

**Events, Errors, Duplicates, Statistics, Library check, Lists detail**: apply rules 1–5; Events' "Occurred at" becomes
a fit column, Details takes the flex column. Errors: the Error text gets the flex column, Job and time become fit columns.

**Jobs**: see section 3.

### Filters

The Torrents filters are seven selects on two rows. They become one row: the search box plus a "Filters" disclosure
(`<details>`) containing the selects, with the active ones also shown as removable chips
(the `.name-filter` style already exists). Collapsed by default, open when any filter is set. This gives back about
110 px of vertical space on every visit and shows which filters are active. The same chip pattern is used on Search,
Series, Events and Errors.

## 3. Jobs page

Three groups, each its own section with a heading, a one-line caption and a background band so the separation is visible
even when scanning:

| Group | Jobs | Order inside |
|---|---|---|
| Standard jobs | Download, Link, Clean | fixed: Download, Link, Clean |
| Configured jobs | Audiobookshelf, Follow, Upgrade, Bonus, Backup, Seeding, Library check, and the list jobs (`hardcover_list`, `file_list`, `bookmarks_list`) | built-ins alphabetically, then lists |
| Autograb jobs | every `[[autograb]]` entry | configuration order, not alphabetical, so "BM 1, BM 2, BM 3" stay in the order they were written |

"Unconfigured" jobs (currently the muted lines at the bottom) move into the Configured group as one collapsed line,
"Not configured: Follow, Upgrade", so the page does not end in a stray paragraph.

```
 Jobs                                                    Ratio 1375 · Unsatisfied 68/200 · Wedges 335
 ┌ Standard ───────────────────────────────────────────────────────────────────────────────────────┐
 │ Job        Last run        Result                                   Next      [Run now]          │
 │ Download   16:51 (0s)      No selected torrents waiting             in 2 min  [Run now]          │
 │ Link       17:01 (55s)     Nothing new to link                      in 9 min  [Run now]          │
 │ Clean      17:02 (0s)      No superseded copies to retire           –         [Run now]          │
 └─────────────────────────────────────────────────────────────────────────────────────────────────┘
 ┌ Configured ─────────────────────────────────────────────────────────────────────────────────────┐
 │ Bonus      16:31 (0s)      Bought 50 GB of upload credit            in 1 h 26 [Run now]          │
 │ Follow     16:41 (13s)     5 series and authors searched, 6 sel.    in 23 h   [Run now][Dry run] │
 │ …                                                                                                │
 │ Not configured: Upgrade, Seeding                                                                 │
 └─────────────────────────────────────────────────────────────────────────────────────────────────┘
 ┌ Autograb (3) ───────────────────────────────────────────────────────────────────────────────────┐
 │ BM 1 Free        08:13 (0s)   3 results, 0 selected                 in 15 h   [Run now][Dry run] │
 │ BM 2 Small ratio 08:13 (1s)   3 results, 2 selected                 in 15 h   [Run now][Dry run] │
 │ BM 3 Large wedge 08:14 (0s)   3 results, 0 selected                 in 15 h   [Run now][Dry run] │
 └─────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Details:

- **Job column** is a fit column, never wraps. Description is no longer a column: it becomes the cell's second line in
  muted text (when `JobExplanations` is on), and for autograb jobs it is left out, since it is the same sentence three times.
  This alone gives the page about 200 px of horizontal space on an iPad.
- **Last run** shows the time and "(took 55s)" under it, as short local time ("today 17:01"; the full date in `title`).
- **Next run in** is formatted as "in 2 min", "in 1 h 26 min", "in 15 h"; never minutes beyond 99. "–" when the job has
  no interval.
- **Buttons**: Run now and Dry run side by side in one cell, never stacked; Findings link for the Library check stays
  after Run now. Buttons get touch size.
- **Group header** shows a summary when the group is long: "Autograb (3): 2 selected in the last runs", and a failing
  job turns its group's header text red so a failure is visible even if the group is scrolled past.
- **Autograb group** collapses to the header only when it has more than 8 jobs (`<details open>` otherwise).
- **Implementation**: `JobStatusViewModel` gets a `Group` (`Standard`, `Configured`, `Autograb`) set where the view
  models are built, from the registration, not from the name: the registry entries for autograb and list jobs are
  already created from their config blocks in `FetchshelfJobs.cs`, so they pass the group when registered. Sorting in
  `BuildJobViewModels` becomes group first, then the rules above. `_JobListPartial` renders one table per group from the
  same polled partial, so the 2-second refresh keeps working and keeps open/closed state (the `<details>` elements carry
  ids and htmx's `hx-preserve` is not needed because the partial renders `open` from server state saved in a cookie).
  Unit test for the grouping/ordering with a registry holding all three kinds.
- The account strip at the top of the page stays, until the Account menu entry shows the ratio; after that it is removed
  from here to save the line.

## 4. Other improvements

Ordered by value for cost.

1. **Page explanations fold away.** The long blue-left-border blocks are shown in full on the first visit and collapse
   to one line with an "About this page" toggle afterwards (state in a cookie per page, as the theme and page size
   cookies already do). Gives back 100–130 px at the top of every page. (Small)
2. **Relative times with exact on hover.** `local-time.js` already runs; give it a short mode ("3 h ago",
   "yesterday 21:14") for tables, the full time in `title`. (Small)
3. **Sticky table headers** inside the scrolling page (`thead th { position: sticky; top: var(--header-height) }`), so the
   column names stay while reading 100 rows. (Small)
4. **A status line instead of the Jobs-page strip**: in the footer or header, show "Download running…" when any job
   runs (from the same registry the Jobs page polls, polled every 5 s, only while a job runs). Makes "Run now" feedback
   visible from every page. (Medium)
5. **Empty states say what to do.** "No torrents found." becomes "No torrents match these filters. Clear filters" or, when
   there are none at all, a pointer to the Link job and the Search page. Same for Selected, Events, Errors. (Small)
6. **Keyboard**: `/` focuses the search box on Search, Torrents and Series; `g` then `t`/`j`/`s` jumps to
   Torrents/Jobs/Selected. Shown in a "?" popover in the footer. Desktop-only benefit but cheap. (Small)
7. **Consistent button hierarchy.** One primary button per page region; destructive actions (Remove selected, Unlink)
   separated from the rest of the bulk bar by a gap and confirmation dialogs instead of `confirm()`. Today
   `button.primary` is applied by selector for GET forms only, so primary actions differ between pages. (Medium)
8. **Pagination**: top and bottom pager with the count ("1–50 of 7,412"), page size selector kept in the cookie that
   exists. Torrents has only a bottom one. (Small)
9. **Feedback in place.** After "Run now", the row's button turns into "Running…" with the spinner already in the CSS;
   after bulk actions the sticky bar shows progress, linking to the bulk operation. (Small)
10. **Contrast and focus checks on all themes** for the new elements (badges on the menu button, sticky bar), using the
    existing token rules (`--text-muted` meets AA on `--surface-2`). (Part of each step)

Not proposed: a sidebar (it costs about 14 rem of the width that the tables need), a card layout for tables (loses the
scan-down-a-column reading that this app is for), a framework or build step.

## 5. Order of work

One branch and PR per step, as usual; each step is shippable alone.

| Step | Content | Notes |
|---|---|---|
| 1 | Table base rules (section 2 rules 1–6) and Torrents column merge | biggest visible win; screenshot at 820 and 2560 px before/after |
| 2 | Grouped menu: `NavModel`, markup, `nav.js`, badges, sticky header | unit tests for `NavModel` |
| 3 | Jobs page groups, job cell layout, next-run text | unit tests for grouping and the time text |
| 4 | Search results, Selected, Series, Events, Errors columns; filter disclosure and chips | |
| 5 | Sticky bulk-action bar on Torrents/Selected/Errors | |
| 6 | Items 1–3, 5, 8, 9 of section 4 | small, can be split further |
| 7 | Items 4, 6, 7 of section 4 | optional |

Each step is checked in the browser at 820 × 1180 (iPad portrait), 1280 and 2560 px, in one warm and one cool theme,
with a configuration that has everything and one that has nothing optional (menu and Jobs groups).
`docs/user-guide.md` gets the new menu groups and the Jobs page groups in the pages table. No screenshots in the guide.

## Open questions

- Is "Insight" the right name for Events + Statistics, or should they sit under Library, leaving the menu with one group?
  With one group the bar is shorter still, at the cost of a longer menu.
- Should Errors stay on the bar with a badge only when non-zero, or always show a count of 0?
- Autograb collapse threshold (8) is a guess; the live instance has 3.
