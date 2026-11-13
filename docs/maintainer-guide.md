# Fetchshelf maintainer guide

This guide is for maintainers: the people who review and merge pull requests and publish releases. It
assumes you have write access to <https://github.com/crimsonking60/fetchshelf> and know the contributor
workflow in the [contributors guide](contributors-guide.md).

## Contents

- [Branches](#branches)
- [Working with pull requests](#working-with-pull-requests)
  - [Triage](#triage)
  - [Reviewing](#reviewing)
  - [Merging into main](#merging-into-main)
  - [Outside contributions](#outside-contributions)
  - [Stale and declined pull requests](#stale-and-declined-pull-requests)
- [Testing the head of main](#testing-the-head-of-main)
- [Versioning](#versioning)
- [Release notes](#release-notes)
- [Releasing a new version](#releasing-a-new-version)
  - [Is main ready?](#is-main-ready)
  - [Preparing the release](#preparing-the-release)
  - [Making the release](#making-the-release)
  - [After the release](#after-the-release)
- [Hotfixes](#hotfixes)
- [When a release goes wrong](#when-a-release-goes-wrong)
- [Maintainer gh command reference](#maintainer-gh-command-reference)

## Branches

| Branch | Holds | Who changes it, and how |
| --- | --- | --- |
| `main` | Everything merged so far. The repository's default branch, and the only long-lived one. A release is a tag on it. | Pull requests from contributors and maintainers, squash-merged. Maintainers may push trivial changes directly. |
| `fix/…`, `feature/…`, `docs/…`, `chore/…`, … | One change each, short-lived: in contributors' forks, or in this repository for maintainers. | Their authors. Deleted once merged. |

Rules that keep this working:

- **Anything that touches code or behaviour goes through a pull request,** maintainers' own changes included.
- **Trivial changes may be pushed straight to `main`** by a maintainer: typos, small documentation tweaks,
  dependency bumps. Such a push uses the admin bypass of the branch rules, so the checks run after it lands,
  not before. If `main` turns red, fix it straight away with a follow-up commit. That risk is the reason to
  keep direct pushes to changes that really are trivial.
- **Pushing to `main` doesn't release anything.** It publishes the `edge` image (see
  [Testing the head of main](#testing-the-head-of-main)). A release is a `vYYYY.MM.N` tag pushed by a
  maintainer (see [Making the release](#making-the-release)).
- `Fixes #123` in a pull request closes the issue when it is merged into `main`.
- **The documentation on `main` may describe changes that are not released yet.** The README's notice and the
  notes at the top of the user guide and the configuration reference say so, and point people who run a
  released version to the "Latest release" section of the README. That section links the user guide and the
  configuration reference at the release's tag, and is updated with every release.

Repository rulesets enforce this:

| Ruleset | Applies to | Rules | Bypass |
| --- | --- | --- | --- |
| Protect main | `main` | Changes through a pull request with the `Build, test and format` check passing; squash merges only. | Repository admins |
| Never delete or rewrite main | `main` | No deletion or force-push. | Nobody |
| Protect release tags | `v*` tags | No creating, moving or deleting. | Repository admins |

The second ruleset has no bypass, so it refuses deleting or rewriting `main` even for admins, for example when
a merged pull request's head branch is deleted automatically. To delete or rewrite `main` on purpose, which
should never be needed, disable that ruleset first and enable it again straight after.

The third ruleset means release tags only come from maintainers, and a published tag can't be moved by
accident.

## Working with pull requests

### Triage

Look at new pull requests within a few days, even if a full review has to wait. For each one:

- **Label it** (`bug`, `enhancement`, `documentation`, `accessibility`) and link the issue it addresses, if any.
  The labels also decide where the pull request is listed in the release notes (see
  [Release notes](#release-notes)).
- **Check the target.** It should be `main`. Retarget it if not: `gh pr edit 42 --base main`.
- **Check that it is wanted.** If it is a larger change that nobody discussed first, say so kindly and point
  to the issue or discussion where it should be agreed, before anyone spends time reviewing code.
- **Thank first-time contributors.**

```sh
gh pr list --base main
gh pr view 42
gh pr edit 42 --add-label bug
```

### Reviewing

Check the pull request out locally and run the same checks contributors are asked to run:

```sh
gh pr checkout 42
dotnet build && dotnet test && dotnet format --verify-no-changes
```

Then read the diff (`gh pr diff 42`) with these questions in mind:

- **Does it do what it says,** and only that? Unrelated changes belong in a separate pull request.
- **Is it tested?** Bug fixes need a test that fails without the fix. Domain logic should be tested directly,
  without mocking EF Core or HTTP where it can be avoided.
- **Database changes:** is there a migration, does it upgrade an existing database with data in it, and does
  it leave already-merged migrations untouched? Try it on a copy of a real database (see
  [Testing the head of main](#testing-the-head-of-main)).
- **Configuration:** setting names are part of the user-facing contract. Renamed or removed settings break
  users' files; new settings must be documented in [configuration.md](configuration.md).
- **MAM safety:** the ratio, unsatisfied-slot and wedge checks must not be weakened, request politeness must
  be kept, and any new job that calls MAM must take the `MamAccessGate`.
- **Event log:** events are only ever appended, never updated or deleted.
- **Documentation:** user-visible changes are reflected in the [user guide](user-guide.md) or the
  [configuration reference](configuration.md).
- **UI changes:** screenshots in the pull request, and the change checked in both a light and a dark theme.
- **Dependencies:** new NuGet packages or front-end libraries need a reason, and should have been discussed.
- **Secrets:** no real `mam_id`, passwords, tokens, databases or personal paths in code, tests or
  screenshots.

Leave the review with `gh pr review` or in the browser. Be specific about what must change before merging and
what is only a suggestion.

```sh
gh pr review 42 --approve
gh pr review 42 --request-changes --body "The migration drops existing rows; see inline comment."
gh pr review 42 --comment --body "Looks good; one suggestion inline, not blocking."
```

When there is more than one maintainer, a maintainer's own pull request needs another maintainer's approval.
The ruleset requires no approvals, because GitHub doesn't let authors approve their own pull requests, and a
single maintainer would otherwise need the bypass for every merge.

### Merging into main

Merge with **squash**, the only merge method allowed, so every change lands on `main` as one commit:

```sh
gh pr merge 42 --squash --delete-branch \
  --subject "Keep one error per torrent and job on the Errors page" \
  --body-file squash-message.txt
```

The squash commit's title is the pull request's title and its body is the branch's commit messages; the pull
request's description isn't used, because it carries the template's checklist. The squash commit is what ends
up in the history, so edit its message to follow the commit conventions in the
[contributors guide](contributors-guide.md#commits): an imperative subject line, and a body that explains why.
Keep the contributor's `Co-authored-by:` lines and any `Fixes #123`.

After merging, check that the linked issue was closed.

### Outside contributions

- **Small fixes to a contributor's pull request:** push them to the contributor's branch rather than asking
  for another round. That works while **Allow edits by maintainers** is ticked on the pull request, which the
  contributors guide asks for. Say in a comment what was changed.

  ```sh
  gh pr checkout 42
  # fix, commit
  git push
  ```

- **Work in progress:** ask for a draft pull request, or leave a review with **Request changes**, so the list
  of open pull requests shows what is ready to merge.
- **Feature requests in code form:** if a pull request adds something that won't be built, say so early and
  kindly, before the author spends more time on it. Turn the idea into an issue if it is worth keeping, or
  decline it with a short reason.

### Stale and declined pull requests

- **Waiting on the author:** if a pull request with requested changes is quiet for two weeks, ask whether
  the author still plans to finish it. After another two weeks, close it with a friendly note that it can be
  reopened at any time.
- **Declining:** when a change doesn't fit the project, close it with a short, specific reason. Point to a
  discussion if the idea deserves more talk.

```sh
gh pr comment 42 --body "Are you still planning to finish this? No rush, just checking in."
gh pr close 42 --comment "Closing for now since it has been quiet; feel free to reopen any time."
```

## Testing the head of main

Automated tests prove the code is correct, not that the app works with real torrents, a real library and a
real MAM account. Maintainers run the head of `main` on their own setups between releases, so problems are
found before users see them.

Keep a local clone of the main repository (not a fork) for this:

```sh
gh repo clone crimsonking60/fetchshelf fetchshelf-maintainer
cd fetchshelf-maintainer
git switch main
git pull
dotnet build && dotnet test && dotnet format --verify-no-changes
```

Then:

1. **Run it against your own setup:** your configuration, your qBittorrent and your library. See
   [Running Fetchshelf locally](contributors-guide.md#running-fetchshelf-locally).
2. **Try what changed since the last release.** The list is in the commits:

   ```sh
   git fetch --tags
   git log --oneline "$(git describe --tags --abbrev=0 main)..main"
   ```

3. **Exercise the core paths:** run each job from the Jobs page, download something small and free, let it
   link, check the Torrents, Selected, Events and Errors pages, and check the library folder on disk.
4. **Try the migrations on a copy of a real database,** never the original:

   ```sh
   cp ~/fetchshelf-dev/data.db /tmp/upgrade-test.db
   dotnet ef database update --project src/Fetchshelf.Data --startup-project src/Fetchshelf.Data \
     --connection "Data Source=/tmp/upgrade-test.db"
   ```

5. **Build and run the container:** `docker compose up --build`.

Testers who don't build Fetchshelf themselves can run the head of `main` from its container image, which is
published on every push to `main`, after the checks pass:

```sh
docker pull ghcr.io/crimsonking60/fetchshelf:edge
```

The `edge` image is for testing only. It can change several times a day, and its database migrations may not
be final, so tell testers to point it at a copy of their data, never at the data their real Fetchshelf uses.
It reports its version as `edge-<commit>`.

Report what you find as issues, labelled `bug`, before the next release.

## Versioning

Fetchshelf uses calendar versioning, **`YYYY.MM.N`**:

| Part | Meaning |
| --- | --- |
| `YYYY` | The year of the release, for example `2026`. |
| `MM` | The month of the release, always two digits: `01` to `12`. |
| `N` | The release number within that month, starting at `1`. |

So the first release in October 2026 is `2026.10.1`, a hotfix later that month is `2026.10.2`, and the first
release in January 2027 is `2027.01.1`. With two-digit months, versions sort correctly as plain text.

- Git tags are the version with a `v` in front: `v2026.10.1`. Pushing one is what makes the release.
- Container images are published to the GitHub Container Registry as `ghcr.io/crimsonking60/fetchshelf`, tagged
  with the plain version (`2026.10.1`) and `latest` for the newest release. The head of `main` is tagged
  `edge`.
- The version says when a release was made, not how big it is. [`RELEASES.md`](../RELEASES.md) says what
  changed, and calls out anything users must do when upgrading.
- The maintainer preparing a release picks the number: the month the release is prepared in, and the next free
  `N` for that month.

  ```sh
  git fetch --tags
  git tag --list "v2026.10.*"     # no tags yet: 2026.10.1; v2026.10.1 exists: 2026.10.2; and so on
  ```

## Release notes

Every release is recorded in [`RELEASES.md`](../RELEASES.md) at the root of the repository, newest first, and
the newest one is named in the "Latest release" section of [`README.md`](../README.md). Both are updated in the
pull request that prepares the release (see [Preparing the release](#preparing-the-release)), so the notes
always ship with the code they describe.

Each release gets a section in this form:

```markdown
## 2026.10.1 - 2026-10-28

### Upgrading

- Anything users must do: configuration changes, new requirements, migrations that make going back to an
  older version impossible. Always include this subsection: when there is nothing to do, say so, and say
  whether going back to the previous version is possible.

### Added

- New features, one line each, with the pull request number: "Errors page: remove several errors at once (#45)".

### Changed

- Changes to existing behavior.

### Fixed

- Bug fixes, described as the user saw the bug: "Musicology torrents with flac files are now linked (#47)".

### Removed

- Features or settings that are gone.
```

Leave out any other subsection that would be empty. Write for people who run Fetchshelf, not for developers: say
what is different for them, not which classes changed. Links into the user guide or the configuration reference
point at the release's tag (`https://github.com/crimsonking60/fetchshelf/blob/v2026.10.1/docs/user-guide.md#…`),
not at `main`, so they keep describing that release as `main` moves on.

The GitHub release's description is that section, followed by GitHub's generated list of the pull requests
merged since the previous release, grouped by label as [`.github/release.yml`](../.github/release.yml) says.
Changes pushed straight to `main` aren't in that list, which is one reason the hand-written section comes
first.

## Releasing a new version

### Is main ready?

Release when `main` has something worth shipping and:

- [ ] the automated checks pass on the head of `main`;
- [ ] you have run the head of `main` (or its `edge` image) on a real setup since the last change (see
  [Testing the head of main](#testing-the-head-of-main));
- [ ] no open issue labelled `bug` was introduced since the last release, unless it is minor and known;
- [ ] migrations upgrade a copy of a real database cleanly;
- [ ] user-visible changes are documented in the [user guide](user-guide.md) or the
  [configuration reference](configuration.md).

### Preparing the release

1. **Pick the version** (see [Versioning](#versioning)).
2. **List what changed** since the last release:

   ```sh
   git fetch --tags
   last=$(git describe --tags --abbrev=0 main)
   git log --oneline "$last..main"
   gh pr list --repo crimsonking60/fetchshelf --base main --state merged --limit 100 \
     --search "merged:>=$(git log -1 --format=%cs "$last")" --json number,title,labels \
     --jq '.[] | "- \(.title) (#\(.number)) [\([.labels[].name] | join(", "))]"'
   ```

3. **Write the release notes** on a branch from `main`:
   - add the new section at the top of `RELEASES.md`, below the introduction;
   - update the "Latest release" section of `README.md` with the new version, its date, a link to its section
     in `RELEASES.md`, and links to the user guide and the configuration reference at the new tag:

     ```markdown
     ## Latest release

     **2026.10.1** (2026-10-28). See [RELEASES.md](RELEASES.md#2026101---2026-10-28) for what changed, and
     for every earlier release. The documentation for this release:
     [user guide](https://github.com/crimsonking60/fetchshelf/blob/v2026.10.1/docs/user-guide.md) and
     [configuration reference](https://github.com/crimsonking60/fetchshelf/blob/v2026.10.1/docs/configuration.md).
     ```

     The tag links don't resolve until the tag is pushed.

   ```sh
   git switch main && git pull
   git switch -c prepare-release/2026.10.1
   # edit RELEASES.md and README.md
   git commit -am "Prepare release 2026.10.1"
   git push -u origin prepare-release/2026.10.1
   gh pr create --repo crimsonking60/fetchshelf --base main --title "Prepare release 2026.10.1" --fill
   ```

4. **Squash-merge** the preparation pull request into `main` like any other, once the checks pass. Nothing is
   released yet.

### Making the release

1. **Tag the merged commit and push the tag.** Pushing the tag publishes the release, so check first that
   the head of `main` is the preparation commit (or a commit after it that you mean to ship):

   ```sh
   git switch main && git pull
   git log --oneline -3
   git tag -a v2026.10.1 -m 2026.10.1
   git push origin v2026.10.1
   ```

2. **Watch the release workflow.** The tag push starts it. It stops with an error naming the cause if the tag
   isn't `vYYYY.MM.N`, the tagged commit isn't on `main`, the first section of `RELEASES.md` at that commit is
   for a different version, or that version is already released. Otherwise it builds and tests, publishes the
   container image as `ghcr.io/crimsonking60/fetchshelf:<version>` and `:latest`, and creates the GitHub release
   (see [Release notes](#release-notes)).

   ```sh
   gh run list --workflow release.yml --limit 3
   gh run watch
   ```

3. **Check the result:** the GitHub release's description (the `RELEASES.md` section, then the list of pull
   requests), the image tags, and that the README's links at the new tag resolve.

   ```sh
   gh release view v2026.10.1 --repo crimsonking60/fetchshelf
   docker pull ghcr.io/crimsonking60/fetchshelf:2026.10.1
   ```

### After the release

1. **Pull the new image** on your own setup and check that it starts and upgrades your database.
2. **Close the milestone,** if you used one, and move anything unfinished to the next.

The release is announced by the updated "Latest release" section in `README.md` and its entry in
`RELEASES.md`; there is nothing else to publish.

## Hotfixes

When the current release has a bug that can't wait, fix it on `main` like any other change, then make a
release as above, with the next number of the month (for example `2026.10.2`). Keep the fix's pull request
minimal: only the fix and its test.

That is all it takes while `main` holds nothing that isn't ready to ship. When it does hold unready, untested
changes, there are three ways out, in this order of preference:

1. **Release `main` as it is,** if the unready changes are safe enough after a quick test.
2. **Revert the unready changes** on `main` (`git revert`, through a pull request), release, then bring them
   back with a revert of the revert.
3. **Release from a branch made from the last tag.** The release workflow refuses tags on commits that aren't on
   `main`, so this needs that check loosened first. It hasn't been needed so far; don't add it until it is.

## When a release goes wrong

- **The workflow failed:** if the cause was temporary (a registry hiccup, a flaky test), re-run it:
  `gh run rerun <run-id> --failed`. If the cause is in the repository (the tag was wrong, or the notes or the
  code need a fix), fix it on `main`, then delete the tag and push it again on the right commit. That needs the
  tag ruleset's bypass, and is only allowed while nothing was published: no image pushed, no GitHub release.

  ```sh
  git push origin :refs/tags/v2026.10.1 && git tag -d v2026.10.1
  ```

- **A broken release was published:** never delete, move or re-push its tag or image; people may already run
  it. Publish a fixed version as the next `N` as soon as possible (see [Hotfixes](#hotfixes)), and add a line
  to the broken release's section in `RELEASES.md` and to its GitHub release: "Don't use this version, upgrade
  to 2026.10.2."
- **Users need to go back:** they can pin the previous image tag, as long as the newer version didn't change
  the database in a way the older version can't read. Say so under "Upgrading" whenever a migration makes going
  back impossible.

## Maintainer gh command reference

| Task | Command |
| --- | --- |
| Open pull requests | `gh pr list --base main` |
| Label a pull request | `gh pr edit 42 --add-label bug` |
| Retarget a pull request | `gh pr edit 42 --base main` |
| Check out and test | `gh pr checkout 42`, then `dotnet build && dotnet test` |
| Read the diff | `gh pr diff 42` |
| Review | `gh pr review 42 --approve` / `--comment` / `--request-changes` |
| Squash-merge into main | `gh pr merge 42 --squash --delete-branch --subject "..." --body-file msg.txt` |
| Comment / close | `gh pr comment 42 --body "..."` / `gh pr close 42 --comment "..."` |
| What's unreleased | `git fetch --tags && git log --oneline "$(git describe --tags --abbrev=0 main)..main"` |
| Used version numbers this month | `git fetch --tags && git tag --list "v2026.10.*"` |
| Make the release | `git tag -a v2026.10.1 -m 2026.10.1 && git push origin v2026.10.1` |
| Watch the release workflow | `gh run list --workflow release.yml`, `gh run watch` |
| Re-run a failed workflow | `gh run rerun <run-id> --failed` |
| See a release | `gh release view v2026.10.1` |
| Close an issue as not planned | `gh issue close 123 --reason "not planned" --comment "..."` |
