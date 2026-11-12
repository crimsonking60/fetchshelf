# Safe folder names, a length cap and a folder template — implementation plan

## Context

`FolderNameComputer.Compute` builds `Author/Series/Series #n - Title, Edition {Narrator}` under `library_dir`, and
`Sanitize` removes only `Path.GetInvalidFileNameChars()`, which on Linux is `/` and NUL. So:

- `:`, `?`, `*`, `"`, `<`, `>`, `|`, `\` and trailing dots or spaces stay. A library on an SMB share or an
  exFAT/NTFS disk, or one synced to Windows or Android, shows mangled names or fails to write. Titles with a colon
  (`Dune: Part One`) are common.
- No length limit. A long title with a series, an edition and a narrator can pass 255 bytes and fail with an errno
  that shows as a Link error.
- A part can become empty (an author made only of removed characters), which moves the book up a level.
- The layout is fixed and uses only the first author.

Step 3 of `plans/5-Windows-version.md` plans `FileNameRules` (`Posix`, `Windows`) for the Windows build. The same
rules are needed on Linux as an option; this plan builds them, and the Windows plan then only picks the Windows rules
by default.

Changing how folders are named moves existing books: the Link job compares each linked book's folder with the one
it would compute now and flags a difference as *Different folder* (`LibraryMismatchCalculator`), and **Relink**
moves it. So every change here is opt-in, and the default output stays byte-for-byte what it is today.

## Change

### 1. File name rules (Core)

`Fetchshelf.Core/Organizer/FileNameRules.cs`, pure:

- `Posix`: today's behaviour exactly (remove `/` and NUL, trim).
- `Portable`: also replace `<>:"\|?*` and control characters, strip trailing dots and spaces, and add `_` to names
  Windows reserves (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`, also with an extension). A colon
  becomes ` -` (`Dune - Part One`), the others are removed, so names stay readable.
- Both: a part that ends up empty becomes `_`.
- Both: each part is cut to at most 255 bytes of UTF-8 (`MaxComponentBytes`). Cutting never splits a character.

### 2. The cap keeps what identifies the copy

The last part is `<series prefix><title>, <edition> {<narrator>}`. When it is too long, the title is shortened and
ends in `…`; the series prefix, edition and narrator stay, since they are what tells two copies apart. If they alone
pass the limit, the whole part is cut at the end. Author and series parts are cut at the end.

### 3. Settings

Per `[[library]]`, so a library on a NAS can be portable while another isn't:

| Setting | Default | Description |
|---|---|---|
| `folder_names` | `"posix"` | `"posix"` keeps today's names. `"portable"` makes names safe on Windows, SMB shares, exFAT and NTFS. |
| `folder_template` | unset | The folder layout under `library_dir`, see below. Unset keeps today's layout. |

The length cap applies to both rule sets, always: a name over 255 bytes fails to write today, so capping it changes
nothing that worked.

### 4. The template

Tokens: `{author}` (first), `{authors}` (all, joined with `, `), `{series}`, `{series_number}` (the entries, as
today: `1, 2`), `{title}`, `{edition}`, `{narrator}` (first), `{language}`, `{year}` (from the upload time, the only
date the metadata has). `/` separates folders, and `{{` and `}}` are literal braces. A part whose tokens are all empty is left out, along with the
separator text next to them, the way today's layout leaves out the series:

```toml
folder_template = "{author}/{series}/{series} #{series_number} - {title}, {edition} {{narrator}}"
```

is today's layout (the narrator in literal braces), and is the default the code uses when the setting is unset. Text between tokens that becomes
dangling (`, ` before an empty `{edition}`, `#` before an empty number, `{}` around an empty `{narrator}`) is removed
by a small rule: a literal segment is dropped when the token on either side of it within the same part is empty and
the segment is made only of punctuation and spaces. The tests pin today's layout through the template exactly.

Validation at load: unknown tokens, an unclosed `{`, an empty template, a template without `{title}` (two books
would share a folder), and a leading `/` are refused.

`exclude_narrator_in_library_dir` keeps working: with it set, `{narrator}` is empty unless the narrator-less folder
already exists, as today.

### 5. Using them

`FolderNameComputer.Compute` takes the library's `FileNameRules` and parsed template (`FolderTemplate`, parsed once
at load). `LinkRunner` passes the matched library's; `SyncMismatch` the same, so a changed setting shows as
*Different folder* on the books it affects, which **Relink selected** fixes.

## Tests

- `FileNameRulesTests`: every replaced character, trailing dots and spaces, reserved names with and without an
  extension, empty parts, the 255-byte cut with multi-byte characters (no broken UTF-8).
- `FolderNameComputerTests`: with `Posix` and no template, every existing expectation unchanged; the cap shortens
  the title and keeps edition and narrator; `Portable` on `Dune: Part One`.
- `FolderTemplateTests`: today's layout through the template equals the old output for every case in the existing
  tests; empty series, edition and narrator drop their punctuation; `{authors}`; validation messages.
- `LinkRunnerTests`: a library with `folder_names = "portable"` links into the portable folder; changing the setting
  flags existing books *Different folder*.

## Documentation

- `docs/configuration.md`, `[[library]]` and "Folder naming": the two settings, the tokens, the cap, and that
  changing either flags existing books as *Different folder* until relinked.
- `docs/user-guide.md`: in the library section, when to choose `portable` (shares, Windows, Android, sync tools).

## Release notes

- Added: `folder_names = "portable"` per library makes folder names safe for Windows, SMB shares, exFAT and NTFS.
- Added: `folder_template` per library chooses the folder layout.
- Fixed: folder names longer than the file system allows are shortened instead of failing to link.
- Upgrading: nothing to do. Existing folders keep their names unless a library sets one of the new settings; then
  the books it affects are flagged *Different folder*, and **Relink selected** on the Torrents page moves them.

## Out of scope

- File names inside the folder: they keep the torrent's own names. Making them portable too is a follow-up once
  folders are.
- Choosing the rules by operating system: the Windows plan does that.
