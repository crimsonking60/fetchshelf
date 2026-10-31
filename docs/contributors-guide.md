# Fetchshelf contributors guide

> **Work in progress.** This guide is incomplete and may change.

This guide is for volunteers who want to help develop Fetchshelf. It covers getting the code, setting up a
Linux development machine, building and testing, and the fork, branch, commit and pull request workflow on
GitHub. If you only want to run Fetchshelf, read the [user guide](user-guide.md) instead.

## Contents

- [Ways to contribute](#ways-to-contribute)
- [Requirements](#requirements)
- [Setting up a development machine](#setting-up-a-development-machine)
  - [Debian and Ubuntu (apt)](#debian-and-ubuntu-apt)
  - [Fedora, RHEL, AlmaLinux and Rocky Linux (dnf)](#fedora-rhel-almalinux-and-rocky-linux-dnf)
  - [Any distribution: the .NET install script](#any-distribution-the-net-install-script)
  - [Signing in to GitHub with gh](#signing-in-to-github-with-gh)
  - [Editors and IDEs](#editors-and-ides)
- [Getting the code](#getting-the-code)
- [Building](#building)
- [Testing](#testing)
- [Formatting and code analysis](#formatting-and-code-analysis)
- [Running Fetchshelf locally](#running-fetchshelf-locally)
- [Running in Docker](#running-in-docker)
- [How the code is organized](#how-the-code-is-organized)
- [Coding conventions](#coding-conventions)
- [Database changes and migrations](#database-changes-and-migrations)
- [Working with MaM responsibly](#working-with-mam-responsibly)
- [Branches](#branches)
- [Commits](#commits)
- [Pushing](#pushing)
- [Pull requests](#pull-requests)
- [Keeping your fork up to date](#keeping-your-fork-up-to-date)
- [GitHub: issues and discussions](#github-issues-and-discussions)
- [gh command reference](#gh-command-reference)
- [Secrets and privacy](#secrets-and-privacy)
- [License of contributions](#license-of-contributions)

## Ways to contribute

You don't have to write code to help:

- **Report bugs** you hit while running Fetchshelf. See [Issues](#issues).
- **Suggest features** or discuss ideas in [Discussions](#discussions).
- **Improve the documentation**: the [user guide](user-guide.md), the [configuration reference](configuration.md)
  and this guide.
- **Fix bugs or build features.** Issues labelled `good first issue` are a good place to start, and
  `help wanted` marks work the maintainers would especially like help with.
- **Review pull requests** from other contributors and test their changes against your own setup.

For anything larger than a small fix, open an issue or a discussion first. A quick "I'd like to work on this,
here is my plan" saves you from building something that doesn't fit.

## Requirements

| Tool | Version | Needed for |
| --- | --- | --- |
| Linux | Any current distribution | Development. Fetchshelf creates hard links through the Linux `link()` call, and its deployment target is Linux in Docker. |
| .NET SDK | 10.0 | Building, testing and running. All projects target `net10.0`. |
| Git | 2.30 or later | Version control. |
| GitHub CLI (`gh`) | 2.40 or later | Forking, pull requests and issues from the terminal. Optional but recommended; this guide uses it throughout. |
| A GitHub account | | Forking and pull requests. |
| qBittorrent | A version MaM currently allows, with the Web UI enabled | Optional. Needed to try downloading and linking end to end. `qbittorrent-nox` (no GUI) is enough. MaM only allows certain client versions and changes the list over time, so check MaM's current list of approved clients before you install one. A distribution's package can be too new or too old. |
| SQLite command-line tool | 3.x | Optional. Handy for looking inside the database. |
| Docker with Compose | Current | Optional. Needed only to test the container image. |
| A MyAnonamouse account | | Optional. Needed only for features that talk to MaM. Much of the web UI, and all tests, work without one. |

Windows and macOS are not supported as development platforms at the moment. WSL2 with a Linux distribution
works the same as the Linux instructions below.

## Setting up a development machine

### Debian and Ubuntu (apt)

Install Git, the GitHub CLI and the optional tools:

```sh
sudo apt update
sudo apt install git gh sqlite3 qbittorrent-nox
```

Before you use `qbittorrent-nox` with your MaM account, check its version (`qbittorrent-nox --version`)
against MaM's current list of approved clients. See [Requirements](#requirements).

Install the .NET 10 SDK.

- **Ubuntu 25.10 and later** ship it in the Ubuntu archive:

  ```sh
  sudo apt install dotnet-sdk-10.0
  ```

- **Ubuntu 24.04 LTS** gets it from the Ubuntu .NET backports archive:

  ```sh
  sudo add-apt-repository ppa:dotnet/backports
  sudo apt update
  sudo apt install dotnet-sdk-10.0
  ```

- **Debian 12 and 13** get it from Microsoft's package repository. Replace `13` with `12` on Debian 12:

  ```sh
  wget https://packages.microsoft.com/config/debian/13/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
  sudo dpkg -i packages-microsoft-prod.deb
  rm packages-microsoft-prod.deb
  sudo apt update
  sudo apt install dotnet-sdk-10.0
  ```

If `apt` can't find `dotnet-sdk-10.0` on your release, use the
[.NET install script](#any-distribution-the-net-install-script) instead.

Docker (optional):

```sh
sudo apt install docker.io docker-compose-v2   # Ubuntu
sudo apt install docker.io docker-compose      # Debian
sudo usermod -aG docker "$USER"                # then log out and back in
```

### Fedora, RHEL, AlmaLinux and Rocky Linux (dnf)

Install Git, the GitHub CLI and the optional tools:

```sh
sudo dnf install git gh sqlite qbittorrent-nox
```

Before you use `qbittorrent-nox` with your MaM account, check its version (`qbittorrent-nox --version`)
against MaM's current list of approved clients. See [Requirements](#requirements).

On RHEL, AlmaLinux and Rocky Linux, `gh` and `qbittorrent-nox` come from outside the base repositories:
enable [EPEL](https://docs.fedoraproject.org/en-US/epel/) for `qbittorrent-nox`, and add GitHub's own package
repository for `gh`:

```sh
sudo dnf install 'dnf-command(config-manager)'
sudo dnf config-manager --add-repo https://cli.github.com/packages/rpm/gh-cli.repo
sudo dnf install gh
```

Install the .NET 10 SDK. Current Fedora releases and RHEL-compatible releases with .NET 10 in their
AppStream repository ship it as a regular package:

```sh
sudo dnf install dotnet-sdk-10.0
```

If `dnf` can't find `dotnet-sdk-10.0` on your release, use the
[.NET install script](#any-distribution-the-net-install-script) instead.

Docker (optional). On Fedora:

```sh
sudo dnf install moby-engine docker-compose
sudo systemctl enable --now docker
sudo usermod -aG docker "$USER"                # then log out and back in
```

On RHEL-compatible systems, install Docker from [Docker's own repository](https://docs.docker.com/engine/install/).

### Any distribution: the .NET install script

Microsoft's install script puts the SDK in your home directory and works on every distribution. It doesn't
need root, but it doesn't update itself either; run it again to upgrade.

```sh
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
bash dotnet-install.sh --channel 10.0
rm dotnet-install.sh
```

Then add it to your shell's `PATH`, for example in `~/.bashrc`:

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
```

Check the installation, whichever way you installed it:

```sh
dotnet --list-sdks     # should list a 10.0.x SDK
```

### Signing in to GitHub with gh

```sh
gh auth login          # choose GitHub.com, HTTPS, and sign in with your browser
gh auth setup-git      # lets git push over HTTPS with the same credentials
gh auth status         # confirm you are signed in
```

Also tell Git who you are, if you haven't already. Use the name and email you want on your commits; GitHub's
`<id>+<username>@users.noreply.github.com` address keeps your real email private.

```sh
git config --global user.name "Your Name"
git config --global user.email "you@example.com"
```

### Editors and IDEs

Any editor works. JetBrains Rider and Visual Studio Code with the C# Dev Kit extension both open
`Fetchshelf.sln` directly and pick up the repository's `.editorconfig`, so their formatting matches the
project's. Keep your editor's formatting on default settings rather than your own preferences.

## Getting the code

Fetchshelf lives at <https://github.com/crimsonking60/fetchshelf>. Contributors work in their own fork and
send changes back as pull requests.

Fork and clone in one step:

```sh
gh repo fork crimsonking60/fetchshelf --clone
cd fetchshelf
git remote -v
```

`gh` sets up two remotes: `origin` is your fork, which you push to, and `upstream` is the main repository,
which you pull from.

If you only want to look around without contributing, a plain clone is enough:

```sh
gh repo clone crimsonking60/fetchshelf
# or
git clone https://github.com/crimsonking60/fetchshelf.git
```

After cloning, restore the repository's .NET tools (at the moment just `dotnet-ef`, for database
migrations):

```sh
dotnet tool restore
```

## Building

From the repository root:

```sh
dotnet build
```

The build doubles as the lint step. Every project has nullable reference types, the .NET analyzers and
`TreatWarningsAsErrors` turned on (see `Directory.Build.props`), so any compiler or analyzer warning fails
the build. A pull request must build with **0 warnings and 0 errors**.

## Testing

Run every test:

```sh
dotnet test
```

Run one test class or a single test:

```sh
dotnet test --filter "FullyQualifiedName~SeriesParserTests"
dotnet test --filter "FullyQualifiedName~SeriesParserTests.Parse_DecimalEntries_ParseAsNumbers"
```

Run the tests of one project only:

```sh
dotnet test tests/Fetchshelf.Data.Tests
```

The tests need no network, no MaM account and no qBittorrent: HTTP clients are tested against fake message
handlers, and database tests use temporary SQLite files. If you find yourself needing a live service in a
test, that is a sign the logic should move somewhere it can be tested on its own.

Tests use xUnit (`[Fact]`, and `[Theory]` with `[InlineData]`). Name them
`Method_Situation_ExpectedResult`, for example `RunAsync_SameTorrentFailsOnEveryRun_KeepsOneErrorRow`.
Every bug fix should come with a test that fails without the fix.

## Formatting and code analysis

Formatting is checked separately from the build:

```sh
dotnet format --verify-no-changes   # check, as a pull request is checked
dotnet format                       # fix everything it can
```

Before every push, run all three checks:

```sh
dotnet build && dotnet test && dotnet format --verify-no-changes
```

## Running Fetchshelf locally

Fetchshelf reads one configuration file and keeps its state in one SQLite database. Point it at files
outside the repository so you never commit them by accident:

```sh
mkdir -p ~/fetchshelf-dev
cp docs/config-examples/1-minimal.toml ~/fetchshelf-dev/config.toml
```

Edit `~/fetchshelf-dev/config.toml`. The [configuration reference](configuration.md) describes every
setting, and `docs/config-examples/` has six worked examples, from minimal to complete.

- **UI-only work, no MaM account:** set `mam_id = ""`. The web UI runs, but no background job starts.
- **Full end-to-end work:** set your own `mam_id`, point `[[qbittorrent]]` at a local qBittorrent, and use
  library and download folders inside `~/fetchshelf-dev`.

Start it:

```sh
export ASPNETCORE_ENVIRONMENT=Development
export FETCHSHELF_CONFIG_FILE=~/fetchshelf-dev/config.toml
export FETCHSHELF_DATA_FILE=~/fetchshelf-dev/data.db
export FETCHSHELF_LOG=Information
dotnet run --project src/Fetchshelf.Web
```

Then open <http://localhost:3157> (or whatever `web_port` you set). The database is created and migrated
automatically on first start.

| Variable | Meaning | Default |
| --- | --- | --- |
| `FETCHSHELF_CONFIG_FILE` | Path to `config.toml`. | A platform-specific config path, else `config.toml` in the working directory. |
| `FETCHSHELF_DATA_FILE` | Path to the SQLite database. | `data.db` in the working directory. |
| `FETCHSHELF_LOG` | Minimum log level: `Verbose`, `Debug`, `Information`, `Warning`, `Error`. | `Debug` |
| `FETCHSHELF_LOG_DIR` | If set, also write daily log files to this folder. | Console only. |

Logs go to the terminal (stderr). Database queries and per-request framework messages are only logged at
`Warning` and above, so the output stays readable.

## Running in Docker

To check that your change works in the container image as well:

```sh
docker compose up --build
```

The Compose file builds the image from the repository and keeps its configuration and data in
`docker-data/` (ignored by Git). Put a `config.toml` in `docker-data/config/` before the first start. Inside
the container, library and download paths must be the same as on the host for hard links to work; see the
[user guide](user-guide.md) for details.

## How the code is organized

The solution is split along two lines: domain logic versus input/output, and the library organizer versus
the optional auto-downloader. Dependencies point one way, from the web host down to the domain.

| Project | Contains |
| --- | --- |
| `Fetchshelf.Core` | Pure domain logic with no I/O: metadata normalization and duplicate detection, series parsing, folder naming, library matching, the in-memory job registry. |
| `Fetchshelf.Core.AutoDownloader` | Pure auto-downloader logic: cost policy, selection budget and ranking, torrent filter matching, tag rules, format preferences. |
| `Fetchshelf.Clients.Mam` | HTTP client for MyAnonamouse. |
| `Fetchshelf.Clients.QBittorrent` | HTTP client for the qBittorrent Web API. |
| `Fetchshelf.Clients.Abs` | HTTP client for Audiobookshelf. |
| `Fetchshelf.Clients.Notion` | Notion list reading. |
| `Fetchshelf.Data` | EF Core database context, entities and migrations; configuration loading; the event log; the library organizer jobs (link, clean, Audiobookshelf matching). |
| `Fetchshelf.Data.AutoDownloader` | Auto-downloader jobs that need the database and clients: autograbbers, selection, downloading, list imports. |
| `Fetchshelf.Web` | The ASP.NET Core host: dependency injection setup and background job scheduling in `Program.cs`, and the Razor Pages UI under `Pages/`. |

Each `src` project has a matching test project under `tests/` once it has tests.

A few things are useful to know before you change them:

- **Configuration** is TOML only. Setting names are part of the user-facing contract: don't rename them,
  and remember that unknown keys are rejected on purpose. Document every new setting in
  [configuration.md](configuration.md).
- **Background jobs** are registered by name in the job registry in `Program.cs`. One runner class per job
  does the work, and a scheduler triggers it on its interval. An interval of `0` means "only when started
  from the UI".
- **Only one job talks to MaM at a time.** Any new code that calls the MaM client from a job must take the
  `MamAccessGate`.
- **The event log is append-only.** Events are never updated or deleted. To record a correction, append a
  new event.
- **The web UI** is server-rendered Razor Pages with htmx for in-place updates. It is not a single-page app,
  and it has no authentication: Fetchshelf is meant for a trusted network or behind a reverse proxy.

## Coding conventions

- Follow `.editorconfig`; `dotnet format` enforces it. Namespaces are file-scoped, and `if`/`else` bodies
  always have braces.
- Keep domain logic in the `Core` projects as plain, testable code, and keep I/O (database, HTTP, files) at
  the edges. Prefer testing a pure function directly over mocking EF Core or HTTP.
- Write comments only where the reason behind the code isn't obvious: a hidden constraint, a workaround, a
  surprising rule. Don't describe what well-named code already says.
- Keep changes focused. A bug fix doesn't need surrounding clean-up; send that separately.
- Don't add new dependencies (NuGet packages, front-end libraries) without discussing them in the issue or
  pull request first.
- User-visible text should be plain and specific. Look at the existing page explanations in the UI for the
  tone.

## Database changes and migrations

Fetchshelf upgrades its database in place at startup, and a database written by any older version must keep
working. When you change an entity or the database context:

1. Make the model change.
2. Add a migration with a descriptive name:

   ```sh
   dotnet ef migrations add AddSomethingToTorrents --project src/Fetchshelf.Data --startup-project src/Fetchshelf.Data
   ```

3. Run `dotnet format`. The generated files don't follow the repository's style until you do.
4. Read the generated migration. If existing rows need converting, or would break a new constraint, add the
   SQL that fixes them before that step.
5. Add a test that upgrades a database from the previous migration with realistic data in it.

Never edit a migration that is already on `develop` or `main`. Add a new one instead.

## Working with MaM responsibly

MyAnonamouse is a private tracker with rules about automated access, and Fetchshelf runs against real
people's accounts. When you work on anything that talks to MaM:

- Keep the existing politeness: rate-limit back-off, pauses between search pages, and one MaM job at a time.
- Test with the fake HTTP handlers in `tests/Fetchshelf.Clients.Mam.Tests`, not with repeated live
  requests.
- When you do try a change live, use your own account, make as few requests as you can, and stop at the
  first sign of rate limiting.
- Never weaken the ratio, unsatisfied-slot or wedge safety checks. They protect users' accounts.

## Branches

The main repository has two long-lived branches. `develop` is where pull requests are merged and where the
next release takes shape; it is the default branch. `main` holds the latest release and only changes when a
new version is released (see the [maintainer guide](maintainer-guide.md)). Contributors only need `develop`.

Never work directly on `develop`, not even in your fork. Keep your fork's `develop` as a clean copy of the main
repository's and create one branch per change from it:

```sh
git switch develop
git pull upstream develop
git switch -c fix/123-errors-page-duplicates
```

Name branches `<kind>/<issue number>-<short-description>`, where the issue number is optional:

| Kind | For |
| --- | --- |
| `fix/` | Bug fixes. |
| `feature/` | New behavior. |
| `docs/` | Documentation only. |
| `refactor/` | Restructuring without behavior changes. |
| `test/` | Tests only. |

One branch is one pull request. Unrelated changes go in separate branches.

## Commits

Make each commit one logical change that builds and passes its tests on its own. A feature with its tests is
one commit; a feature plus an unrelated formatting clean-up is two.

Write commit messages like the existing history (`git log` shows plenty of examples):

- **Subject line:** imperative mood, at most about 70 characters, no trailing period. "Keep one error per
  torrent and job", not "Kept…" or "Keeps…".
- **Blank line**, then a **body** wrapped at about 72 characters that explains *why* the change is needed and
  what it does differently. The diff already shows *how*.
- Mention user-visible effects, and anything a reviewer should look at closely, such as a migration or a
  changed default.
- Refer to issues in the body: `Fixes #123` closes the issue when the pull request is merged.

```text
Keep one error per torrent and job on the Errors page

Link added a new error row on every failed run and never cleared one,
so a torrent that kept failing piled up identical rows. A repeat
failure now updates the existing row, and the row is cleared once the
torrent links.

Fixes #123
```

Stage specific files rather than everything at once, and check what you are about to commit:

```sh
git status
git add src/Fetchshelf.Data/Organizer/LinkRunner.cs tests/Fetchshelf.Data.Tests/LinkRunnerTests.cs
git diff --staged
git commit
```

Never commit configuration files with real values, databases (`*.db`), logs or anything from `docker-data/`.

## Pushing

Push your branch to your fork (`origin`), never to `upstream`:

```sh
git push -u origin fix/123-errors-page-duplicates
```

After the first push, a plain `git push` is enough. If you rebased your branch (see
[Keeping your fork up to date](#keeping-your-fork-up-to-date)), push with `--force-with-lease`. It refuses to
overwrite commits on your branch that you haven't seen yet, unlike a plain `--force`:

```sh
git push --force-with-lease
```

## Pull requests

### Before you open one

- `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` all pass.
- New behavior and bug fixes have tests.
- User-facing changes are documented in the [user guide](user-guide.md) or the
  [configuration reference](configuration.md).
- Database changes have a migration that upgrades existing databases (see
  [Database changes and migrations](#database-changes-and-migrations)).
- Your branch is up to date with `upstream/develop`.
- You haven't edited `RELEASES.md` or the "Latest release" section of `README.md`. Maintainers write those when
  they prepare a release, from your pull request's description.

### Opening one

From your branch, after pushing:

```sh
gh pr create --repo crimsonking60/fetchshelf --base develop --fill
```

`--fill` uses your commit messages for the title and description. Edit them if the pull request has more
than one commit. To write the description yourself:

```sh
gh pr create --repo crimsonking60/fetchshelf --base develop \
  --title "Keep one error per torrent and job on the Errors page" \
  --body-file pr-description.md
```

Open a **draft** pull request to get early feedback on work in progress, and mark it ready when it is:

```sh
gh pr create --repo crimsonking60/fetchshelf --base develop --fill --draft
gh pr ready
```

To finish the pull request in the browser instead, add `--web`.

A good description covers the following. The pull request template has a section for each, and the checklist
from [Before you open one](#before-you-open-one):

- **What and why:** the problem, and how the change solves it.
- **Issue:** `Fixes #123` or `Refs #123`.
- **How you tested it:** the automated tests you added, and what you tried by hand.
- **Screenshots** of the before and after for UI changes, in both a light and a dark theme.
- **Anything reviewers should know:** migrations, changed defaults, new dependencies, open questions.

### Review

```sh
gh pr status                 # your pull requests and their review state
gh pr view --web             # open the current branch's pull request in the browser
gh pr checks                 # the automated build, test and format checks
gh pr view --comments        # read review comments in the terminal
```

GitHub asks the code owners listed in `.github/CODEOWNERS` to review automatically, so you don't need to pick
reviewers yourself.

Answer review comments by pushing more commits to the same branch. The pull request updates itself. Reply to
each comment, or resolve it, so the reviewer knows it was handled. If a discussion stalls, say what you need
to move forward.

### Reviewing someone else's pull request

Anyone can review. Check out the pull request locally, build, test and try it:

```sh
gh pr checkout 42
dotnet build && dotnet test
gh pr review 42 --comment --body "Tried this against my library; works, one question inline."
gh pr review 42 --approve
gh pr review 42 --request-changes --body "The migration drops existing rows; see inline comment."
```

Maintainers merge approved pull requests into `develop`, squashing each into one commit. The
[maintainer guide](maintainer-guide.md) describes merging and releasing.

### After the merge

```sh
git switch develop
git pull upstream develop
git push origin develop
git branch -d fix/123-errors-page-duplicates
git push origin --delete fix/123-errors-page-duplicates
```

## Keeping your fork up to date

Bring your fork's `develop` up to date with the main repository:

```sh
gh repo sync <your-username>/fetchshelf --branch develop   # updates your fork on GitHub
git switch develop
git pull origin develop                                    # updates your local develop
```

or, with Git alone:

```sh
git switch develop
git pull upstream develop
git push origin develop
```

To bring a branch you are working on up to date, rebase it on the new `develop`, then force-push it:

```sh
git switch fix/123-errors-page-duplicates
git rebase develop
# fix any conflicts, then: git add <files> && git rebase --continue
dotnet build && dotnet test
git push --force-with-lease
```

## GitHub: issues and discussions

### Issues

Issues are for **bugs** and **concrete, actionable work**: something is broken, or a specific change has been
agreed on. Open-ended questions and ideas belong in [Discussions](#discussions).

Before opening an issue, search existing ones, including closed issues:

```sh
gh issue list --repo crimsonking60/fetchshelf --search "errors page"
gh issue list --repo crimsonking60/fetchshelf --state all --search "flac"
```

A good bug report has:

- the Fetchshelf version or commit (`git rev-parse --short HEAD` if you built it yourself), and how you run
  it (Docker or from source);
- what you did, what you expected, and what happened instead;
- the relevant log lines;
- the relevant part of your `config.toml`, **with secrets removed** (see [Secrets and privacy](#secrets-and-privacy)).

Create an issue from the terminal:

```sh
gh issue create --repo crimsonking60/fetchshelf --label bug \
  --title "Musicology torrents with flac files are not linked" \
  --body-file bug-report.md
```

or open the form in your browser with `gh issue create --repo crimsonking60/fetchshelf --web`. In the browser,
the **Bug report** and **Feature request** forms ask for everything above; questions and ideas are sent to
Discussions, and security problems to private vulnerability reporting.

Labels in use:

| Label | Meaning |
| --- | --- |
| `bug` | Something isn't working. |
| `enhancement` | New feature or request. |
| `documentation` | Improvements or additions to documentation. |
| `accessibility` | A barrier affecting people with disabilities. |
| `question` | More information is needed. |
| `good first issue` | Suitable for a first contribution. |
| `help wanted` | The maintainers would especially welcome help. |
| `duplicate`, `invalid`, `wontfix` | Reasons an issue was closed without a change. |

Find something to work on, and claim it before you start so two people don't do the same work:

```sh
gh issue list --repo crimsonking60/fetchshelf --label "good first issue"
gh issue view 123 --repo crimsonking60/fetchshelf --comments
gh issue comment 123 --repo crimsonking60/fetchshelf --body "I'd like to work on this. Plan: ..."
```

### Discussions

[Discussions](https://github.com/crimsonking60/fetchshelf/discussions) are for everything that isn't yet a
concrete task:

- questions about using or developing Fetchshelf;
- feature ideas you'd like feedback on before anyone writes code;
- design proposals for larger changes;
- sharing setups and configurations that work well.

When a discussion turns into agreed, concrete work, a maintainer converts it into an issue or opens one that
links back to it.

`gh` has no built-in command for discussions, so use the web page. To jump to it from the terminal:

```sh
gh repo view crimsonking60/fetchshelf --web
```

then open the **Discussions** tab.

### Security problems

Don't report security problems in public issues or discussions. Use the repository's **Security** tab and
choose **Report a vulnerability**, so the report stays private until a fix is out.

## gh command reference

| Task | Command |
| --- | --- |
| Sign in | `gh auth login`, then `gh auth setup-git` |
| Fork and clone | `gh repo fork crimsonking60/fetchshelf --clone` |
| Update your fork on GitHub | `gh repo sync <your-username>/fetchshelf --branch develop` |
| Open the repository in the browser | `gh repo view crimsonking60/fetchshelf --web` |
| List issues | `gh issue list --repo crimsonking60/fetchshelf` |
| Search issues | `gh issue list --repo crimsonking60/fetchshelf --search "text"` |
| Read an issue | `gh issue view 123 --repo crimsonking60/fetchshelf --comments` |
| Create an issue | `gh issue create --repo crimsonking60/fetchshelf --label bug --title "..." --body-file report.md` |
| Comment on an issue | `gh issue comment 123 --repo crimsonking60/fetchshelf --body "..."` |
| Create a pull request | `gh pr create --repo crimsonking60/fetchshelf --base develop --fill` |
| Create a draft pull request | `gh pr create --repo crimsonking60/fetchshelf --base develop --fill --draft` |
| Mark a draft ready | `gh pr ready` |
| Your pull requests | `gh pr status` |
| View the current pull request | `gh pr view --web` |
| Check out someone's pull request | `gh pr checkout 42` |
| Review a pull request | `gh pr review 42 --approve` / `--comment` / `--request-changes` |
| See a pull request's diff | `gh pr diff 42` |

`gh <command> --help` lists every option.

## Secrets and privacy

Fetchshelf's configuration holds credentials. Never commit or post any of these, in code, issues, pull
requests, discussions, screenshots or logs:

- `mam_id` (your MaM session);
- qBittorrent usernames and passwords;
- Audiobookshelf and Notion tokens.

Replace them with placeholders such as `replace-with-your-mam-session-id` before you share a config. Also
check logs and screenshots for private paths, usernames and library contents you'd rather not publish.

If you accidentally push a secret, revoke it straight away: create a new MaM session, change the password,
or rotate the token. Removing it from Git history afterwards does not make it safe again.

## License of contributions

Fetchshelf is released under the [MIT License](../LICENSE). By submitting a pull request, you agree that your
contribution is released under the same license.
