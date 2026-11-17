# Notifications by email — implementation plan

## Context

`[[notify]]` blocks (PR #46) POST to an HTTP address in one of five shapes: `json`, `ntfy`, `gotify`, `discord`,
`apprise`. Email was left out. Apprise can send email, but only through an Apprise API server run next to
Fetchshelf. For someone without ntfy or a chat service, email is the one channel they already have.

How notifications flow stays as it is: `INotificationSink.Publish` queues, `NotificationSender` merges bursts, and
`NotificationDelivery.SendToAsync` delivers to each target in the shape `NotificationFormats` builds. Email is one
more format with its own way of delivering.

## Change

### 1. Configuration

`format = "email"` in `[[notify]]`, with the server in `url`:

```toml
[[notify]]
name = "Mail"
url_file = "/run/secrets/fetchshelf_smtp"   # smtps://user:password@smtp.example.org:465
format = "email"
email_from = "Fetchshelf <fetchshelf@example.org>"
email_to = ["books@example.org"]
events = ["mam_refused", "job_failing", "job_recovered", "hit_and_run"]
```

- `url`: `smtp://` (port 587 by default, STARTTLS required) or `smtps://` (port 465 by default, TLS from the start),
  with an optional `user:password@`, percent-encoded. The URL is a secret like every `[[notify]]` URL: never logged,
  shown only as set.
- `email_from` (required with `email`) and `email_to` (one or more, required with `email`); refused with any other
  format.
- `smtp://` without STARTTLS on the server fails rather than sending in the clear; `email_insecure = true` allows it,
  for a relay on the same host or network.
- The URL check at load (`http`/`https` only today) accepts `smtp`/`smtps` for this format only.

### 2. The message

`NotificationFormats.Email(notification)` (Core, pure) builds subject and bodies:

- Subject: `[Fetchshelf] {title}`.
- Plain-text body: the message, the fields as `name: value` lines, the link when `web_url` is set.
- An HTML part with the same content, escaped, and the link as a link.
- A merged burst (several notifications at once) is one email listing them, subject "[Fetchshelf] 3 notifications".

### 3. Sending

- MailKit (MIT licence, the library Microsoft recommends instead of `System.Net.Mail.SmtpClient`) in
  `Fetchshelf.Data`, behind `IEmailSender` so tests don't open sockets.
- `NotificationDelivery.SendToAsync` sends email targets through it, with the same 10-second timeout and one retry
  after `RetryAfter` as HTTP targets. Failures are logged by target name, without the URL, as for the others.
- The Config page's **Send a test** works for email blocks too.

## Tests

- `NotificationFormatsTests`: subject, text and HTML bodies, escaping, a merged burst, no link without `web_url`.
- `NotifyConfigTests`: `smtp`/`smtps` URLs only with `email`; `email_from`/`email_to` required with it and refused
  without it; a malformed address; `email_insecure`.
- `NotificationDeliveryTests` with a fake `IEmailSender`: host, port, TLS mode, credentials and recipients from the
  URL and settings; retry once; the URL never in a log line.
- `NotifyConfigPageTests`: **Send a test** for an email block.

## Documentation

- `docs/configuration.md`, `[[notify]]` and "Notification formats": the `email` format, its settings, an example
  for a mail provider's SMTP server with an app password.
- `docs/user-guide.md`, "Getting notified": email.

## Release notes

- Added: `[[notify]]` blocks can send email (`format = "email"`) through an SMTP server.
- Upgrading: nothing to do.

## Out of scope

- A daily digest email of everything that happened.
- Sending without an SMTP server (direct to the recipient's mail server).
