# Security policy

## Supported versions

Only the latest release gets security fixes. Fixes ship as a new release, so update to it; see
[Updating Fetchshelf](docs/user-guide.md#updating-fetchshelf). The `edge` image is for testing and is not
supported.

## Reporting a vulnerability

Don't report security problems in public issues or discussions. Use the repository's **Security** tab and
choose **[Report a vulnerability](https://github.com/crimsonking60/fetchshelf/security/advisories/new)**, so
the report stays private until a fix is out.

Include:

- the Fetchshelf version (shown on the **Config** page and in the first log line);
- what the problem is and what an attacker could do with it;
- how to reproduce it, as precisely as you can.

Never include real secrets, such as a `mam_id`, passwords or tokens, not even your own.

The report stays private while it is looked into. Once a fix is released, the advisory is published.

## Not a vulnerability

Fetchshelf has no login, by design. Anyone who can open its web interface can use all of it, so it must only
be reachable from a trusted network, or through a reverse proxy that does authentication; see
[Security and privacy](docs/user-guide.md#security-and-privacy). Reports that come down to "the web interface
has no login" are not vulnerabilities. A way to reach or misuse Fetchshelf from somewhere the user didn't
expose it to is.
