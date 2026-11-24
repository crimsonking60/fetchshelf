# Mixed-format ebook torrents: choose the format per title, not per torrent

Written 2026-11-24, from a comic compilation that linked 45 of its 878 files.

## The problem

Link takes, from each torrent, the files of its best audio format and its best ebook format, where best means earliest
in `ebook_types` (or the library's own list): `LinkedFormats.Choose` picks one extension per side, and
`LinkRunner.ChosenFiles` keeps the files with that extension (`LinkedFormats.IsChosen`). For a book that comes as
`Title.epub` and `Title.mobi` that is right: one copy of the book is linked, in the format wanted most.

For a torrent whose titles come in different formats it is wrong. A comic compilation with 45 issues as `cbz` and 830
as `cbr` gets its 45 `cbz` files linked and nothing else, as `cbz` is earlier in the list; the page says "Format cbz"
and the library folder holds 5% of the torrent. Nothing tells the user that the rest was left out.

Splitting such a torrent (`split_collections`) is no way out. `CollectionSplitter` already picks a format per book,
but it is made for one series' run (`Saga 001-054`). A reading order across many series, named
`026_Batman - Family.cbz`, splits into hundreds of one-issue series.

## The rule

The ebook side chooses per title. Audio stays as it is: audio formats of one book carry different track names, so the
same rule would link an `m4b` book and its `mp3` copy side by side.

1. **A folder of one book.** The torrent's wanted ebook files (an extension in the list) are grouped by folder. A
   folder holding at most one file of each format is one book in several formats, whatever the files are called
   (`HOME_epub.epub`, `HOME_mobi.mobi`; `Title - A.azw3`, `Title - M.mobi`): only its best format is linked.
2. **Everything else, format by format.** The rest of the files are taken in the list's order: all files of the best
   format present, then each file of the next format unless it is the same title as a file already taken, and so on.
3. **The same title.** Two file names are the same title when the words of the shorter name are all in the longer one.
   The words are those of the name without its extension, a trailing group in brackets such as `(Digital)` or
   `(2014)`, apostrophes and the minor words *a*, *an*, *the*, *and*, *of* and *ed*. A word may be the start of the
   other's word, as names get cut short (`A Lord Darc` is `A Lord Darcy`). Numbers compare by value (`01` is `1`) and
   never by their start, so issue `1` is never issue `11`. When the shorter name holds numbers, the longer one must
   hold the same numbers, no more, and the shorter one needs two other words besides: `Batman 686` alone is too
   little to say it is `686_Batman - Under the Hood (Part 4)`.

The rule was tried on every linked ebook torrent of a live library (below). It links what the torrents hold and leaves
every case of one book in several formats as it is today.

## Analysis of a live library (2026-11-24)

The live database, read through a copy, and qBittorrent's file list of each torrent: every torrent that is linked,
not split, not replaced and not unlinked, on the ebook side. Split collections are left out, as their books already
choose a format each. `ebook_types` there is `["epub", "mobi", "cbz", "cbr", "azw3", "azw", "pdf"]`, with five
libraries on the ebook side: two ebook libraries (fiction, nonfiction) and a comics library with the series layout and
`split_collections`.

### How many torrents mix formats

| | Torrents |
|---|---|
| Linked ebook-side torrents looked at | 3,262 (2 more without a file list in qBittorrent) |
| With two or more wanted ebook formats | 640 (20%) |
| ... every title in each of the formats | 596 |
| ... some titles in two formats, some in one | 21 |
| ... no title in two formats | 23 |

The 640 by the formats they hold:

| Formats | Torrents | Changed by the rule |
|---|---|---|
| epub + mobi | 371 | 0 |
| epub + mobi + azw3 | 91 | 0 |
| epub + azw3 | 59 | 0 |
| epub + pdf | 48 | 1 |
| epub + mobi + pdf | 33 | 0 |
| epub + mobi + azw3 + pdf | 15 | 0 |
| cbz + cbr + pdf | 5 | 5 |
| epub + cbz + pdf | 4 | 0 |
| epub + azw3 + pdf | 3 | 0 |
| cbz + cbr | 3 | 3 |
| cbz + azw3 + pdf | 2 | 0 |
| epub + mobi + cbz | 1 | 1 |
| epub + mobi + cbz + cbr | 1 | 1 |
| five more, one each | 5 | 0 |

By library: 530 in the fiction ebook library (2 changed), 87 in the nonfiction one (none), 23 in the comics library (9).

### What the rule changes

11 torrents get 2,576 more files linked, 65.8 GiB, and none loses a file:

| Kind of torrent | Library | Linked today | With the rule |
|---|---|---|---|
| A humour magazine's complete run, issues as cbr and pdf, one as cbz | comics | 1 | 919 |
| A comic reading-order compilation over many series, cbz and cbr | comics | 45 | 877 |
| A year of a publisher's comics in reading order, cbz and cbr | comics | 20 | 332 |
| An SF magazine's run in three torrents (1977-1985, 1986-2000, 2001-2015), by year in cbz, cbr, epub and mobi | comics | 27, 20, 19 | 94, 114, 129 |
| Another SF magazine's run in three torrents, by decade in cbz, cbr and pdf | comics | 7, 44, 41 | 120, 112, 84 |
| A 12-book series, books 1-11 as pdf only and book 12 as epub | fiction | 1 | 12 |
| A mystery magazine's 23 issues, 15 as epub and mobi and 8 as cbz only | fiction | 15 | 23 |

Today the library holds one issue of the magazine run's 919, a twentieth of the compilation, a sixteenth of the year
of comics, and one book of the 12-book series.

### Other rules that were tried

| Rule | Torrents changed | Wrongly |
|---|---|---|
| By name alone, over the whole torrent | 44 | the 33 besides the 11 below get a second copy of a book in another format, named differently (`Title - Author.mobi` beside `Title (2010) - Author - Series 17.epub`) |
| Step 1 above, and by exact name otherwise | 16 | 5 get second copies: a series whose mobi and azw3 are named `Ghostlight - Ghostlight` beside the epub's `Author - Series 01 - Ghostlight`, an omnibus, two anthologies, a magazine |
| Step 1, and a lesser format only where it has more files in its folder than the formats before it | 10 | the 8 cbz-only issues of the mystery magazine and 43 issues of the SF magazines are left out |
| The rule above, without the guard on numbers | 11 | 1 issue of the compilation is taken for another (`Batman 686` against `686_Batman - Under the Hood (Part 4)`) |
| The rule above | 11 | none found |

The file lists of every torrent changed by the last four rules were read in full; for the first rule, the names of the
files it would add.

### Consequences for these torrents

- **Nothing changes until they are relinked.** Link places a torrent once; the rule applies to the next Link of a new
  torrent and to **Refresh and relink** of an existing one. The 11 are found by step 4 below.
- **Disk space.** `link_method = "hardlink_or_copy"` with downloads and libraries on one file system: the 65.8 GiB are
  hard links and take no space. Where they had to be copied they would take it all.
- **File names in the comics library.** The series layout names a single linked file `Series #001 - Title.ext` and
  several files by their own names (`LinkRunner.PlaceInSeriesFolder`). The magazine run, linked as one renamed file
  today, gets its 919 files under their own names; Relink must take the renamed file away, not leave it beside its
  original (to be checked in step 2).
- **The torrent's format.** `ChosenEbookExtension` stays one value: the best format linked, the first. What reads it
  keeps working: the Statistics page counts the compilation as `cbz`, Upgrade and Clean judge it by `cbz` as today.
- **Books page and OPDS.** `CatalogBook.From` lists only the files of the chosen format (`FilesOf(files,
  ebookExtension)`), so the newly linked `cbr` files would not be offered. It must take every linked file of a format
  in the ebook list (step 3).
- **The other 629 mixed torrents** link exactly what they link today.

## Steps

1. **The rule, in Core.** `LinkedFormats.ChooseEbookFiles(ebookTypes, files)`: the files to link and the best format
   among them, pure, with the folder grouping and the title match above (the title match as its own small type, as
   it is the part most likely to need tuning). Unit tests from the cases above, by file names only: one book in
   epub, mobi and pdf with unrelated names; a compilation in cbz and cbr; a magazine run whose issues are in cbr and
   pdf; a series with azw3 and mobi copies named apart from the epubs; truncated names; `Hour 1` against `Hour 11`;
   `Batman 686` against `686_Batman - Under the Hood (Part 4)`; upper-case extensions (`.CBR`).
2. **Link and Relink use it.** `LinkRunner.ChooseFormats`/`ChosenFiles` take the ebook files from the rule and the
   audio files as today. A series library still links the ebook side only. Check what Relink does with a file it
   placed under another name when the new placement names files differently, and make it remove the old link.
3. **The catalog.** `CatalogBook.From` takes every linked file whose extension is in the ebook list as the book's
   ebook files, so the Books page and OPDS offer all of them. `BookCatalogReader` passes the list.
4. **Finding the torrents linked before.** A Library check finding, *Formats left out*: a linked torrent whose files,
   by the rule, include wanted files that aren't linked, with the number of files. It comes with Relink as the action,
   like the other findings, and lists the 11 in the library above. (A dry count of it is the analysis above.)
5. **Docs.** `docs/user-guide.md`, *Which files*: per title on the ebook side, one book in several formats keeps the
   best one. `docs/configuration.md` at `ebook_types`: the same in one sentence. `RELEASES.md` *Upgrading*: run Library
   check and relink what *Formats left out* lists.

Split collections (`CollectionSplitter`) are not touched: their books already choose a format each.
