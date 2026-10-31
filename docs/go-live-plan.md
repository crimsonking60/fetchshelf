# Fetchshelf go-live plan

> **Work in progress.** This plan is incomplete and may change.

What to do when the repository at <https://github.com/crimsonking60/fetchshelf> goes from private to public.
The steps are in order: decide, prepare while still private, switch the repository and the container
image to public, turn on what only works for public repositories, check that it all works, then follow up.

Tick each item when it is done. "You" means decisions or steps only the project owner can do. "Me" means
work I can do on request.

## 1. Decide first

These decisions change what is published, so make them before anything becomes public.

- [x] **You:** Commit email. Every commit so far is authored as `crimsonking60@tutamail.com`, and that address
  becomes public with the history. Decided: that is fine, the address is meant to be public.
- [x] **You:** Support channel. Decided: both GitHub Discussions (questions, ideas) and issues (bugs, feature
  requests), plus private vulnerability reports for security problems, all enabled before the project is
  announced.
- [x] **You:** Code of conduct. Decided: "Don't be a dick", in `CODE_OF_CONDUCT.md`.
- [x] **You:** Images for 64-bit ARM (`linux/arm64`) as well as `linux/amd64`, for Raspberry Pi and ARM-based
  NAS users. Decided: `linux/amd64`, `linux/arm64` and `linux/arm/v7`, every platform the .NET base images
  support.
- [ ] **You:** Run an ARM image on a real ARM device once one is published. The images are checked to contain
  the right native code for each architecture, but only the `linux/amd64` image has been started.
- [ ] **You:** Whether the user, contributors and maintainer guides keep their "Work in progress" note.

## 2. Prepare while still private

### Content

- [x] **Me:** Move the internal to-do lists out of the published guides: "Open items for this guide" in the
  user guide, "Open items" in the contributors guide and "To do" in the maintainer guide. They address "You"
  and "Me" and are notes for us, not for readers. Done on 2026-10-31: the sections are removed, and every
  item still open was already in this plan.
- [ ] **Me:** Add issue templates for bug reports and feature requests, and a pull request template with the
  checklist from [Before you open one](contributors-guide.md#before-you-open-one).
- [ ] **Me:** Add a `SECURITY.md`. GitHub shows it on the **Security** tab and next to **Report a
  vulnerability**; it should say what `CONTRIBUTING.md` already says about reporting security problems.
- [ ] **Me:** Fill in [Getting help](user-guide.md#getting-help) in the user guide and
  [Discussions](contributors-guide.md#discussions) in the contributors guide: Discussions for questions and
  ideas, issues for bugs and feature requests, and the **Security** tab for security problems.
- [ ] **Me:** Show the version in the web UI and the startup log, so bug reports can say which version they are
  about.
- [ ] **You:** Check the Notion steps in [Importing reading lists](user-guide.md#importing-reading-lists) and the
  Audiobookshelf steps in [Connecting to Audiobookshelf](user-guide.md#connecting-to-audiobookshelf).
- [ ] **Me:** Final read-through of the user guide against the configuration reference, then check all links.
- [ ] **Me:** Update the "Status" section of `README.md` to say what state the project is in for new readers.

### Secrets in the history

- [x] **Me:** Scan every commit for secrets. Done on 2026-10-31 with pattern searches over all 63 commits for
  `mam_id`, token and password values, Notion and GitHub tokens, JWTs and private keys. The only matches
  are the made-up values in `tests/Fetchshelf.Web.Tests/ConfigPageTests.cs`, which test that the Config page
  hides secrets.
- [ ] **Me:** Repeat the scan right before switching, with a dedicated scanner such as
  [gitleaks](https://github.com/gitleaks/gitleaks) (`gitleaks git .`), to cover anything committed since.

### Repository settings that already work while private

- [ ] **You:** Merge settings: allow squash merging and merge commits, turn off rebase merging, and delete head
  branches automatically after merging (protected branches such as `develop` are never deleted):

  ```sh
  gh repo edit crimsonking60/fetchshelf \
    --enable-squash-merge --enable-merge-commit --enable-rebase-merge=false --delete-branch-on-merge
  ```

- [ ] **You:** Description and topics, shown on the repository page and in search:

  ```sh
  gh repo edit crimsonking60/fetchshelf \
    --description "Self-hosted bridge between MyAnonamouse, qBittorrent and Audiobookshelf" \
    --add-topic audiobookshelf --add-topic qbittorrent --add-topic myanonamouse --add-topic self-hosted
  ```

- [x] **You:** Turn off **Projects** (unused). Done on 2026-10-31.
- [x] **You:** Issues: already enabled.
- [x] **You:** Enable Discussions. Done on 2026-10-31; GitHub created the default categories Announcements,
  General, Ideas, Polls, Q&A and Show and tell.
- [x] **You:** Adjust the Discussions categories under the **Discussions** tab if the defaults don't fit, for
  example add Development or remove Polls. Decided: keep the defaults.

## 3. Go public

- [ ] **You:** Make the repository public: **Settings → General → Danger Zone → Change repository
  visibility**. The whole history, the existing releases, issues and Actions logs become visible.
- [ ] **You:** Make the container image public. Packages published from a private repository stay private
  until changed: open the `fetchshelf` package from the repository's **Packages** sidebar, then **Package
  settings → Danger Zone → Change package visibility → Public**. The image already carries the
  `org.opencontainers.image.source` label, so the package is linked to the repository.
- [ ] **You:** Right away, turn on private vulnerability reporting, which
  [Security problems](contributors-guide.md#security-problems) and `CONTRIBUTING.md` rely on. GitHub only
  offers it for public repositories (it answers "Not Found" while the repository is private), so it cannot
  be done earlier:

  ```sh
  gh api --method PUT repos/crimsonking60/fetchshelf/private-vulnerability-reporting
  ```

## 4. Turn on what needs a public repository

### Branch protection

Rulesets need GitHub Pro or a public repository; on the free plan GitHub refuses them for private
repositories. The commands below create one ruleset per branch:

| | `develop` | `main` |
|---|---|---|
| Block deletion and force pushes | yes | yes |
| Require a pull request | yes | yes |
| Require code-owner review | yes | no |
| Require the "Build, test and format" check | yes | yes |

Repository admins may bypass both rulesets, so the owner can still push directly and merge their own pull
requests. GitHub never lets an author approve their own pull request, so code-owner review could not
otherwise be met while the owner is the only code owner. A bypass also skips the required check, so look at
the checks before merging that way.

GitHub Actions may also bypass the `develop` ruleset, so the release workflow can push its merge of `main`
back into `develop`. Without that, the workflow opens a pull request for the merge instead. Workflows run
from forks only ever get a read-only token, so they cannot use this.

- [ ] **You:** Create the rulesets:

  ```sh
  ruleset() {  # name, branch, require code-owner review (true/false), let GitHub Actions bypass (true/false)
    bypass='{ "actor_id": 5, "actor_type": "RepositoryRole", "bypass_mode": "always" }'
    if [ "$4" = true ]; then
      bypass="$bypass, { \"actor_id\": 15368, \"actor_type\": \"Integration\", \"bypass_mode\": \"always\" }"
    fi
    gh api --method POST repos/crimsonking60/fetchshelf/rulesets --input - <<EOF
  {
    "name": "$1",
    "target": "branch",
    "enforcement": "active",
    "conditions": { "ref_name": { "include": ["refs/heads/$2"], "exclude": [] } },
    "bypass_actors": [ $bypass ],
    "rules": [
      { "type": "deletion" },
      { "type": "non_fast_forward" },
      { "type": "pull_request", "parameters": {
          "required_approving_review_count": 0,
          "require_code_owner_review": $3,
          "dismiss_stale_reviews_on_push": false,
          "require_last_push_approval": false,
          "required_review_thread_resolution": false } },
      { "type": "required_status_checks", "parameters": {
          "strict_required_status_checks_policy": false,
          "required_status_checks": [ { "context": "Build, test and format" } ] } }
    ]
  }
  EOF
  }
  ruleset "Protect develop" develop true true
  ruleset "Protect main" main false false
  ```

  `actor_id` 5 is the built-in Repository admin role, and the `Integration` with id 15368 is the GitHub
  Actions app. The required check is named `Build, test and format`
  on pull requests; on pushes the same job shows as `checks / Build, test and format`, because the develop
  image and release workflows call it. If the check on the first pull request stays at "Expected — Waiting
  for status to be reported", edit the check's name in **Settings → Rules → Rulesets** to match the name
  shown on the pull request.

### Security features

These are free for public repositories, under **Settings → Advanced Security** (named **Code security** on
some accounts).

- [ ] **You:** Turn on **Secret scanning** and **Push protection**, so a pushed token is caught.
- [ ] **You:** Turn on **Dependabot alerts** and **Dependabot security updates** for the NuGet packages and the
  workflow actions.
- [ ] **You:** Set up **CodeQL** code scanning with the default setup for C#.

### Actions from forks

- [ ] **You:** Under **Settings → Actions → General**, keep **Require approval for first-time contributors**
  (or stricter), so a stranger's first pull request cannot run workflows until you have looked at it.
  Fork pull requests only ever run the check workflow with a read-only token: the develop image and release
  workflows run on pushes to this repository only, and skip themselves anywhere else.

## 5. Check that it works

- [ ] **You:** In a private browser window, signed out: the repository, `README.md` and the guides display, and
  the links between them work.
- [ ] **You:** Without being logged in to the registry, the image can be pulled:

  ```sh
  docker logout ghcr.io
  docker pull ghcr.io/crimsonking60/fetchshelf:latest
  ```

- [ ] **You:** Open a small pull request into `develop`: the "Build, test and format" check runs and is shown as
  required, the pull request asks for code-owner review, and you can merge it by bypassing the rules.
- [ ] **You:** A direct push to `develop` still works, with a note that rules were bypassed.
- [ ] **You:** **Report a vulnerability** appears on the **Security** tab.

## 6. Follow up

- [ ] **Me:** Remove the "repository is private" wording from the contributors guide.
- [ ] **You:** Tell people about it, if and where you want to.
- [ ] **Me:** Delete this plan once everything in it is done, or keep it as a record.
