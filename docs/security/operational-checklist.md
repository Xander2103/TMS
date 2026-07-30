# Operationele securitychecklist — buiten de repository

Deze punten zijn **niet** door code op te lossen en worden daarom **niet** als opgelost gemarkeerd.
Elk item: huidige status (voor zover bekend uit de repo), waarom het niet statisch verifieerbaar
is, concrete verificatiestappen, aanbevolen eigenaar, en of het release-blocking is.

Legenda eigenaar: **Ops** = platform/infra · **Sec** = security officer · **Dev** = engineering ·
**Legal** = DPO/juridisch.

| # | Item | Huidige status (repo) | Waarom niet statisch | Verificatiestappen | Eigenaar | Release-blocking |
|---|---|---|---|---|---|---|
| 1 | `ASPNETCORE_ENVIRONMENT` per omgeving | Code gated correct; waarde onbekend | Runtime-env, niet in repo | Bevestig `Production`/`Staging` op elke non-dev host; `StartupSecurityValidator` faalt nu bij fout | Ops | **Ja** |
| 2 | Productie-secretbron (JWT key, connstring, webhook secret) | Config leeg; bron onbekend | Vault/env buiten repo | Bevestig secrets uit key vault/env; geen `appsettings.*` met echte waarden in publish | Ops/Sec | **Ja** |
| 3 | JWT signing-key **rotatie** (oude dev-key gecompromitteerd) | Key in git-history; deny-listed in code | History-rewrite is destructief | Roteer key; overweeg `git filter-repo`; invalideer bestaande sessies | Sec | **Ja** |
| 4 | Git-history purge van gecommitte secrets | Aanwezig in history | Destructieve rewrite buiten sprintscope | `git filter-repo` gecoördineerd; force-push; her-clone door team | Sec/Dev | Nee (mitigatie via rotatie+deny-list) |
| 5 | Reverse proxy + known-proxy IP's | Geen proxyconfig in repo | Infra | Configureer `UseForwardedHeaders` KnownProxies (Fase 5) + edge | Ops | Ja (voor rate limiting) |
| 6 | TLS-terminatie & certificaten | Alleen `UseHttpsRedirection` (non-dev) | Edge/infra | Bevestig TLS 1.2+/ciphers; cert-rotatie | Ops | **Ja** |
| 7 | HSTS op edge | Nog niet in app (Fase 5) | Edge kan het ook zetten | Bevestig HSTS-header (+ preload) op edge of app | Ops | Ja |
| 8 | Database-encryptie at rest / TDE | Geen bewijs in repo | Infra/DB | Bevestig TDE/schijfversleuteling | Ops | Ja (voor bijzondere data) |
| 9 | Backup-encryptie | Geen bewijs | Infra | Bevestig versleutelde back-ups | Ops | **Ja** |
| 10 | Backupretentie | Geen bewijs | Infra | Definieer + bevestig retentie; align met GDPR (Fase 7) | Ops/Legal | Ja |
| 11 | Point-in-time recovery | Geen bewijs | Infra | Bevestig WAL/PITR | Ops | Ja |
| 12 | Restore-test | Geen bewijs | Operationeel | Voer periodieke restore-test uit + documenteer | Ops | Ja |
| 13 | WAF | Geen | Edge | Overweeg WAF vóór API | Ops/Sec | Nee |
| 14 | Centrale logaggregatie | Alleen console-`ILogger` + in-DB audit | Infra | Ship gestructureerde logs (Fase 6) naar centrale sink | Ops | Ja |
| 15 | Alerting (brute force, token-reuse, adminrole, grote export, tenant-denials, webhook-fails, configfouten) | Events deels gepland (Fase 6) | Runtime/monitoring | Definieer alerts op de gestructureerde events | Sec/Ops | Ja |
| 16 | Cloudstorage ACL's | Alleen `LocalFileStorageService` | Prod-backend onbekend | Bevestig private buckets, geen publieke URL's | Ops | **Ja** |
| 17 | Signed URLs voor documenten | N.v.t. lokaal | Prod-backend | Als object storage: korte-TTL signed URLs + autorisatie | Ops/Dev | Ja |
| 18 | Antivirus/malwarescanner op uploads | Interface gepland (Fase 4) | Externe dienst | Koppel scanner + quarantaine | Ops/Sec | Ja |
| 19 | SMTP/SMS/Peppol-provider | Geen echte provider; prod faalt nu fail-closed te booten | Externe vendor | Implementeer provider; secrets via vault | Dev/Ops | **Ja** (prod start anders niet) |
| 20 | DPA's & subprocessors | Geen | Juridisch | Sluit DPA's met vendors; ROPA bijwerken | Legal | Ja |
| 21 | `App_Data` niet publiek bereikbaar | Lokale schijf | Deploytopologie | Bevestig App_Data buiten webroot + uit back-upscope of versleuteld | Ops | **Ja** |
| 22 | Production object storage-scheiding per tenant | Storage-keys tenant-prefixed in code | Prod-backend | Bevestig scheiding/ACL's op prod-backend | Ops | Ja |
| 23 | Incident response-procedure | Geen | Organisatorisch | Stel IR-runbook op | Sec | Ja |
| 24 | Externe pentest | N.v.t. | Extern | Plan pentest na Fase 1–5 | Sec | Ja |
| 25 | Interne audit / access reviews | Geen | Organisatorisch | Periodieke rol-/toegangsreview | Sec | Ja |
| 26 | Business continuity / DR-plan | Geen | Organisatorisch | Stel BCP/DR op + test | Ops | Ja |
| 27 | GDPR legal review | Techniek gepland (Fase 7) | Juridisch | Valideer grondslagen, bewaartermijnen, DSR-proces | Legal | Ja |
| 28 | ISO 27001 ISMS-documentatie | Geen | Organisatorisch | Bouw ISMS (beleid, risico's, SoA, controls) | Sec | Nee (certificeringstraject) |
| 29 | Audit-retentie ondanks append-only-trigger | Trigger `trg_audit_logs_append_only` weigert UPDATE/DELETE (migratie `AuditAppendOnly`) | Retentie-delete vereist bewuste maintenance-actie | Draai archivering/purge als aparte DB-rol die de trigger tijdelijk disabled (`ALTER TABLE audit_logs DISABLE TRIGGER …`), gelogd en four-eyes | Ops/Sec | Nee |

> **Belangrijk:** de codewijzigingen in deze sprint zijn *security hardening* en *ISO 27001-/OWASP-
> aligned controls* / *voorbereiding op formele compliance* — geen bewijs van certificering.
> Certificering vereist de organisatorische items hierboven plus een formeel ISMS en externe audit.
