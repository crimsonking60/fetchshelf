# Fetchshelf maintainer guide

This guide is for maintainers: the people who review and merge pull requests and publish releases. It
assumes you have write access to <https://github.com/crimsonking60/fetchshelf> and know the contributor
workflow in the [contributors guide](contributors-guide.md).

## Contents

- [Branches](#branches)
- [Working with pull requests](#working-with-pull-requests)
  - [Triage](#triage)
  - [Reviewing](#reviewing)
  - [Merging into develop](#merging-into-develop)
  - [Stale and declined pull requests](#stale-and-declined-pull-requests)
- [Testing the head of develop](#testing-the-head-of-develop)
- [Versioning](#versioning)
- [Release notes](#release-notes)
- [Releasing a new version](#releasing-a-new-version)
  - [Is develop ready?](#is-develop-ready)
  - [Preparing the release](#preparing-the-release)
  - [Making the release](#making-the-release)
  - [After the release](#after-the-release)
- [Hotfixes](#hotfixes)
- [When a release goes wrong](#when-a-release-goes-wrong)
- [Maintainer gh command reference](#maintainer-gh-command-reference)

## Branches

| Branch | Holds | Who changes it, and how |
| --- | --- | --- |
| `develop` | The integration branch: everything merged since the last release. The repository's default branch. | Pull requests from contributors and maintainers, squash-merged. |
| `main` | Exactly what was last released. Every commit on `main` is a release. | Only the release pull request from `develop` and hotfix pull requests. |
| `fix/…`, `feature/…`, `docs/…`, … | One change each, in contributors' forks. | Their authors. |
| `release/…` | The release notes for the next release, branched from `develop`. | Maintainers. See [Preparing the release](#preparing-the-release). |
| `hotfix/…` | An urgent fix to the current release, branched from `main`. | Maintainers. See [Hotfixes](#hotfixes). |

Rules that keep this working:

- **Nobody pushes directly to `develop` or `main`,** maintainers included. Maintainers' own changes go
  through pull requests like everyone else's.
- **Pushing to `main` publishes a release.** Merge into `main` only when you mean to release.
- **`main` is always an ancestor of `develop`.** After every release or hotfix, `main` is merged back into
  `develop` (see [After the release](#after-the-release)).
- Because `develop` is the default branch, pull requests target it automatically, and `Fixes #123` in a pull
  request closes the issue when it is merged into `develop`.
- **Visitors land on `develop`,** being the default branch, so the documentation they see first is for
  changes not released yet. The README's notice and the notes at the top of the user guide and the
  configuration reference say so in words that are true on both branches, so they can be merged into `main`
  as they are. Keep it that way: no wording that only fits one branch. The README's links for people who run
  Fetchshelf (the user guide, the configuration reference, getting help) are absolute links to `main`; a
  link to a page that isn't on `main` yet stays relative until the release that brings it.

Repository rulesets enforce this:

| Ruleset | Branch | Rules | Bypass |
| --- | --- | --- | --- |
| Protect develop | `develop` | No deletion or force-push; changes through a pull request with passing checks. | Repository admins |
| Protect main | `main` | The same. | Repository admins |
| Never delete or rewrite main | `main` | No deletion or force-push. | Nobody |

The third ruleset exists because the repository deletes a pull request's head branch once it is merged, and
GitHub does that as the person who merged. That person is usually an admin, who may bypass the first two
rulesets, so after the 2026.11.3 release merging the back-merge pull request (whose head is `main`) deleted
`main`. With no bypass, the third ruleset refuses that deletion, and anyone else's. To delete or rewrite
`main` on purpose, which should never be needed, disable that ruleset first and enable it again straight
after.

## Working with pull requests

### Triage

Look at new pull requests within a few days, even if a full review has to wait. For each one:

- **Label it** (`bug`, `enhancement`, `documentation`, `accessibility`) and link the issue it addresses, if any.
- **Check the target.** It should be `develop`. Retarget it if not: `gh pr edit 42 --base develop`.
- **Check that it is wanted.** If it is a larger change that nobody discussed first, say so kindly and point
  to the issue or discussion where it should be agreed, before anyone spends time reviewing code.
- **Thank first-time contributors.**

```sh
gh pr list --base develop
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
  [Testing the head of develop](#testing-the-head-of-develop)).
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

### Merging into develop

Merge with **squash**, so every change lands on `develop` as one commit:

```sh
gh pr merge 42 --squash --delete-branch \
  --subject "Keep one error per torrent and job on the Errors page" \
  --body-file squash-message.txt
```

The squash commit is what ends up in the history and the release notes, so edit its message to follow the
commit conventions in the [contributors guide](contributors-guide.md#commits): an imperative subject line,
and a body that explains why. Keep the contributor's `Co-authored-by:` lines and any `Fixes #123`.

After merging, check that the linked issue was closed.

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

## Testing the head of develop

Automated tests prove the code is correct, not that the app works with real torrents, a real library and a
real MAM account. Maintainers run the head of `develop` on their own setups between releases, so problems are
found before users see them.

Keep a local clone of the main repository (not a fork) for this:

```sh
gh repo clone crimsonking60/fetchshelf fetchshelf-maintainer
cd fetchshelf-maintainer
git switch develop
git pull
dotnet build && dotnet test && dotnet format --verify-no-changes
```

Then:

1. **Run it against your own setup:** your configuration, your qBittorrent and your library. See
   [Running Fetchshelf locally](contributors-guide.md#running-fetchshelf-locally).
2. **Try what changed since the last release.** The list is in the commits:

   ```sh
   git log --oneline main..develop
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

Testers who don't build Fetchshelf themselves can run the head of `develop` from its container image, which
is published on every push to `develop`:

```sh
docker pull ghcr.io/crimsonking60/fetchshelf:develop
```

The `develop` image is for testing only. It can change several times a day, and its database migrations may
not be final, so tell testers to point it at a copy of their data, never at the data their real Fetchshelf
uses.

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

- Git tags are the version with a `v` in front: `v2026.10.1`.
- Container images are published to the GitHub Container Registry as `ghcr.io/crimsonking60/fetchshelf`, tagged
  with the plain version (`2026.10.1`) and `latest` for the newest release. The head of `develop` is tagged
  `develop`.
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
same pull request that prepares the release (see [Preparing the release](#preparing-the-release)), so the
notes always ship with the code they describe.

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
what is different for them, not which classes changed.

The release workflow reads the version from the first `## ` heading of `RELEASES.md`, and uses that section as
the GitHub release's description. While `RELEASES.md` lists no release, a push to `main` publishes nothing.

## Releasing a new version

### Is develop ready?

Release when `develop` has something worth shipping and:

- [ ] the automated checks pass on the head of `develop`;
- [ ] you have run the head of `develop` on a real setup since the last change (see
  [Testing the head of develop](#testing-the-head-of-develop));
- [ ] no open issue labelled `bug` was introduced since the last release, unless it is minor and known;
- [ ] migrations upgrade a copy of a real database cleanly;
- [ ] user-visible changes are documented in the [user guide](user-guide.md) or the
  [configuration reference](configuration.md).

### Preparing the release

1. **Pick the version** (see [Versioning](#versioning)).
2. **List what changed** since the last release:

   ```sh
   git log --oneline main..develop
   gh pr list --repo crimsonking60/fetchshelf --base develop --state merged --limit 100 \
     --search "merged:>=$(git log -1 --format=%cs main)" --json number,title,labels \
     --jq '.[] | "- \(.title) (#\(.number)) [\([.labels[].name] | join(", "))]"'
   ```

3. **Write the release notes** on a branch from `develop`:
   - add the new section at the top of `RELEASES.md`, below the introduction (for the first release, also
     remove the "No release has been published yet." line);
   - update the "Latest release" section of `README.md` with the new version, its date and a link to its
     section in `RELEASES.md`:

     ```markdown
     ## Latest release

     **2026.10.1** (2026-10-28). See [RELEASES.md](RELEASES.md#2026101---2026-10-28) for what changed, and
     for every earlier release.
     ```

   ```sh
   git switch develop && git pull
   git switch -c release/2026.10.1
   # edit RELEASES.md and README.md
   git commit -am "Prepare release 2026.10.1"
   git push -u origin release/2026.10.1
   gh pr create --repo crimsonking60/fetchshelf --base develop --title "Prepare release 2026.10.1" --fill
   ```

4. **Squash-merge** the preparation pull request into `develop` like any other.

### Making the release

1. **Open the release pull request** from `develop` into `main`:

   ```sh
   gh pr create --repo crimsonking60/fetchshelf --base main --head develop \
     --title "Release 2026.10.1" --body "See RELEASES.md."
   ```

2. **Check it** one last time: the commit list (`gh pr view --web`) contains what you expect, the top of
   `RELEASES.md` has the version you picked, and the checks pass.

3. **Merge it with a merge commit**, not squash or rebase, and **don't delete the branch**:

   ```sh
   gh pr merge <number> --merge
   ```

   A merge commit keeps `develop`'s individual commits in `main`'s history and keeps the two branches related.
   Squashing would make `main` and `develop` disagree about history and cause conflicts at the next release.

4. **Watch the release workflow.** The push to `main` starts it. It reads the version from `RELEASES.md`, stops
   if that version's tag already exists or doesn't match `YYYY.MM.N`, builds and tests, publishes the container
   image as `ghcr.io/crimsonking60/fetchshelf:<version>` and `:latest`, tags the commit, creates the GitHub
   release with the `RELEASES.md` section as its description, and merges `main` back into `develop` (see
   [After the release](#after-the-release)).

   ```sh
   gh run list --workflow release.yml --limit 3
   gh run watch
   ```

5. **Check the result:**

   ```sh
   gh release view --repo crimsonking60/fetchshelf
   docker pull ghcr.io/crimsonking60/fetchshelf:2026.10.1
   ```

### After the release

1. **Check that `main` was merged back into `develop`.** The release workflow's last job does this, so `main`
   stays an ancestor of `develop`: it pushes a merge commit "Merge release 2026.10.1 back into develop" to
   `develop`. If it can't, because the merge has conflicts, `develop` moved on during the release, or branch
   protection refused the push, it opens a pull request from `main` into `develop` instead and says why in a
   warning on the workflow run. Resolve any conflicts and merge that pull request with a merge commit:

   ```sh
   gh pr list --repo crimsonking60/fetchshelf --base develop --head main
   gh pr merge <number> --merge
   ```

   A pull request opened by a workflow doesn't start other workflows, so its checks never run. Merge it by
   bypassing the branch rules, after making sure `develop` builds.

   With the current rulesets the direct push is always refused, since `develop` only takes pull requests, so
   expect this pull request after every release.

   Merging it also makes the repository try to delete its head branch, `main`. The *Never delete or rewrite
   main* ruleset refuses that (see [Branches](#branches)); check afterwards that `main` is still there:
   `gh api repos/crimsonking60/fetchshelf/branches/main --jq .name`. If it is gone, because the ruleset was
   disabled or removed, press **Restore branch** on the merged pull request straight away and restore the
   ruleset.

   A local `origin/main` doesn't show that `main` was deleted on GitHub, since fetching keeps branches that are
   gone. Fetch with `git fetch --prune` to see the real state.

   Pull `develop` before you continue working on it: `git switch develop && git pull`.

2. **Pull the new image** on your own setup and check that it starts and upgrades your database.
3. **Close the milestone,** if you used one, and move anything unfinished to the next.

The release is announced by the updated "Latest release" section in `README.md` and its entry in
`RELEASES.md`; there is nothing else to publish.

## Hotfixes

When the current release has a bug that can't wait, and `develop` holds changes that aren't ready to ship:

1. Branch from `main`, fix, and add the release notes in the same branch: a new section at the top of
   `RELEASES.md` with the next number of the month (for example `2026.10.2`), and the updated "Latest release"
   section in `README.md`.

   ```sh
   git switch main && git pull
   git switch -c hotfix/2026.10.2
   # fix, test, update RELEASES.md and README.md, commit
   git push -u origin hotfix/2026.10.2
   gh pr create --repo crimsonking60/fetchshelf --base main --title "Release 2026.10.2" --fill
   ```

2. Review it as usual, but keep it minimal: only the fix, its test and the release notes.
3. Squash-merge it into `main`. That publishes the release.
4. The release workflow merges `main` back into `develop`, so the fix and its release notes aren't lost. A
   hotfix is the likeliest release to conflict with `develop`; if it does, finish the merge as in
   [After the release](#after-the-release).

If `develop` is ready to release anyway, skip the hotfix branch: fix it on `develop` and make a normal
release.

## When a release goes wrong

- **The workflow failed before publishing:** nothing reached users and no tag was created. Fix the cause on
  `develop` (or with a hotfix) and merge into `main` again. The version in `RELEASES.md` can stay the same.
- **A broken release was published:** don't delete or move its tag or image; people may already run it.
  Publish a fixed version as soon as possible (usually a [hotfix](#hotfixes)), and add a line to the broken
  release's section in `RELEASES.md` and to its GitHub release: "Don't use this version, upgrade to
  2026.10.2."
- **Users need to go back:** they can pin the previous image tag, as long as the newer version didn't change
  the database in a way the older version can't read. Say so under "Upgrading" whenever a migration makes going
  back impossible.

## Maintainer gh command reference

| Task | Command |
| --- | --- |
| Open pull requests for develop | `gh pr list --base develop` |
| Label a pull request | `gh pr edit 42 --add-label bug` |
| Retarget a pull request | `gh pr edit 42 --base develop` |
| Check out and test | `gh pr checkout 42`, then `dotnet build && dotnet test` |
| Read the diff | `gh pr diff 42` |
| Review | `gh pr review 42 --approve` / `--comment` / `--request-changes` |
| Squash-merge into develop | `gh pr merge 42 --squash --delete-branch --subject "..." --body-file msg.txt` |
| Comment / close | `gh pr comment 42 --body "..."` / `gh pr close 42 --comment "..."` |
| What's unreleased | `git log --oneline main..develop` |
| Used version numbers this month | `git fetch --tags && git tag --list "v2026.10.*"` |
| Open the release pull request | `gh pr create --base main --head develop --title "Release 2026.10.1"` |
| Merge the release | `gh pr merge <number> --merge` (no `--delete-branch`) |
| Watch the release workflow | `gh run list --workflow release.yml`, `gh run watch` |
| See a release | `gh release view v2026.10.1` |
| Close an issue as not planned | `gh issue close 123 --reason "not planned" --comment "..."` |
