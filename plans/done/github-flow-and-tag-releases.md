# Move to GitHub Flow: one `main` branch, releases from tags

## Context

Today the repository runs a two-branch flow (see `docs/maintainer-guide.md`):

- `develop` is the default branch. Every change lands there through a squash-merged pull request.
- `main` holds exactly what was last released. **Every push to `main` is a release**: `.github/workflows/release.yml`
  reads the version and notes from the first `## ` section of `RELEASES.md`, publishes
  `ghcr.io/crimsonking60/fetchshelf:<version>` and `:latest`, creates the tag and GitHub release, and opens a
  back-merge pull request from `main` into `develop`.
- A release takes a `release/YYYY.MM.N` branch with the notes, a pull request into `develop`, a release pull request
  from `develop` into `main` (merge commit), and the back-merge pull request.
- `.github/workflows/develop-image.yml` publishes the head of `develop` as `:develop` for testers.

The goal is a simpler flow for a solo maintainer. `develop` goes away, `main` becomes the default branch and the only
long-lived branch, and a release is a tag on `main`. Versions stay CalVer (`YYYY.MM.N`, tags `vYYYY.MM.N`). Semver is
not adopted.

### Decisions already made

| Topic | Decision |
|---|---|
| Versioning | Keep CalVer `YYYY.MM.N`, tags `vYYYY.MM.N`. Numbering continues from `v2026.11.7`. |
| Release notes | Keep the hand-written `RELEASES.md` section, including its **Upgrading** subsection. The GitHub release gets that section followed by GitHub's auto-generated list of merged pull requests. |
| Test image | `:develop` is replaced by `:edge`, the head of `main`, published on every push to `main`. `:develop` stops updating. |
| Docs for a released version | The README's "Latest release" section links the user guide and configuration reference at the release tag (`blob/v<version>/…`), updated with every release. The banners say `main` may describe unreleased changes. |
| What goes straight to `main` | Trivial changes (typos, doc tweaks, dependency bumps). Anything touching code or behaviour goes on a short-lived branch and a pull request. |

## Does the proposed model work here?

Mostly yes. The repository is already close to it:

- **Reviews and protections.** The "Protect main" ruleset already requires a pull request with **0** approving
  reviews and the `Build, test and format` check, with an admin bypass. "Never delete or rewrite main" blocks deletion
  and force-pushes with no bypass. That is the solo setup the model describes: self-merge, no force-push, no deletion,
  CI required.
- **History.** Squash-merge is already the rule for pull requests, so `main` gets one commit per logical change.
- **No branch surgery.** `main` is an ancestor of `develop` (every commit on `main` is also on `develop`), so `main`
  can be fast-forwarded to `develop`: no merge commit, no rewritten hashes.
- **Nothing in flight.** There are no open pull requests to retarget.
- **Tooling.** Labels (`bug`, `good first issue`, `wontfix`, …), issue forms and a pull request template already
  exist.

These parts must change, and the order matters:

1. **"Every push to `main` is a release" is the blocker.** In the new model every merge lands on `main`. As long as
   `release.yml` triggers on pushes to `main`, the first merge would try to publish a release. In practice it would
   fail, because the top `RELEASES.md` section, `2026.11.7`, is already tagged. The tag-triggered `release.yml` must
   therefore be in place **before** anything from `develop` reaches `main`. Workflows run from the file in the commit
   that triggered them, so landing the new `release.yml` on `develop` first and then fast-forwarding `main` is safe.
2. **The back-merge job, the `release/…` branches and the `develop` image** have no purpose any more.
3. **Docs that promise "`main` matches the latest release"** (README note, user guide banner, image tags table,
   SECURITY.md, contributors and maintainer guides, pull request template, CONTRIBUTING.md) become wrong.

Things to know about the new model:

- **Direct pushes skip CI until after they land.** A trivial change pushed straight to `main` uses the admin bypass,
  so the check runs after the push, not before. A red `main` is possible and is fixed with a follow-up commit. That is
  the reason for keeping direct pushes to genuinely trivial changes.
- **Auto-generated notes only list pull requests.** Direct pushes don't appear in them. That is fine, because
  `RELEASES.md` stays the primary notes, but it is one more reason the hand-written section stays.
- **`Fixes #123` starts working on merge.** Issues are closed by merges into the default branch, which is now the
  branch that releases are cut from. Today they close when the change reaches `develop`.
- **Hotfixes when `main` holds unready work.** Without release branches, an urgent fix while `main` has unreleased,
  untested changes means one of three things: release `main` as it is, revert the unready change, or (only then)
  branch from the last tag. The release workflow below requires the tagged commit to be on `main`. A release branch
  would need that check loosened, and is left until it is actually needed.
- **Old release notes link to `blob/main/…` anchors.** Those links may drift as `main` moves on. From the next
  release, `RELEASES.md` links go to the release tag.

## Open questions (defaults used unless decided otherwise)

1. **Dependabot scope and cadence.** Default: weekly, for `nuget` (root) and `github-actions`. Minor and patch updates
   are grouped into one pull request per ecosystem, majors come one per pull request. `docker` is left out: the
   Dockerfile uses floating `10.0` tags, which already pick up patches at every build.
2. **Dependabot auto-merge.** Default: off. Its pull requests are merged by hand once CI passes. Turning on "Allow
   auto-merge" in the repository settings would let them merge themselves.
3. **Squash commit message.** Default: title = pull request title, body = the branch's commit messages (as today).
   The pull request body is not used: it carries the template's checklist.
4. **Remote `feature/*` branches.** Default: delete the eight left on GitHub
   (`feature/account-page`, `feature/job-failures`, `feature/job-runs`, `feature/notifications`,
   `feature/queryable-authors-and-series`, `feature/search-page`, `feature/series-and-author-follow`,
   `feature/upgrade-to-better-copy`), once each is confirmed merged. Local `feature/*` and `release/*` branches are
   deleted too.
5. **A last release before the switch.** Default: no. The unreleased commits on `develop` simply become unreleased
   commits on `main`, and the first tag-based release ships them.
6. **The dev instance's image.** If `dev-fetchshelf` runs the `:develop` image, it moves to `:edge` after step 3.

## Who does what

Each step is marked:

- **Repo**: a change to files in the repository, or a `git`/`gh` command. It can be prepared from the command line.
- **Manual**: needs a decision, a review, a test on the maintainer's own setup, or a click in GitHub's settings.
- **Repo or manual**: a GitHub setting that `gh api` can change with the admin token, or the web UI can. Outward
  facing, so it is only done on an explicit go-ahead.

## Steps

### 1. Get `develop` into a clean state

1. **Manual.** Commit (or deliberately set aside) any uncommitted work in the local clone. At the time of writing,
   the `extra_categories` change is uncommitted on `develop`.
2. **Repo.** Check that `main` is still an ancestor of `develop` on GitHub:
   `git fetch --prune && git merge-base --is-ancestor origin/main origin/develop`.

### 2. Change workflows, docs and conventions (one pull request into `develop`)

Branch `chore/github-flow` from `develop`. The pull request goes into `develop` (still protected), and is
squash-merged after review.

**Workflows** (**Repo**)

- `checks.yml`: `pull_request: branches: [main]`. Update the header comment ("called by the edge-image and release
  workflows").
- `develop-image.yml` → `edge-image.yml`: triggers on `push: branches: [main]`, runs the checks, then publishes
  `:edge` with version label `edge-<short sha>`. Concurrency group `edge-image`.
- `release.yml`, rewritten:
  - Trigger: `push: tags: ['v*']`.
  - Validate: the tag matches `^v[0-9]{4}\.(0[1-9]|1[0-2])\.(0|[1-9][0-9]*)$`. The tagged commit is an ancestor of
    `origin/main` (needs `fetch-depth: 0`). The first `## ` heading of `RELEASES.md` **at the tagged commit** starts
    with the same version. Any failure ends the run with an `::error::` naming the cause.
  - Extract that section's notes as today.
  - Run the checks, then build and push `:<version>` and `:latest` with the same build arguments and labels as today.
  - `gh release create "$TAG" --verify-tag --title "$VERSION" --notes-file notes.md --generate-notes`. The
    hand-written section comes first, followed by the generated list of merged pull requests since the previous
    release.
  - The `back-merge` job is removed.
- New `.github/release.yml` (the release-notes configuration, not a workflow): groups the generated list by label
  (for example **Bugs** for `bug`, **Features** for `enhancement`, **Dependencies** for `dependencies`), and leaves
  out `invalid`, `duplicate` and `wontfix`.

**Code** (**Repo**)

- `Dockerfile`: the comment's example `develop-<commit>` becomes `edge-<commit>`.
- `tests/Fetchshelf.Web.Tests/AppVersionTests.cs`: test data `develop-14747e2` becomes `edge-14747e2`. The
  behaviour itself doesn't change.

**Docs** (**Repo**)

- `README.md`: rewrite the branch note (`main` is where development happens and may describe unreleased changes; for
  a released version, use the links under "Latest release"). "Latest release" gets links to the user guide and
  configuration reference at `blob/v2026.11.7/…` until the next release.
- `docs/user-guide.md`: the banner at the top, the image tags table (`edge` replaces `develop`, with the same
  warning), and the "NEVER run the `develop` image" line.
- `SECURITY.md`: `develop` image → `edge` image.
- `CONTRIBUTING.md`: kept short. Fork, branch from `main`, run the three checks, open the pull request against
  `main`, leave **Allow edits by maintainers** ticked, open an issue first for anything larger than a small fix.
- `docs/contributors-guide.md`: the branches section becomes "one branch, `main`". Every `develop` in the git and
  `gh` commands becomes `main` (fork sync, rebase, `gh pr create --base`). "Never edit a migration that is already
  on `main`". The merge section says maintainers squash-merge into `main`, and may push small fixes to the
  contributor's branch.
- `docs/maintainer-guide.md`, rewritten around the new flow:
  - **Branches:** `main` plus short-lived topic branches. The direct-push rule for trivial changes.
  - **Rulesets:** as set in step 4.
  - **Pull requests:** triage, review, squash-merge; outside contributions (push fixes to the contributor's branch,
    drafts or "changes requested" for work in progress, close stale ones with a friendly note, turn feature requests
    that won't be built into an issue or decline them early).
  - **Testing the head of `main`:** the `:edge` image.
  - **Versioning:** unchanged CalVer rules; the next `N` comes from `git tag --list "vYYYY.MM.*"`.
  - **Release notes:** unchanged format, including **Upgrading**. Links into the docs point at the release tag.
  - **Releasing:** (1) check `main` is ready (checks green, `:edge` run on a real setup); (2) write the
    `RELEASES.md` section and update the README's "Latest release" (version, date, links at the new tag) in a
    "Prepare release X" pull request into `main`; (3) after it is merged,
    `git switch main && git pull && git tag -a vX -m X && git push origin vX`; (4) watch the release workflow and
    check the image tags and the GitHub release.
  - **Hotfixes:** a fix on `main` followed by a release, as above. The trade-off when `main` holds unready work.
  - **When a release goes wrong:** a failed workflow run can be re-run. A wrong tag that was never published can be
    deleted (with the tag ruleset's admin bypass) and pushed again. A published version is never re-tagged: the fix
    ships as the next `N`.
  - The `gh` command reference, updated.
- `.github/pull_request_template.md`: the link points at `blob/main/…`, and the checklist says "up to date with
  `upstream/main`". The rest is unchanged.
- `RELEASES.md`: no new section. The intro line about reading **Upgrading** notes stays.

**Repository instructions** (**Repo**)

- `CLAUDE.md`, "Implementing feature requests": the pull request goes into `main`, not `develop`.

### 3. Switch `main` over

In this order, right after the step 2 pull request is merged into `develop`:

1. **Repo or manual.** Fast-forward `main` to `develop`: `git fetch origin && git push origin origin/develop:main`.
   This is a fast-forward, so "Never delete or rewrite main" allows it. "Protect main" requires a pull request and
   is passed with the admin bypass. The push runs the **new** workflows from the pushed commit: `edge-image.yml`
   publishes the first `:edge`, and `release.yml` does not run (no tag).
2. **Manual.** Check the Actions tab: the checks and the edge image succeeded, and no release run started.
3. **Repo or manual.** Make `main` the default branch: `gh repo edit --default-branch main` (or Settings →
   General → Default branch).
4. **Manual.** Move the dev instance from `:develop` to `:edge`, if it uses the image.

### 4. Repository settings and rulesets

**Repo or manual** for each item (`gh api` with the admin token, or Settings in the web UI):

1. Delete the "Protect develop" ruleset (id 26452904). It also blocks deleting `develop`.
2. "Protect main" (id 26452672): allowed merge methods → `squash` only. Keep 0 required reviews, no code-owner review
   (the author can't approve their own pull request, so requiring one would block every self-merge without the
   bypass), the required `Build, test and format` check and the admin bypass.
3. Keep "Never delete or rewrite main" (id 26753240) as it is.
4. Add a tag ruleset "Protect release tags" for `refs/tags/v*`: block creation, update and deletion, with an admin
   bypass. Tags then only come from the maintainer, and a published tag can't be moved by accident.
5. General → Pull Requests: turn off **Allow merge commits** (it was only needed for release and back-merge pull
   requests). Keep **Allow squash merging** with the commit message from open question 3. Keep **Automatically
   delete head branches**: with no pull requests from `main` any more and the no-bypass deletion ruleset, the
   earlier accident (auto-delete removed `main` after the back-merge) can't happen again.
6. **Manual.** Security → turn on Dependabot alerts and security updates, if they are not on already.

### 5. Retire `develop` and clean up

1. **Repo or manual.** Delete `develop` on GitHub: `git push origin --delete develop`. Only after steps 3 and 4.1.
2. **Repo or manual.** Delete the merged remote `feature/*` branches (open question 4).
3. **Repo.** In the local clone: `git switch main && git pull`, `git remote set-head origin -a`, then delete the
   local `develop`, `feature/*` and `release/*` branches once each is confirmed merged (`git branch --merged main`).
4. **Manual.** Optionally, in GitHub Packages, mark or delete the old `:develop` image. It no longer updates, and the
   docs say so.

### 6. Dependency updates (one pull request into `main`)

**Repo.** Add `.github/dependabot.yml` with the schedule and grouping from open question 1. Its pull requests run
the required check like any other and are squash-merged once green. Dependabot labels them `dependencies`, which
`.github/release.yml` groups in the generated notes.

This comes after step 3: Dependabot opens its pull requests against the default branch.

### 7. The first release from a tag

1. **Repo.** On a branch, a "Prepare release 2026.11.8" pull request into `main` (or the right `N` for the month by
   then): the `RELEASES.md` section with **Upgrading** (the switch itself changes nothing for users; `:develop`
   users move to `:edge`), and the README's "Latest release" with links at `v2026.11.8`.
2. **Manual.** Review the notes, and merge.
3. **Manual.** Tag and push: `git tag -a v2026.11.8 -m 2026.11.8 && git push origin v2026.11.8`. Pushing the tag
   publishes, so it stays a deliberate maintainer action.
4. **Repo.** Watch the run (`gh run watch`), then check `:2026.11.8` and `:latest` in GHCR, the GitHub release body
   (the hand-written section followed by the pull request list), and that the README's tag links resolve.

### 8. Working notes outside the repository

**Repo.** Update the local assistant notes that describe the old flow: work on `develop`, releases via `release/X`
branches and back-merge pull requests, and the main-deletion caveat that only applied to back-merges. The new notes
cover the direct-push rule for trivial changes, branch and pull request for everything else, and tag-based releases
that pause before the tag is pushed.

## Verification

- **Step 2:** `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` pass. `actionlint` (if
  available) is clean on the three workflows.
- **Step 3:** the first push to `main` runs Checks and Edge image only.
- **Step 7:** a deliberately wrong tag (for example `v2026.13.1`, created and deleted with the bypass, or on a fork)
  fails with a clear error, and the real tag publishes the image and the release.
- **After step 5:** `grep -rn "develop" --include=*.md --include=*.yml .` finds only meaningful uses (for example
  "develop Fetchshelf"), plus `RELEASES.md` history, which is left as written.
