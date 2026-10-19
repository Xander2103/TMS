# Single instance, health and operational signals

Status: production-hardening remediation 2026-10-19 (audit H1, H2, H5, H7, H12, LOG-01/02, CFG-04/05/06/11).

## 1. Rule for the pilot: exactly ONE API instance

The pilot runs **exactly one** `transportationservice-api` process against the database. No
blue/green overlap, no second systemd unit, no horizontal scale-out. The work below makes an
accidental second instance far less harmful, but several parts of the application are still
process-local (section 3), so one instance remains the supported topology.

## 2. What is now safe with more than one instance

| Area | Mechanism | Behaviour of a 2nd instance |
|---|---|---|
| Outbox mail/SMS (`OutboxDispatcherHostedService`) | Per-row claim: conditional `UPDATE … SET Status='Sending' WHERE Id=@id AND Status='Pending'`; outcome saved after **each** message | Can never send a row the other instance claimed. No global lock needed. |
| Startup sync (canary, backfills, permission catalogue, role templates; MIG-07) | PostgreSQL advisory lock `startup-sync` | Waits (up to 5 min) for the first instance, then re-runs the idempotent sync. After 5 min it refuses to start with an explicit message. |
| Sweeps: employment-end, task, expiry, inventory, notification maintenance, token retention, GDPR retention, attendance, calendar sync | Advisory lock per job (`pg_try_advisory_lock`) for the duration of one run | Skips the run ("Job … skipped: another instance holds its lock", Information). |
| Automatic daily backup (`AutomaticBackupHostedService`) | Advisory lock `automatic-backup` | Skips; only one `pg_dump` at 02:00 UTC. |

Lock implementation (`TransportationService.Api/Common/Jobs/JobLock.cs`):

- `DatabaseJobLock` takes a session-level advisory lock on a **dedicated, non-pooled**
  connection (`Pooling=false`, `Application Name=tms-job-lock:<job>`) that is held for the
  whole run and closed afterwards, so the lock can never stick to a pooled request connection.
  If the process dies, PostgreSQL releases the lock with the session.
- Lock key = first 8 bytes of `SHA-256("tms-job:<job name>")` (stable across processes and
  releases). Inspect held locks: `SELECT pid, application_name, objid FROM pg_locks JOIN
  pg_stat_activity USING (pid) WHERE locktype = 'advisory';`
- On a non-PostgreSQL provider (the SQLite test suite) the lock is a no-op.
- Peppol is NOT wrapped here; its dispatcher gets its own submission claim (separate change).

## 3. What is NOT multi-instance safe (process-local by design)

| Component | Why it matters with 2+ instances |
|---|---|
| Rate limiter (`AddAuthRateLimiting`, in-memory, per IP) | Each instance counts separately: limits multiply by N and reset on restart. Scale-out needs nginx `limit_req` or a shared store. |
| Kiosk interaction tokens (`KioskInteractionTokenStore`, in-memory) | A token issued by instance A is unknown to instance B. Needs sticky sessions or a shared store. |
| In-memory caches and the job heartbeat registry | Each instance has its own view; `/health/details` shows only the instance that answered. |
| Development fake calendar provider | Development only; outside Development the calendar provider is "not configured" (nothing is marked Synced). |
| Peppol dispatcher | Until its own claim lands, a second instance can double-submit (H3). |

## 4. Outbox delivery semantics (H2)

- **At most once per claim.** `Pending → Sending` is claimed atomically, then sent, then the
  outcome (`Sent`, retry `Pending` with backoff 5/10/20/40 min, `Failed` after 5 attempts,
  `Suppressed`) is saved immediately for that message.
- **Interrupted sends are not resent automatically.** A row still `Sending` 10 minutes after
  its claim (crash, SIGTERM during a deploy, DB lost right after the SMTP server accepted the
  mail) is moved to `AwaitingReview` with the reason *"Verzending onderbroken: het bericht is
  mogelijk al verzonden …"*. A dispatcher checks the SMTP log or asks the recipient and then
  uses the existing **Vrijgeven** (send again) or **Afwijzen** action. `AttemptCount` counts
  every claim, so repeated interruptions stay visible.
- Trade-off: at-least-once would resend automatically and risk duplicate invoice/POD mails to
  customers; at-most-once costs a human decision in the rare interrupted case. Every SMTP mail
  carries a stable `Message-ID: outbox-<row id>@<sender domain>`, so a released duplicate is
  recognisable downstream.
- **Poison rows.** Rows are read one by one after their claim. A row that cannot be read (for
  example an unknown `OwnerType`) is set to `Failed` with *"Ongeldige outboxrij …"* and the
  batch continues. A failing permanent-failure alert/fallback write no longer blocks the batch.
- **SMS without provider.** Outside Development no SMS provider exists: SMS rows are
  `Suppressed` with *"Sms-verzending is niet geconfigureerd …"* (no retries, no Critical alert).
- **Mail body (H12).** The rendered body is plain text; SMTP sends `multipart/alternative`
  with that text and an HTML part in which everything is HTML-encoded and line breaks become
  `<br>`. The invoice mails no longer promise an attachment; they point to the customer portal.

## 5. Health endpoints (H1)

| Endpoint | Access | Checks | Status codes |
|---|---|---|---|
| `GET /health/live` | anonymous | none (process answers) | always 200 |
| `GET /health/ready` | anonymous | only checks tagged `ready`: `database` (`SELECT 1`, 3 s), `migrations` (0 pending), `column-encryption` (key matches data), plus `storage` from the storage module | 200 when all Healthy, otherwise 503 |
| `GET /health/details` | authenticated + `system_info.view` | every check (tags `ready`, `outbox`, `jobs`, `integrations`, `peppol`, `backup`, …) with tags and scalar data, plus `jobs[]` heartbeats | 200 (status in body) |

Body of live/ready:

```json
{"status":"Healthy","totalDurationMs":37,"checks":[
  {"name":"database","status":"Healthy","description":"Database bereikbaar.","durationMs":34},
  {"name":"migrations","status":"Healthy","description":"Databaseschema is up-to-date.","durationMs":34},
  {"name":"column-encryption","status":"Healthy","description":"Kolomversleuteling: sleutel komt overeen met de database.","durationMs":28}]}
```

Bodies contain status, check names and short descriptions only. A check that throws is shown
as *"Controle mislukt."*; exception messages, connection strings and key material never appear.
The probes bypass authentication, the tenant guard, the account-state filter, every rate-limit
policy and the HTTPS redirect, so a loopback probe on `http://127.0.0.1:5019/health/ready` gets
the real answer. Adding a check from another module:

```csharp
builder.Services.AddHealthChecks().AddCheck<MyCheck>("my-check", tags: [HealthTags.Ready]); // gates readiness
builder.Services.AddHealthChecks().AddCheck<MyInfoCheck>("my-info", tags: [HealthTags.Peppol]); // details only
```

Details-only checks shipped here: `outbox` (Degraded when mail failed in the last 24 h, Pending
mail is older than 1 h, or interrupted sends wait for review; counts only), `jobs` (Degraded
when a job's last run failed), `integrations` (calendar/SMS: `NotConfigured`, `DevelopmentFake`,
`Configured`).

## 6. Column-encryption key canary (H5)

On every start, inside the startup lock, the API verifies that `ColumnEncryption:Key` (plus
`ColumnEncryption:PreviousKeys`) decrypts the canary row in `column_encryption_canaries` and a
sample of encrypted employee identifiers. A fresh database gets the canary written once with the
active key; after a rotation the canary is re-sealed with the new key. On a mismatch the API
**refuses to start** with an actionable message (which key to restore; the key is never logged)
and `/health/ready` reports `column-encryption` Unhealthy. Keep the key and all previous keys in
the vault next to (not inside) the backups.

## 7. Database outage → 503

When the database cannot be reached (Npgsql connection/IO failure, SQLSTATE `08xxx`,
`57P01–57P03`, `53300`, or EF's retry limit), the API answers
`503 {"title":"Service unavailable","code":"common.service_unavailable","correlationId":"…"}`
with `Retry-After: 5` instead of a generic 500. Constraint violations, SQL errors and every other
exception keep their normal handling.

## 8. Correlation ids and logging (LOG-01/02)

- The correlation id is the request's W3C trace id. It is returned as `correlationId` in every
  error body and in the `X-Correlation-Id` header of every response.
- Console logging includes scopes, so every log line of a request carries
  `TraceId:<correlation id>` and the request path; outside Development each event is one line
  (`journalctl -u transportationservice-api | grep <correlationId>` finds the request). JSON
  output: set `Logging__Console__FormatterName=json` (scopes included).
- One request line per 5xx (Warning) and per 401/403/429 (Information):
  `HTTP <method> <path> responded <status> in <ms> ms (correlationId …)`. The query string is never
  logged.
- `appsettings.Production.json` sets `Microsoft.EntityFrameworkCore`,
  `Microsoft.EntityFrameworkCore.Database.Command` and `Npgsql` to Warning: no SQL text at
  Information; errors are still logged.

## 9. Startup configuration rules (non-Development)

The API refuses to start when:

- `Frontend:BaseUrl` is missing, not an absolute `https://` URL, carries a query/fragment, or
  points at localhost/loopback or the demo domain `tms-demo.vanmalderstudio.be`;
- `Cors:AllowedOrigins` is empty, or an entry is not an `https://` origin (no path), or is
  loopback/demo;
- a `Network:KnownProxies` entry is not an IP address, or a `Network:KnownNetworks` entry is not
  a valid `address/prefix` network (previously skipped silently).

`appsettings.Production.json` no longer ships the demo domain: set `Frontend__BaseUrl` and
`Cors__AllowedOrigins__0` in `/etc/transportationservice/tms.env`. For a demo or a local
acceptance run in Production mode, `Deployment__AllowNonProductionUrls=true` relaxes ONLY the URL
rules (http, loopback, demo domain) and logs a startup warning; never set it for a real
installation. An empty `Network:KnownProxies` is a startup warning.

Optional integrations outside Development log one startup warning each when disabled:
calendar sync ("not configured": queue items stay Pending, never Synced) and SMS (suppressed).

## 10. Frontend API base URL (CFG-11)

`npm run build` (production mode) fails when `VITE_API_BASE_URL` is not set in the environment
or `.env.production`. The `http://localhost:5019` fallback applies only in development and tests.

## 11. Cookies and rate limiting (reviewed, unchanged)

- Refresh cookie `ts_refresh`: HttpOnly, SameSite=Strict, Path=/api/auth, `Secure` follows
  `Request.IsHttps`. That is correct only when nginx sends `X-Forwarded-Proto` from an address in
  `Network:KnownProxies` (now validated at startup). Verify `Set-Cookie … Secure` on the public
  domain after each nginx change.
- Rate limits stay in memory, per client IP: auth 10/min, session 30/min, webhook 60/min, kiosk
  60/min per device. One office behind one NAT shares the auth budget; with more than one
  instance limits multiply (section 3). No behaviour change in this release.
