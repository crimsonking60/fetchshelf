# Magazine runs: read a magazine's numbering when a collection is split

Written 2026-11-25.

## The problem

A **magazine run** is one torrent that holds many issues of one magazine: a year of it, a decade, or its whole run.
It is a plain collection, with one series (the magazine) and one book per issue, and `CollectionSplitter` is meant to
split it. Its file names, however, number the issues the way magazines do, and the splitter reads few of these
numberings. A check of the live library's ebook-side torrents with several files, with today's splitter, found the
following kinds of names. The names below are invented, but each one has the same shape as a real file name.

| Named | Today's splitter |
|---|---|
| `Lantern - 112`, `Lantern Issue 001`, `Lantern Magazine 013`, `SRM-01 - Various Authors` | split as it should be |
| `Nightfall Stories v10n04 (Apr 1986)`, `v14n11-12 (Nov 1990)` | not a collection |
| `Nightfall Stories v78n01 1990-01`, `v034n01 (1968-01)`, `v034n04 [1968-04]`, `v035 n01 [1968-07]` | not a collection |
| `Hollow House Review No 268 December 2022`, `No 3 September-October 1981`, `No 118 Fall 2000` | not a collection |
| `Star Rangers IssueNN 2020` | not a collection |
| `Lantern Monthly 01 Jan 1950` | not a collection |
| `Lantern Adventure 1 Fall 1978` | not a collection |
| `Lantern Monthly 1967 Issue 1 (November 09)`, `1978 Issue 281-282 (December 28)` | split, but each year becomes a series of its own (`Lantern Monthly 1967 Issue`), and a double issue has no number |
| `1953, 10 (October), NSM` | split, but each year becomes a series of its own (`1953,`) |
| `Nightfall Stories 2013 03`, `2013 01-02` | split, but the series is the torrent's title (`… - 2013 Issues`), the number is the month (3), and a double issue has no series |
| `2019 01-02 (Nightfall Stories Book 136)` | not a collection |
| `Lantern 001 June 2010` | split, but every issue is numbered by its year (2010) |

Other things the check found:

- **Renamed magazines.** One run can hold the magazine under more than one name: an older and a newer title
  (`Lantern Science Fiction Magazine v14n11` and later `Lantern Science Fiction v17n01`), or a predecessor magazine at the start of
  the run (`Hollow House Quarterly No 1 May 1981` before `Hollow House Review No 84 May 1995`).
- **Repeated numbers.** For a weekly or biweekly magazine, the year and month repeat across issues, so only the issue
  number is unique. For some magazines that published 13 or 14 issues a year, the month repeats once or twice in a
  year, while `v..n..` doesn't.
- **One magazine, several torrents.** A magazine often comes in several torrents, such as one per decade or one per
  year, and they use different name styles. MAM gives the same series name for all of them.
- **Libraries that don't split collections.** Most runs sit in libraries with `split_collections` off. They are split
  only after **Split into its books** is ticked on the torrent's page, or after the bulk split action is used. The
  runs in the library that splits collections are the ones that are not taken for collections today.
- **Things that are not runs.** A single issue in several files (`Lantern#5-Cover`, `Lantern#5-Pages`)
  repeats one number. A single issue whose files are its stories has no numbering at all. Neither is a run.

## The rule

1. **A magazine issue's name.** A book's cleaned name (as `Clean` gives it today, without the trailing groups in
   brackets) is read as a magazine issue when it has one of these numberings after the magazine's name, or with no
   name at all:
   - **Issue number**: `Issue 1`, `Issue1`, `No 268`, `No. 268`, `#5`. An issue number may sit after a year
     (`1967 Issue 1`), and a date or season may follow it (`No 268 December 2022`, `001 June 2010`, `1 Fall 1978`). A
     bare number followed by a date or season counts as an issue number too.
   - **Volume and number**: `v10n04`, `v034n01`, `v035 n01`, optionally followed by a date.
   - **Date**: a year and a month as `1990-01`, `1990 01`, `1990.01`, `Jan 1950`, `January 1950`, `01 Jan 1950` (a
     month number followed by the name of the same month is one month), `1953, 10 (October)`, or a month and a year
     as `10 1927` (today's `DateIssuePattern`). A date may come before the magazine's name, which then follows after a
     comma (`1953, 10 (October), NSM`) or in the brackets that `Clean` takes off.

   **Double issues.** A double issue (`v14n11-12`, `Issue 281-282`, `2013 01-02`, `No 3 September-October 1981`,
   `Apr-May 2010`) is one book. It is numbered by its first issue and shown with both.

2. **A magazine run.** A torrent is a magazine run when at least three quarters of its books (and at least
   `MinimumNumberedBooks`) are magazine issues by rule 1, and their numbers differ. Rule 3 decides which number counts.
   An issue number that repeats in every book (`Lantern#5-…`) means one issue in several files, so the torrent is not a
   run. Runs are found before the `<series> <number>` reading and the reading orders. A collection with MAM's series
   range is read by these rules too, so that `v10n04` names in a range collection are not taken for a leading
   number.

3. **One numbering per torrent.** Every issue in a run is numbered the same way, so that the run sorts as one series.
   The first of these that gives every issue a number, with no two different issues sharing one, is used:
   1. **The issue number**: `Issue 512` is 512.
   2. **The year and month**: `1990-01` is 1990.01, as today's month-and-year names already are. A season
      counts as its first month: spring 3, summer 6, fall 9, winter 12.
   3. **The volume and number**: `v10n04` is 10.04. This is used only when no number in a volume is above 99.

   Two files of the same issue (one with the same name and number) are repeats, which `WithoutRepeats` already
   handles, so they don't count as a clash. A run in which no numbering works is still split, with the magazine as its
   series and no numbers, and in the order of its names, as the books of a series without numbers are today.

   A double issue's series entry is a range: `281-282`, `2013.01-2013.02`, `14.11-14.12`.

4. **The magazine is the series.** The series name is chosen in this order:
   1. MAM's series name, when MAM lists exactly one. This gathers a magazine's torrents into one series even when
      their name styles differ, and covers renamed magazines.
   2. Otherwise the magazine name that most issues use, spelled as most of them spell it, as `spellings` does today.
   3. Otherwise, when the names have no magazine name (`2019 01-02`), the torrent's title without what isn't a name:
      its years, *Issues*, *All Issues*, *Complete*, a count of issues, and tags in brackets.

   This replaces today's `BetterSeriesName` for runs, which takes `Nightfall Stories - 2013 Issues` as the series.
   Issues under an older name of the magazine stay in the one series, and each keeps its own name in its title.

5. **An issue's title.** The issue's title is its cleaned name, with the series name in front when the name doesn't
   start with a magazine name: `2019 01-02` becomes `Nightfall Stories 2019 01-02`. An epub's own title is not used
   for an issue, since magazine epubs often carry the title of a story or of the whole year.

6. **What doesn't change.** Torrents that split today into `<series> <number>` books (`Lantern - 112`,
   `Lantern Issue 001`, `SRM-01`) split into the same books, series and numbers. Reading orders and bundles are read
   as they are now. Only the ebook side is split. A run is placed in a series library like any other split collection.

## Steps

1. **Reading issue names, in Core.** A new `MagazineIssueName` in `Fetchshelf.Core/Organizer`: a pure parser from a
   cleaned name to the magazine's name (or none), with the issue number, year and month, and volume and number (each
   optional), whether it is a double issue, and where the name ended. Unit tests cover every shape in rule 1 with the
   invented names above, plus:
   - double issues;
   - `01 Jan 1950` read as one month;
   - seasons;
   - names that are not issues: `Lantern 1-40`, `Nightfall 683`, `04 - The Long Winter`, `Lantern#5-Cover`.
2. **Runs in `CollectionSplitter`.** Rules 2-5: detection, choosing the numbering, the series name and the titles.
   Tests in `CollectionSplitterTests`, with invented names:
   - a run for each numbering;
   - a biweekly run whose months repeat, numbered by issue;
   - a run whose months repeat once, numbered by volume;
   - a renamed magazine with MAM's series name, and without one;
   - names without a magazine name;
   - double issues, as one book with a range entry;
   - a single issue in several files, and an issue of stories, neither of them split;
   - a range collection with `vNNnNN` names.
3. **Nothing else changes.** The existing `CollectionSplitterTests` must pass unchanged. A check is run before and
   after the change against the live library's ebook-side torrents with several files, read-only from the database and
   with each torrent's linked files. Only the magazine runs may change.
4. **Docs.** `docs/user-guide.md`, *Collections*: how a magazine run is split, which numbering is chosen, and that the
   magazine is the series (MAM's name first). `RELEASES.md` *Upgrading*: in a library that splits collections, run
   Library check and relink what *Collection not split* lists. A run in a library that doesn't split is split by
   ticking **Split into its books** on its page, or with the bulk split action.

## Left out

- **Numbering across torrents.** Each torrent picks its own numbering, so one magazine can be numbered by volume in
  one torrent and by date in another. Both sort in publication order, because years are larger than volume numbers,
  but they are shown differently. Numbering a series the same way in every torrent would need the library's other
  books of that series. That is not part of this plan.
- **Annuals, specials and indexes** inside a run. These are kept in the series without a number when they have no
  numbering of their own.
