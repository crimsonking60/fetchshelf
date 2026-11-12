# Allowed host names, and an optional login for OPDS — implementation plan

## Context

Fetchshelf has no login, by decision: it runs on a trusted network or behind a reverse proxy. Within
that, two openings are cheap to close.

- **DNS rebinding.** `appsettings.json` sets `AllowedHosts` to `*`. A web page open in a browser on the same
  network can point a name it controls at Fetchshelf's address and then use Fetchshelf's forms as if it were the
  user: it reads the page, so it has the antiforgery token too. ASP.NET Core's host filtering stops this when it
  knows which `Host` names are real.
- **OPDS without credentials.** `/opds` serves the ebook files to anyone who can reach it. Exposing the catalog to a
  phone outside the house today means exposing the whole UI. Every OPDS reader supports HTTP Basic login.

## Change

### 1. `allowed_hosts`

Top-level setting, a list of host names (no port), default empty:

```toml
allowed_hosts = ["fetchshelf.lan", "books.example.org"]
```

- Empty: as today, any `Host` is accepted.
- Set: `HostFilteringOptions.AllowedHosts` is the list plus `localhost`, `127.0.0.1` and `[::1]`, which the
  container's health probe (`--healthcheck`) and local access use. Entries may start with `*.` for a subdomain
  wildcard, as host filtering supports.
- A request with any other `Host` gets HTTP 400 from the built-in middleware, before any page runs.
- Validation at load: each entry is a host name or IP address without a scheme, port or path; an entry with any of
  them is refused with "allowed_hosts: write `fetchshelf.lan`, not `http://fetchshelf.lan:3157/`".
- `--check-config` lists them; the Config page shows them.
- `appsettings.json` keeps `"AllowedHosts": "*"`; the setting overrides it in `Program.cs` (in
  `AddFetchshelfWeb()` once `plans/composition-root.md` is done).

### 2. A login for `/opds`

In `[opds]`:

| Setting | Default | Description |
|---|---|---|
| `username` | unset | Required by OPDS readers when set, with `password`. |
| `password` | unset | A secret: hidden on the Config page, and readable from `password_file` like `mam_id_file`. |
| `password_file` | unset | Path of a file holding the password. Setting both is an error. |

- Both or neither of `username` and a password; one alone is an error at load.
- An endpoint filter on the `/opds` group checks `Authorization: Basic …` with a fixed-time comparison. Missing or
  wrong → 401 with `WWW-Authenticate: Basic realm="Fetchshelf OPDS"`. Nothing else in the app changes: the UI stays
  without a login.
- A wrong login is logged at Information with the remote address, never with what was sent.
- Environment overrides work as for other settings (`FETCHSHELF_CONF_opds_password_file`).

## Tests

- `AllowedHostsTests` (web fixture): with `allowed_hosts = ["fetchshelf.lan"]`, `Host: fetchshelf.lan` → 200,
  `Host: evil.example` → 400, `Host: localhost` → 200; without the setting, any host → 200.
- Config tests: entries with a scheme, port or path are refused; `*.example.org` is accepted.
- `OpdsLoginTests`: with a login set, no header → 401 with the challenge; wrong password → 401; right → 200; the UI
  still answers without a login. Without a login set, `/opds` answers as today.
- Config tests: username without password, both `password` and `password_file`, and the file being read.
- The Config page doesn't show the password.

## Documentation

- `docs/configuration.md`: `allowed_hosts` in the top-level table; `username`, `password`, `password_file` under
  `[opds]`.
- `docs/user-guide.md`, "Security and privacy" and "Reading on an e-reader (OPDS)": what `allowed_hosts` protects against in one
  paragraph; how to give an OPDS reader the login; that HTTP Basic sends the password readably, so outside the home
  network use HTTPS through a reverse proxy.

## Release notes

- Added: `allowed_hosts` makes Fetchshelf answer only to the host names it is reached by, which stops other web
  pages from using it through DNS rebinding.
- Added: an optional username and password for the OPDS catalog.
- Upgrading: nothing to do; both are off unless set.

## Out of scope

- A login for the UI, which stays without one by decision.
- More than one OPDS user, or per-user access to libraries.
