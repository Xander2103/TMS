# Production hardening — final remediation (2026-10-19)

Branch `remediation/production-hardening-final` on `integration/portal-security-final` (ad405ac2).
Source audit: `~/Documents/TMS-acceptance-evidence/production-readiness-20261018/REPORT.md`.
Drill evidence: `~/Documents/TMS-acceptance-evidence/production-hardening-20261019/`.
No real deploy; isolated local PostgreSQL 16 clusters and API processes only.

## Findings

| ID | Original | Root cause | Change | Test | Status |
|---|---|---|---|---|---|
| B1 | A deploy deletes every stored file | Storage hard-coded to `<ContentRoot>/App_Data`, inside the bin dir that `rsync --delete` replaces | `Storage:RootPath` (one resolver for uploads, backups, dev sink); non-Development refuses to start unless it is absolute, writable and outside the app dir; legacy `Storage:AllowContentRootStorage`; deploy refuses unsafe/legacy storage and excludes `App_Data/`; migration procedure `docs/storage.md`; `--verify-storage` | `StorageRootTests`, sim `storage-*` + `legacy-app-data`, drill start-failure cases, upgrade copy (35 files, checksums equal) | Fixed |
| B2 | Auto-rollback starts old code on the new schema | `on_error` restored and started previous binaries after migrations had committed | Forward-only: `RESTORE_PREVIOUS_ON_FAILURE=0` default, `=1` refused with pending migrations, never start previous binaries after the migration step, service stays stopped + maintenance on + unit disabled; restore-based rollback documented | sim `startup-never-ready`, `startup-crash`, `migration-failure`, `lock-timeout`, `db-ahead-of-code`, `restore-previous-pending` | Fixed |
| B3 | No way to set up a company in Production | Only Development seeders created tenant/admin | `--provision-tenant` (tenant, settings, Administrator, 12 role templates v40, reference data, legal entity, expiry policies, item templates; admin without password + activation mail; idempotent; no demo data); add-if-missing reference sync in every environment | `TenantProvisioningTests`; fresh install drill | Fixed |
| B4 | Peppol "Live" fakes delivery | Only the sandbox provider exists and Live was accepted | Live refused by the settings API without a live-capable provider; Live rows at startup → critical log, Peppol off for that entity, never submitted/polled/Delivered; UI "Live-provider niet geconfigureerd" | `PeppolProductionHardeningTests`; drill API 400 + planted-row restart; sim `app-peppol-live` | Fixed |
| H1 | No health endpoints | — | `/health/live`, `/health/ready` (database, migrations, key canary, storage), `/health/details` (auth): outbox, Peppol, backup, integrations, job heartbeats | `Hardening/*` tests; drill outage | Fixed |
| H2 | Mail can be sent twice | Batch of 100 saved once at the end, no claim | Atomic per-row claim (`ClaimToken`), save after each message, stale `Sending` → AwaitingReview (at-most-once), poison row quarantined | `OutboxClaimingTests`; agent PG run | Fixed |
| H3 | Peppol double submission | Send before guarded save | `Submitting` claim + idempotency key before the provider call; stale claims reconciled by key; polling fairness; webhook non-200 on store failure | `PeppolProductionHardeningTests` | Fixed |
| H4 | DB-only, unverified backups | Backup = `pg_dump` only, stderr dropped | Backup set = dump + storage archive + sha256 manifest, verified before Completed; failures recorded and logged; freshness health check + listing flags | `BackupServiceTests`; restore drill | Fixed |
| H5 | Wrong/missing key undetected | No key check | Startup + readiness canary; refuses to start on mismatch; key docs `docs/operations/encryption-keys.md` | `ColumnEncryptionCanaryTests`; drill wrong key / no key | Fixed |
| H6 | 30 s migration timeout, unbounded locks | Default command timeout, no lock timeout | Deploy migrates with `Command Timeout=0` + `lock_timeout` (60 s default), per-migration timings, preflight plate/VIN checks | sim `success`, `lock-timeout` | Fixed |
| H7 | Single-instance assumption unsafe/undocumented | No locks around jobs/startup | PostgreSQL advisory locks for startup sync, sweeps and backup; per-row outbox/Peppol claims; `docs/operations/single-instance.md`: **pilot = exactly one instance** | `Hardening/*`; agent PG lock test | Fixed (single instance documented) |
| H8 | In-app restore on the live DB | `pg_restore --clean` from an HTTP request | Restore 409 outside Development; offline runbook; download only for the system Administrator | `BackupServiceTests`; drill 409 | Fixed |
| H9 | Password-reset mail never sent | Production path was a stub | Activation and reset links through the outbox; outbox detail hides their bodies | `AccountMailDeliveryTests` | Fixed |
| H10 | Secrets in builds; default deploy ref | `tms.env` exported for npm/dotnet; default `nav-redesign` | env only for pg_dump/dotnet-ef/psql; ref mandatory | sim `success` (no tms.env keys in build envs), `no-ref` | Fixed |
| H11 | Pre-deploy backup not final | Dump before `systemctl stop` | Stop/maintenance → final dump + storage snapshot + verify → migrate | sim `success` (dump with service inactive) | Fixed |
| H12 | Unsafe/misleading customer mail | Plain text as HTML; attachment promised | text/plain + HTML-encoded body; invoice wording | `SmtpEmailBodyTests` | Fixed |

Also: NU1903/NU1902 (`System.Security.Cryptography.Pkcs` pinned 10.0.13, no advisory in the build);
DB outage → 503 + `Retry-After` + correlation id (connection failures only); correlation id in every
log line of the request; EF SQL at Warning in Production; demo URLs removed and http/localhost/demo
refused outside Development (`Deployment:AllowNonProductionUrls` for local/demo only); calendar/SMS
honestly disabled instead of fake success; production web build refuses a missing
`VITE_API_BASE_URL`.

## Drills (2026-10-19)

- **Fresh production-like install** (empty DB, owner `tms`, Production, no dev seeders): 193
  migrations in 17 s; provisioning → 1 tenant, 1 admin (no password), 13 roles at v40, no demo
  data, activation mail queued; rerun idempotent; activation link single-use; `admin@dev.local`
  refused; customer, employee (encrypted national number), vehicle, dossier created: 9/9.
- **Upgrade** of an ad405ac2 database with real documents: 2 new migrations applied; legacy
  `App_Data` copied to the durable root (35 files, checksums equal); start ready; `--verify-storage`
  43 references, 5 pre-existing missing signatures.
- **Backup/restore** (new architecture): in-app set (dump + archive + manifest) → new cluster, new
  owner role, new storage root: checksums OK, `pg_restore` OK, 35 files; wrong key → refused to start;
  same key → ready; POD photo, CMR, invoice PDF, UBL, customer document byte-identical; encrypted
  national number identical; integrity identical to the source.
- **Deployment simulation:** see `scripts/deploy-sim/` and the evidence folder.

## Known residuals

- Scale-out is not supported: in-memory rate limiter and caches; run exactly one API instance.
- No real Peppol Access Point and no real calendar/SMS provider (honestly disabled).
- Uploads are not malware-scanned (pass-through seam); employee IBAN plaintext at rest; medical files
  unencrypted on disk; activation/reset links readable in the DB outbox until scrubbed.
- Restore remains an offline operator procedure (by design).
