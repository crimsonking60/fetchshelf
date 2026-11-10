# Path prefixes: match whole folders, not leading characters — implementation plan

## Context

Several places ask "is this path inside that folder?" and answer it with a raw string prefix:

| Where | Check | Wrong answer |
|---|---|---|
| `LibraryMatcher.FindMatchingLibraryIndex` (`src/Fetchshelf.Core/Organizer/LibraryMatcher.cs`) | `savePath.StartsWith(library.DownloadDir)` | A library with `download_dir = "/data/audio"` claims torrents saved in `/data/audiobooks2`. Libraries are tried in order and the first match wins, so a library listed earlier can take another library's torrents. |
| `PathMapper.Translate` (`src/Fetchshelf.Core/Organizer/PathMapper.cs`) | `path.StartsWith(prefix)` for each `path_mapping` key | A mapping `"/downloads" = "/data/downloads"` also rewrites `/downloads2/book` to `/data/downloads2/book`, a path that probably doesn't exist. Used for `[qbittorrent.path_mapping]` and `[audiobookshelf.path_mapping]`. |
| `LibraryMismatchState.Compute` (`src/Fetchshelf.Core/Organizer/LibraryMismatchState.cs`) | `storedLibraryPath.StartsWith(recomputedLibraryDir)` | A book stored under `/library/audiobooks` counts as still inside `library_dir = "/library/audio"`, so a changed `library_dir` is reported as *new path* instead of *new library folder*. |

Four other places already do it right, each with their own copy of the same few lines:
`ConfigLoader.IsSameOrInside`, `AbsMatcher.IsPathPrefix`, `LibraryFolders.IsSameOrUnder` and the path guard in
`OpdsEndpoints` (file downloads).

## Change

1. **One helper in Core.** `Fetchshelf.Core.Organizer.PathPrefix` with:

   ```csharp
   /// Whether path is folder itself or inside it, by whole path components.
   public static bool IsSameOrUnder(string path, string folder)
   ```

   Rules, taken from `LibraryFolders.IsSameOrUnder`, the most complete of the existing copies:
   - Trailing `/` or `\` on `folder` is ignored, so `"/data/audio/"` and `"/data/audio"` behave alike.
   - `path` equal to `folder`, or starting with `folder` followed by `/` or `\`, is inside.
   - Comparison is ordinal (case-sensitive), as today. Case-insensitive comparison on Windows is left to the
     Windows plan, which then has one place to change.
   - An empty `folder` matches nothing (today `"".StartsWith` would match everything).

2. **Use it in the three wrong places.**
   - `LibraryMatcher`: `downloadDirMatches = library.DownloadDir is not null && PathPrefix.IsSameOrUnder(savePath, library.DownloadDir)`.
   - `PathMapper.Translate`: a key matches when `PathPrefix.IsSameOrUnder(path, key)`, and the longest key still
     wins. The replacement is joined at the folder boundary (`value` without its trailing slash, plus the rest of the
     path from the key's folder on), so a trailing slash on the key or the value makes no difference. The old
     `value + path[key.Length..]` would run past the end for the path `/downloads` with the key `/downloads/`, which
     whole-folder matching now accepts, and it gave `/data/downloadsx` or `//` when key and value disagreed about the
     trailing slash.
   - `LibraryMismatchState.Compute`: `!PathPrefix.IsSameOrUnder(storedLibraryPath, recomputedLibraryDir)`.

3. **Replace the four correct copies** with calls to the helper, so there is one rule. `OpdsEndpoints` keeps its
   `Path.GetFullPath` normalization before the check; only the comparison moves.

## Tests

- `tests/Fetchshelf.Core.Tests/PathPrefixTests.cs`: same folder, inside, sibling with a longer name
  (`/data/audio` vs `/data/audiobooks2`), trailing `/` and `\` on the folder, `\` as separator in the path, empty
  folder, root `/`.
- `LibraryMatcherTests`: a `download_dir` that is a string prefix but not a parent folder of the save path doesn't
  match; the next library that really contains it does.
- `PathMapperTests`: a key that is a string prefix of a sibling folder doesn't translate it; keys with and
  without a trailing slash translate the paths inside them as before; the longest of two nested keys wins.
- `LibraryMismatchState` tests: a stored path in a sibling folder with a longer name is *new library folder*.
- The existing tests of the four replaced copies keep passing unchanged.

## Documentation

`docs/configuration.md`: `download_dir` "Accept torrents whose save path is under this folder" already says the
right thing. Add "matched by whole folder names: `/data/audio` does not match `/data/audiobooks`" to `download_dir`
and to the `path_mapping` description of `[[qbittorrent]]`.

## Release notes

Under **Fixed**: `download_dir`, `path_mapping` and the library-folder mismatch check matched folders by their
leading characters, so `/data/audio` also matched `/data/audiobooks`. Under **Upgrading**: a setup that relied on
this, for example `download_dir = "/data/audio"` meant to catch `/data/audiobooks` too, needs the full folder
name. Torrents linked that way stay linked; the Torrents page flags them with a library mismatch if no library
claims them any more. Written at release time.

## Out of scope

Case-insensitive paths and drive letters (Windows plan).
