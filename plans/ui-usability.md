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
- **Paging.** `_Pager.cshtml` is a left-aligned sentence ("Showing 25 of 7412. Page 1 of 297.") followed by text links
  that come and go (First/Previous only after page 1, Next/Last only before the end), so the links move around between
  pages. No page numbers, so reaching page 40 means typing `PageNumber=40` in the address bar. The Search page's pager
  (`_SearchPagerPartial.cshtml`) has only Previous and Next.
- **Sorting.** Only Torrents' Title column sorts, as an ordinary underlined link with no sign of the current order or
  direction. Search sorts through a select. Series, Selected, Events, Errors and the rest cannot be sorted at all.
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

### Page navigation

One pager component for every paged list (`_Pager.cshtml`, `_SearchPagerPartial.cshtml`), in the style used by most
well-made list UIs: centered, arrows at the ends, numbered pages in between.

```
        Showing 26–50 of 7,412                                    Per page [25 ▾]

                     ‹ Prev   1  …  4  5 [6] 7  8  …  297   Next ›
```

- **Centered** under the table, with the count line and the page-size select on one row above or beside it
  (count left, size right, the arrows centered below on narrow widths). The same pager also goes above long tables, in a
  compact form (arrows and "26–50 of 7,412" only), since a table of 100 rows is long.
- **Arrows always present.** `‹` and `›` (with the words "Prev"/"Next" at 52 rem and wider) are shown on every page and
  render as disabled (dimmed, `aria-disabled="true"`, no link) at the ends, so nothing shifts. First and Last are not
  separate links: page 1 and the last page are always among the numbers.
- **Page numbers.** The first and last page, the current page and two on each side of it, with "…" where pages are
  left out; at most 9 items, so the width is constant. The current page is a filled chip in the accent color with
  `aria-current="page"`; the others are bordered squares of at least 2.5 rem, so they are touch targets on an iPad.
  Fewer than 8 pages are all shown. The "…" can be a small number field ("go to page") later; not in the first version.
- **Keyboard and touch.** The links are ordinary links in a `<nav aria-label>`, so Tab and Enter work; `[` and `]`
  (or `←`/`→` while the table has focus) are an optional later addition together with the shortcuts in section 4.
- **Search page.** MAM reports the number of results found, so the same numbered pager is used there; the page size
  is MAM's, and only the page count and the current page are needed. Snatched-before results left out of a page do not
  change the page numbers (the count line still says "N left out").
- **After htmx swaps** (Torrents' bulk actions, Check qBittorrent) the pager is part of the swapped list, as today, so
  it keeps the filters, sort and page; the page-size select keeps working without a form, as now.
- **Implementation.** `PagerViewModel` gets `Items()`, returning the sequence of numbers and gaps (for example
  `[1, Gap, 4, 5, 6, 7, 8, Gap, 297]`) so the view only loops. This is pure logic, unit tested for: one page, 7 pages,
  the first, middle and last page of 297, and a page number past the end (clamped by `Paging.Window` as now).
  `.pager` styles in `site.css`; no JavaScript.

### Sortable columns

Today a sortable header looks like any link: underlined, with no sign of whether the list is sorted by it. Sortable
columns become a consistent pattern with a visible state.

```
 Title ▲        Authors        Series        Added ▾        Size ⇅
 ^ sorted ascending                         ^ sorted descending   ^ sortable, not in use
```

- **No underlines in headers.** `thead th a` gets `text-decoration: none` and inherits the header color; the whole
  header cell is the click target (the link is stretched with padding, or `th` handles the click through the link's
  `::after { inset: 0 }`), at least 2.25 rem high.
- **Arrows.** A sorted column shows `▲` (ascending) or `▼` (descending) in the accent color, with the header text in full
  text color. A sortable column that is not in use shows a dim `⇅` that turns brighter on hover/focus, so it is visible
  which headers can be clicked at all. Arrows are drawn with CSS (`::after` on `a.sort`, an inline SVG mask, or plain
  characters) so they follow the theme tokens.
- **Behaviour.** A click sorts ascending; clicking the sorted column again flips it; a third click does not remove the
  sort (the list always has an order). Text columns start ascending, numbers and dates descending (newest and largest
  first). The sort and its direction are in the route values, as Torrents' title sort is now, so they survive paging,
  filters and bulk actions; changing the sort goes back to page 1.
- **Accessibility.** The sorted `th` has `aria-sort="ascending"` or `"descending"`; the arrow is `aria-hidden`.
- **Which columns.**

| Page | Sortable columns |
|---|---|
| Torrents | Title, Authors (first author), Series, Format, Status, and added date |
| Search | the Sort select stays for MAM's orders (MAM sorts, not Fetchshelf); the select is replaced by arrows on the Title, Size and Added headers only where MAM supports that order, otherwise the select is kept |
| Series | Series, In the library, Missing, Following |
| Selected | Title, Cost, Created |
| Events | Occurred at, Type |
| Errors | Job, Last failed |
| Jobs | none (the groups are the order) |
| Lists, Duplicates, Library check | none at first |

  A page that cannot sort by a column in the database (Torrents is paged in the database) only offers columns with an
  index or a cheap `ORDER BY`; sorting by a joined name (author, series) needs checking against the 10k-torrent
  scale plan before it is offered.
- **Implementation.** A tag helper `<th sort-by="Title" sort="@Model.Query">Title</th>` (in `TagHelpers/`) renders the
  link, arrow and `aria-sort` from the current sort, so every page uses the same markup. The sort parsing already
  existing on Torrents' query (`ToggleTitleSort`) becomes a small shared type (`SortState`: column, direction;
  `Toggle(column)`) with unit tests for the toggle rules above.

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
   cookies already do). Gives back 100–130 px at the top of every page. (Small) **Built:** `fs-seen-pages` cookie
   (a list of page paths), `_PageExplanation` renders the full text on the first visit and a `<details>` afterwards.
2. **Relative times with exact on hover.** `local-time.js` already runs; give it a short mode ("3 h ago",
   "yesterday 21:14") for tables, the full time in `title`. (Small) **Built:** `<local-time short>`, used on Events,
   Errors, Following and the Jobs page's last-run time; refreshed every minute.
3. **Sticky table headers** inside the scrolling page (`thead th { position: sticky; top: var(--header-height) }`), so the
   column names stay while reading 100 rows. (Small) **Built:** `thead th` sticky; `nav.js` sets
   `--header-height` from the header's real height.
4. **A status line instead of the Jobs-page strip**: in the footer or header, show "Download running…" when any job
   runs (from the same registry the Jobs page polls, polled every 5 s, only while a job runs). Makes "Run now" feedback
   visible from every page. (Medium) **Built:** a pill in the menu bar (`run-status.js`, polling
   `/jobs?handler=Running` every 5 s while the page is visible), hidden when nothing runs.
5. **Empty states say what to do.** "No torrents found." becomes "No torrents match these filters. Clear filters" or, when
   there are none at all, a pointer to the Link job and the Search page. Same for Selected, Events, Errors. (Small)
   **Built** for Torrents, Selected, Events, Errors and Series.
6. **Keyboard**: `/` focuses the search box on Search, Torrents and Series; `g` then `t`/`j`/`s` jumps to
   Torrents/Jobs/Selected. Shown in a "?" popover in the footer. Desktop-only benefit but cheap. (Small) **Built:** `keys.js`; the list is a `<details>` in the footer (`g` then
   `t`, `j`, `s`, `e` or `h`).
7. **Consistent button hierarchy.** One primary button per page region; destructive actions (Remove selected, Unlink)
   separated from the rest of the bulk bar by a gap and confirmation dialogs instead of `confirm()`. Today
   `button.primary` is applied by selector for GET forms only, so primary actions differ between pages. (Medium)
8. **Pagination and sorting** are covered under section 2.
9. **Feedback in place.** After "Run now", the row's button turns into "Running…" with the spinner already in the CSS;
   after bulk actions the sticky bar shows progress, linking to the bulk operation. (Small) **Built:** the button reads
   "Starting..." at once (the list already shows Running and Cancel when the answer comes); bulk progress is left.
10. **Contrast and focus checks on all themes** for the new elements (badges on the menu button, sticky bar), using the
    existing token rules (`--text-muted` meets AA on `--surface-2`). (Part of each step)

Not proposed: a sidebar (it costs about 14 rem of the width that the tables need), a card layout for tables (loses the
scan-down-a-column reading that this app is for), a framework or build step.

## 5. Themes

The four themes keep their names and characters: **Dark Warm** (brown-black with goldenrod, the default), **Dark Cool**
(navy with steel blue), **Light Warm** (parchment with bronze) and **Light Cool** (pale steel with blue). Only values
change, and only where measured below.

### Evaluation

Measured with WCAG 2.x contrast ratios on the values in `themes.css`, for every pair the CSS uses (2026-11-19).

**Sound, no change needed**
- Body and muted text: at least 12.0:1 and 5.1:1 respectively on every surface, zebra and hover row in all four
  themes (the rule in the file header, "AA 4.5:1 on all", holds).
- Links: 4.8:1 to 10.6:1 on all surfaces; links are also underlined, so they never rely on color alone.
- Field outlines (`--border-strong`): 3.3:1 to 4.9:1 against bg, surface and field (the 3:1 non-text rule holds).
- Focus ring: 4.3:1 to 14.1:1 against bg and surface in all themes.
- Status badges: the badge text on its own tint is 4.5:1 to 7.0:1 in all themes (the tint is opaque, mixed with
  `--surface`, so row stripes and hover do not lower it). Light Cool's `--c-info` on its tint is exactly 4.5:1: the
  minimum, with no room to lighten it.
- Each status is also written as text ("Linked", "Selected"), not only colored, and the row "spine" is a second cue.
- The four feel distinct and consistent: the status colors keep the same roles in every theme.

**Problems found**
1. **Light Warm's accent is too pale for anything but a filled button.** `--accent` #b8860b is 2.6:1 on the page
   background, 3.1:1 on panels and 2.3:1 on the header bar, under the 3:1 needed for non-text indicators. It is used for
   the current-page underline in the nav (on the header bar: 2.3:1, hard to see), chart lines, stat bars and the page
   explanation's left border, and, from this plan, for sort arrows and the current page chip. Light Cool is fine
   (4.1:1 on the header bar). The dark themes are fine (6.0:1 and up).
2. **The red count badge uses a hard-coded `#fff`** (`.nav-badge`). On Dark Warm's and Dark Cool's light-salmon
   `--c-danger` that is 2.6:1 and 2.5:1, so the error count on the menu is hard to read in both dark themes. The light
   themes are fine (6.5:1). The menu in this plan puts such badges on more buttons, so it has to be right.
3. **Row stripes are nearly invisible in the dark themes.** Zebra against surface is 1.05:1 (Dark Warm) and 1.06:1
   (Dark Cool); Light Warm has 1.10:1. Together with 1.3:1 hairlines (`--border`), a 25–100 row table has little to guide
   the eye along a wide row, which is exactly the problem of wide tables on a tablet. Hover (1.2–1.3:1) is visible.
4. **Panels barely separate from the page.** Panel against page is 1.09:1 (Dark Warm) to 1.19:1 (Light Warm). Panels
   (filters, notices, cards) are found by their borders alone.
5. **Light Warm's warning and link colors are the same brown** (`--c-warn` #9a5200, `--link` #7d5300: 1.15:1 apart,
   similar hue). Links are underlined and warnings are badges, so nothing breaks, but the "warn orange" role the file
   header promises reads as "brown" on a parchment page. In Dark Warm, warn orange (#f0a24a) and link gold (#efbf55) are
   1.23:1 apart, for the same reason.
6. **Hard-coded colors outside the tokens:** `#fff` on the badge (above), `#000` for shadows, and `#ff0000` for the
   development banner. The shadows are intended and the banner is intentionally theme-independent; only the badge is a bug.
7. **The theme is chosen at the very bottom of every page,** and the default is always Dark Warm, whatever the
   operating system prefers. Someone on a light tablet in daylight gets the dark theme first, and has to scroll to the
   footer to change it.
8. **Nothing guards the rules.** The contrast claims in the `themes.css` header are checked by hand. A new token or a
   tweak can break one without a failing test.

### Changes

| # | Change | Result |
|---|---|---|
| 1 | New token `--accent-line`, used wherever the accent is a line, bar, arrow or border rather than a fill: nav underline, sort arrows, stat bars, sparkline, explanation border, current-page chip outline. In the dark themes and Light Cool it equals `--accent`. Light Warm: `#8f6600`, a deeper bronze (4.2:1 on the page, 5.0:1 on panels, 3.7:1 on the header bar). The filled-button `--accent` stays goldenrod `#b8860b` with its dark `--on-accent` text (5.3:1), so Light Warm still looks like Light Warm. | Passes 3:1 for indicators everywhere |
| 2 | New token `--on-danger`: dark `#1a1510` / `#0f1620` on the dark themes (7.0:1 and 7.3:1), white on the light themes (6.5:1). `.nav-badge` uses it. | Count badges readable in all four |
| 3 | Zebra a step stronger in the dark themes: Dark Warm `--zebra` `#2d241a` (1.09:1), Dark Cool `#1f2c3d`; Light Cool one step stronger (`#ecf2f8`, was 1.06:1 and under the test's 1.06 floor after rounding); Light Warm is unchanged. `--row-hover` stays. Text on the new zebra stays above 10:1. | Visible stripes without turning the table into stripes |
| 4 | Panel against page: Dark Warm `--bg` a touch darker (`#17120d`), so `--surface` reaches about 1.12:1; the others are unchanged. No shadows are added. | Panels are seen without relying on the border |
| 5 | Warn colors moved away from the link hue, same roles: Dark Warm `--c-warn` `#f28a3a` (6.7:1 on panels; 1.45:1 from the link instead of 1.23:1); Light Warm `--c-warn` `#aa3f00` (darkened one step from `#b34200`, which was 4.4:1 on its badge tint; the test needs 4.5:1). | Warning is orange-red, not brown or gold |
| 6 | The theme choice moves out of the footer: an icon button at the header's right edge (next to Config) opens the four names in a small menu, in the same `<details>` pattern as the nav menus. The footer select goes. | One tap from every page |
| 7 | A fifth choice, "Match system", is the default for new visitors: Dark Warm when the system is dark, Light Warm when it is light (the warm pair keeps the app's character). It sets no cookie until a theme is picked, and is done with a CSS `prefers-color-scheme` rule while `data-theme` is left unset, so there is no flash. The four names stay as they are; an existing cookie wins. | Light tablets start light |
| 8 | A test in `Fetchshelf.Web.Tests` parses `themes.css` and asserts, for every theme: text and muted text 4.5:1 on bg, surface, surface-2, zebra and hover; link 4.5:1 on the same; `--accent-line` 3:1 on bg, surface and surface-2; `--border-strong` 3:1 on bg, surface and field; `--focus` 3:1 on bg and surface; each `--c-*` 4.5:1 on its own 14% tint; `--on-accent` on `--accent` 4.5:1; `--on-danger` on `--c-danger` 4.5:1. The values above are the ones to land on, and the test is the way to adjust them. | The header comment becomes a checked fact |

### What is not changed

- The names, the base hue families of each palette (bg, surface, text, accent), the default theme, and the status roles
  (info blue, ok green, warn orange, danger red, VIP rose).
- No further palette, no high-contrast theme, no color picker. "Match system" is the only addition.
- No cue is made by color alone: the sort arrows, pager chips and menu badges carry shapes or text.

### Checks

Screenshots of Torrents, Jobs and Search in all four themes at 820 px, before and after, compared side by side; the
contrast test above; a look at the Account charts and the Statistics bars in Light Warm, where `--accent-line` changes
their color.

## 6. Order of work

One branch and PR per step, as usual; each step is shippable alone.

| Step | Content | Notes |
|---|---|---|
| 1 | Table base rules (section 2 rules 1–6) and Torrents column merge, with the `SortState` type and the sort tag helper on Torrents | biggest visible win; screenshot at 820 and 2560 px before/after |
| 2 | Grouped menu: `NavModel`, markup, `nav.js`, badges, sticky header | built (PR: grouped menu). The ratio on the Account entry is left out for now: it needs the account snapshot on every page |
| 2b | Theme menu in the header and "Match system" (section 5, changes 6 and 7) | built (stacked PR); the footer selector is gone, and `AppVersionTests` now pins the menu in the header |
| 3 | Jobs page groups, job cell layout, next-run text | built (PR: Jobs page groups). Left out: the collapse of a long autograb group (the 2-second refresh would reopen it) and the short local time |
| 4 | Search results, Selected, Series, Events, Errors columns; filter disclosure and chips | built (stacked PR). Search: authors under the title, quality under the media type (5 columns); Selected: grabber and dates under the title; Series: authors under the name, missing under the library range; Torrents: filters folded with chips. Left for later: the Select/Wedge button pair as one button with a toggle, and filter chips on Search, Series, Events and Errors |
| 5 | Sticky bulk-action bar on Torrents/Selected/Errors | built (stacked PR), also on Duplicates; the Library check page keeps its buttons where they are (it has two lists, each with its own) |
| 5b | New pager on every paged list (Torrents, Series, Events, Following, Lists, Jobs history, Search) | built (stacked PR): centered arrows and numbered pages on every list, including Search. The compact pager above long tables is left for later; First and Last are the numbers 1 and the last page |
| 5c | Sortable headers on the pages in the sort table | built (stacked PR) for Series (name, books, following), Events (time, type), Errors (job, last failed) and Selected (title, cost); the sort is kept by paging links and filter forms. Left out: authors and series on Torrents (they need the metadata the list reads only when searching), Search (MAM sorts) and the Lists, Duplicates and Library check pages |
| 6 | Items 1–3, 5, 8, 9 of section 4 | small, can be split further |
| 7 | Items 4, 6, 7 of section 4 | optional |
| 0 | Themes (section 5): the contrast test, `--accent-line`, `--on-danger`, zebra, warn colors | first, since later steps use the new tokens; the theme menu and "Match system" (changes 6 and 7) come with step 2, as they sit in the header |

Each step is checked in the browser at 820 × 1180 (iPad portrait), 1280 and 2560 px, in one warm and one cool theme,
with a configuration that has everything and one that has nothing optional (menu and Jobs groups).
`docs/user-guide.md` gets the new menu groups and the Jobs page groups in the pages table. No screenshots in the guide.

## Open questions

- Is "Insight" the right name for Events + Statistics, or should they sit under Library, leaving the menu with one group?
  With one group the bar is shorter still, at the cost of a longer menu.
- Should Errors stay on the bar with a badge only when non-zero, or always show a count of 0?
- Autograb collapse threshold (8) is a guess; the live instance has 3.
