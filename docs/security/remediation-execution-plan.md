# Security Remediation Sprint — uitvoeringsplan per finding

Backlog = de bevindingen uit de Security Gap Analyse (C1–C3, H1–H15, M1–M15, L1–L11).
Dit document koppelt elke bevinding aan bestanden, config, DB, tests en operationele acties, met
een **eerlijke status**. Regel: niets staat op `DONE` zonder code **en** groene test. Operationele
acties buiten de repository staan in `operational-checklist.md` en worden nooit als opgelost
gemarkeerd.

**Statuslegenda:** `DONE` = geïmplementeerd + getest + groen in deze sprint · `PLANNED` = ontwerp
vastgelegd, nog te implementeren · `OPS` = (deels) buiten de repository, zie checklist.

---

## Fase 1 — Kritieke bevindingen & configuratieveiligheid

### C1 — Authenticatiebypass via ambient context & dev-headers · **DONE**
- **Code:** `Modules/Authentication/AuthenticationServiceCollectionExtensions.cs` (FallbackPolicy
  `RequireAuthenticatedUser`), `Modules/Tenancy/TenantContextMiddleware.cs` (identiteit uit
  `HttpContext.User`; dev-headers alleen bij `IsDevelopment() && Dev:AllowImpersonationHeaders`;
  fail-open default-tenant volledig verwijderd; pure `Resolve`-methode), `Modules/Security/
  StartupSecurityValidator.cs` (fail-fast op onveilige prod-config), `Controllers/HealthController.cs`
  (`[AllowAnonymous]`).
- **Config:** `Dev:AllowImpersonationHeaders` (dev=true, prod=false).
- **Tests:** `Security/Phase1ConfigAndAuthTests.cs` — resolutie met/zonder claim/header/opt-in,
  FallbackPolicy vereist authenticatie, reflectie-allowlist van `[AllowAnonymous]`-acties,
  startupvalidator gooit bij impersonatie in prod.
- **Rest/OPS:** productie moet `ASPNETCORE_ENVIRONMENT` correct zetten (checklist).

### C2 — Privilege escalation via users.edit & password reset · **DONE (iteratie 2)**
- **Code:** `Modules/Identity/Services/AccountSecurityService.cs` (nieuw: effectieve permissies,
  `CanManageUserAsync`, `IsProtectedSystemUserAsync`, `ActorHoldsAllAsync`, `RevokeAllSessionsAsync`,
  portal-helpers); `UserService.SetPasswordAsync` (aparte permissie, self-reset geweigerd,
  privilege-subset + systeemaccount-guard, sessie-revocatie, audit `PasswordResetByAdmin` zonder
  wachtwoord/token); `UsersController` gate → `users.reset_password` + 403-mapping;
  `Modules/Identity/Authorization/AccountStateAuthorizationFilter.cs` (nieuw: security-stamp-
  verificatie + MustChangePassword-afdwinging, centraal als globale filter geregistreerd);
  `PermitWhenPasswordChangeRequiredAttribute` op `/auth/me`, `/auth/logout`, `/me/password`;
  `TokenService`/`AuthService`/`AppClaimTypes` (stamp- + must-change-claims);
  `UserAccountFlowService` roteert de stamp bij token-reset.
- **DB:** migratie `20260730203928_AccountSecurityHardening` — `users.SecurityStamp` (additief).
- **Permissie:** `users.reset_password` (v-loze catalogusuitbreiding; systeemrollen krijgen de
  volledige catalogus automatisch via `PermissionCatalogSeeder`, dus standaard alleen Administrator).
- **Tests:** `Security/Phase2PrivilegeEscalationTests.cs` — hoger-geprivilegieerd doel 403,
  systeemadmin 403, self-reset 403, toegestane reset revoked sessies + zet MustChangePassword +
  roteert stamp + audit zonder secrets.
- **Open (bewust):** *recente re-authenticatie* bij administratieve reset is NIET geïmplementeerd;
  gekozen alternatief = aparte gevoelige permissie + effectieve privilege-subsetcontrole. Blijft
  open hardeningpunt.

### H2 — Self-role assignment & role escalation · **DONE (iteratie 2)**
- **Code:** `UserService.AssignRolesAsync` — self-guard (actor ≠ target), replace-semantiek
  autoriseert zowel toevoegen als verwijderen, systeemrol alleen door systeemgebruiker,
  rol-permissies moeten subset van de actor zijn, laatste-administrator-guard behouden, audit
  met actor.
- **Tests:** self-assign 403, rol met ontbrekende permissies 403, systeemrol 403, verwijderen van
  hoger-geprivilegieerde rol 403, toegestane toewijzing + audit.

### H3 — Onveilige permission assignment · **DONE (iteratie 2)**
- **Code:** `RoleService.AssignPermissionsAsync` — systeemrollen volledig beschermd
  (`SystemRoleProtected`, voorkomt uithollen/lockout), onbekende codes geweigerd (ook
  casing-varianten), klantportaalrollen alleen `customer_portal.*`, actor kan enkel eigen
  permissies toekennen; `RolesController` 403/400-mapping.
- **Break-glass:** systeemrollen worden centraal door `PermissionCatalogSeeder` van de volledige
  catalogus voorzien bij startup — herstel vereist dus geen applicatiepermissie (gedocumenteerd
  als break-glass, geen verborgen bypass in de gewone flow).
- **Tests:** systeemrol-mutatie geweigerd, permissie buiten actor-set 403, portalrol weigert
  interne permissie (ook andere casing), portalrol accepteert portal-permissie, onbekende code 400.

### C3 — Raw tokens in responses/outbox/bestanden · **DONE** (retentie/purge → Fase 7)
- **DONE (responselek dicht):** sink alleen in Development geregistreerd; prod = fail-closed
  `UnconfiguredEmailProvider` + `StartupSecurityValidator` weigert te booten zonder echte provider.
  Hierdoor is `IsRawTokenSafeToReturn` (die op de sink-provider keyt) in productie altijd `false`,
  en worden ruwe activatie-/invite-tokens niet meer in prod-responses teruggegeven.
  `Program.cs`, `Modules/Messaging/Services/UnconfiguredMessageProviders.cs`. Test:
  `UnconfiguredEmailProvider_Throws…`, `StartupValidator_Throws_WhenNoRealEmailProvider…`.
- **DONE (token-persistentie-hygiëne):** `IdempotencyKey` gebruikt nu een one-way
  SHA-256-referentie i.p.v. een ruwe-tokenprefix (`CustomerPortalUserService.TokenReference`);
  de dispatcher scrubt de body van credential-dragende kinds
  (`MessageKinds.CarriesOneTimeCredential`) zodra bezorging beslist is — bij Sent én bij
  permanente Failed (vóór de fallback wordt gespawnd, dus die erft de gescrubde body); retries
  behouden de link tot de beslissing. Migratie `20260730231233_TokenPersistenceHygiene` scrubt
  historische invite-rijen en revoked alle nog-open activatietokens (als gecompromitteerd
  beschouwd). `UserAccountFlowService` bleek al schoon (alleen hashes in DB; sink alleen in
  Development). Retentie/purge van outbox/sink = Fase 7; sink-rotatie = dev-hygiëne (checklist).
- **Tests:** `Security/Phase1TokenHygieneTests.cs` (scrub bij Sent/Failed, behoud bij retry,
  non-credential mails onaangeroerd) + `CustomerPortalUserServiceTests.Invite_IdempotencyKey_
  ContainsNoRawTokenMaterial`.

### H15 — Gecommitte development secrets · **DONE (repo-deel) + OPS**
- **Code/Config:** `appsettings.Development.json` (lege signing key + lege connection string +
  toelichting), `appsettings.json` (`Dev:AllowImpersonationHeaders:false`, `Cors:AllowedOrigins`),
  `TransportationService.Api.csproj` (`UserSecretsId`), `Modules/Authentication/JwtOptionsValidator.cs`
  (weigert lege/korte/placeholder/**gecompromitteerde** key in elke omgeving), `docs/security/dev-setup.md`.
- **Tests:** `JwtOptionsValidator_RejectsUnsafeKeys` (incl. de gecommitte key), `…AcceptsAStrongKey`.
- **OPS (niet als opgelost gemarkeerd):** de gecommitte key roteren; Git-history purge
  (`git filter-repo`) is een gecoördineerde destructieve operatie — zie checklist.

---

## Fase 2 — Rollen, permissions & sessie · **DONE**

> H2/H3 in iteratie 2 (`f2c301d`); H4/H14 in `979c99b`; H8/M3/M4 in `36db50c`;
> M13 (HS256-pinning + `IssuerSigningKeys` huidig+vorig) zit in dezelfde reeks
> (`AuthenticationServiceCollectionExtensions`). Tests: `Security/Phase2SessionSecurityTests.cs`.

- **H4 refresh-reuse · DONE:** reuse-detectie + familie-revocatie + audit `RefreshReuseDetected`.
- **H8 account-lockout · DONE:** `FailedLoginCount`/`LockedUntil` (migratie
  `SessionAndLockoutHardening`), exponentiële lock, audit login-events.
- **H14 block/inactive · DONE:** security-stamp per request + `RevokeAllSessionsAsync` bij
  `SetActive`/`SetBlocked`.
- **M3 password-hardening · DONE**, **M4 cross-tenant login · DONE** (veilige disambiguatie +
  audit `LoginAmbiguousTenant`), **M13 JWT-hardening · DONE** (`ValidAlgorithms=[HS256]`).

## Fase 3 — Tenant-isolatie & objectautorisatie · **DEELS DONE (`2ff0058`)**

> H7/M1/M9/M12/L8/L9 zijn **DONE** in `2ff0058` (`EnsureBelongsToTenantAsync`-guard, driver-scoping
> POD/exception, unieke Peppol-identiteit, inactieve-klant-portalgate). Tests:
> `Security/Phase3TenantIsolationTests.cs`. Alleen H1 (globale EF-queryfilter) staat nog open —
> de architectuurtests (TenantId-kolom + PK op elke tenant-entiteit) liggen al klaar.

- **H1 globale tenant-filter · DONE:** `ITenantQueryFilterAccessor` (HTTP-implementatie leest de
  door `TenantContextMiddleware` geresolvede tenant; null buiten een request) +
  `TransportationDbContext.ApplyGlobalTenantFilters` — elke `ITenantOwned`-entiteit krijgt
  `CurrentTenantFilterId == null || TenantId == CurrentTenantFilterId` ge-AND met de bestaande
  (soft-delete-)filter. Request-contexten zijn structureel omheind, ook zonder expliciete
  `Where`; system-/backgroundscope (dispatcher, seeders, migraties, anonieme login/webhook) is
  de gedocumenteerde open-bypass. `IgnoreQueryFilters`-sites dragen hun eigen tenantpredicaat
  (al aanwezig). Architectuurtest: tenant-entiteit zonder filter faalt de build; gedragstests:
  fencing per tenant, open systeemscope, compositie met soft delete
  (`Phase3TenantIsolationTests`). PostgreSQL RLS blijft Fase 9-defence-in-depth.
- **H7 driver-scoping POD/exception:** `PodService`/`ExecutionExceptionService` open/download —
  `restrictToOwnDriver` + trip→driver-check, 404 bij mismatch. Test: chauffeur A ≠ B → 404.
- **M1 Peppol-identiteit:** globale unieke index op actieve `(PeppolScheme, PeppolId)` op
  `LegalEntity`; webhook fail-closed bij multi-match; audit identiteitswijziging. Migratie.
- **M9 inactive-customer portal:** gedeelde `MyCustomerIdAsync` die `IsActive && !IsDeleted` vereist
  (`PortalDocumentService`/`PortalInvoiceService`/`CustomerPortalService`); portalsessies revoken
  bij klantdeactivatie.
- **M12/L8/L9 referentie-tenantchecks:** gedeelde `EnsureBelongsToTenant`-helpers voor
  trip/packages/voertuig/locatie/leasing/EDI-mapping/qualification/communication-rule.
- **L2 fail-open fallback:** al verwijderd in C1 (`TenantContextMiddleware`). Test dekt het.

## Fase 4 — XSS, uploads, documenten, frontendsessie · **DONE**

- **H5 tokens uit localStorage · DONE:** refresh-token in `HttpOnly; Secure; SameSite=Strict`
  cookie, gescoped op `Path=/api/auth` (`AuthController` — login/refresh/logout zetten/roteren/
  verwijderen de cookie; body-token blijft geaccepteerd voor non-browser-API-clients maar wordt
  in browser-responses leeggehaald). Frontend: access-token uitsluitend in geheugen
  (`authStorage.ts`), boot-restore via cookie-refresh (`AuthContext.tsx`), refresh-on-401 via
  cookie (`apiClient.ts`), legacy-localStorage-tokens worden actief gewist. CSRF: SameSite=Strict
  (cross-site requests dragen de cookie nooit) + expliciete CORS-origins met `AllowCredentials`.
- **H6/L3 content-type · DONE:** gedeelde `Modules/Security/UploadValidation` — magic-byte-
  signaturen (pdf/png/jpg/webp/xlsx) naast de bestaande extensie-whitelists, gewired in alle
  upload-endpoints (employee-/fleet-/order-docs, kwalificaties, attesten, POD/exception-foto's,
  invoices, logo's, klant-/pricing-/colli-imports) + service-level in `AbsenceService`.
  Downloads bepalen het content-type al server-side uit de extensie; nosniff staat globaal (H10).
- **M5 sanitizer · DONE:** `HtmlSanitizer` herbouwd op AngleSharp (parse → allowlist-rebuild,
  tekst geëncodeerd, href alleen absolute http/https via `Uri.TryCreate`); regressietests dekken
  svg/onload, img/onerror, malformed nesting, slash-attrs, casing, data:/javascript:-URL's.
- **L1 SVG · DONE:** SVG uit de logo-whitelist (`LegalEntitiesController`); scriptbare formaten
  hebben nergens een signatuur. Bestaande SVG-logo's worden met attachment-disposition geserveerd.
- **L4/L10 storage · DONE:** separator-guard in `LocalFileStorageService`-root-containment
  (sibling-prefix-truc gedekt); globale Kestrel-bodylimiet 32 MB naast per-endpoint
  `[RequestSizeLimit]`; `IUploadScanner`-seam met expliciete `PassThroughUploadScanner` in DI —
  echte engine = checklist #18.
- **Tests:** `Security/Phase4UploadHardeningTests.cs` + frontend-suite (549) groen.

## Fase 5 — Rate limiting, headers, CORS, API-hardening · **DEELS DONE (`59732e0`)**

> H9 (forwarded headers + resolved-IP-partitie), H10 (security-headers-middleware), M15 (CORS
> fail-closed uit config), M10/M11 (ProblemDetails zonder stacktrace, `TryParseDefined<TEnum>`)
> zijn **DONE**; tests in `Security/Phase5ApiHardeningTests.cs`. **L7 is inmiddels DONE**: de
> permissiechecks in `TransportOrderService` (prijs-override, prijsstatus) zijn fail-closed —
> een ontbrekende authorization service weigert, nooit stilzwijgend toestaan. **Nog open:** M2
> webhook-HMAC is deels (replay-window/rotatie ontbreken).

- **H9 forwarded headers:** `UseForwardedHeaders` met `KnownProxies/KnownNetworks` vóór rate
  limiting/auth; partitie op resolved client-IP + per-account (`Program.cs`,
  `RateLimitingServiceCollectionExtensions`).
- **H10 security headers:** middleware met HSTS (niet-dev), CSP (React-compatibel, geen
  unsafe-inline waar mogelijk), `frame-ancestors 'none'`, nosniff, Referrer-Policy,
  Permissions-Policy, cache-control op gevoelige responses, serveridentificatie verwijderen.
- **M2 webhook:** HMAC over raw body + timestamp/replay-window + rotatie + per-provider/tenant
  secret + rate limit; constant-time; idempotent (`PeppolWebhookController`/`Service`).
- **M15 CORS:** origins uit `Cors:AllowedOrigins` (al toegevoegd aan config); fail-closed
  buiten Development; integratietest.
- **M10/M11 errors/parsing:** `UseExceptionHandler`/ProblemDetails-flow (geen stacktrace in prod);
  service-validatie vóór EF; gedeelde `TryParseDefined<TEnum>` + 400 op ongedefinieerde enums
  (`IncidentService`, `DossierService`, `PricingExcelService`, `EdiService`, `PackageImportService`).
- **L7 authorization service verplicht:** `TransportOrderService._permissionService` niet-nullable;
  fail-closed.

## Fase 6 — Audit, monitoring, forensics · **DONE**

- **H12 auth-events · DONE (in Fase 2-commits):** `SecurityAuditEvents`-catalogus;
  Login(Succeeded/Failed/AmbiguousTenant/BlockedWhileLocked), AccountLocked, TokenRefreshed,
  RefreshRejected, RefreshReuseDetected, Logout, PasswordResetByAdmin, UserBlocked/Deactivated,
  SessionsRevoked — nooit wachtwoord-/tokenmateriaal.
- **M6 forensics · DONE:** `AuditService` stempelt client-IP (na `UseForwardedHeaders`) +
  `CorrelationId` (TraceIdentifier) op elk record; append-only door constructie via
  PostgreSQL-trigger (migratie `20260730222437_AuditAppendOnly`; SQLite-testharness geguard).
  Retentie/purge volgt in Fase 7; maintenance-delete = bewuste actie (checklist).
- **M7 medische data · DONE:** permissie `absences.view_medical` (catalogus + role-upgrade v22,
  alleen HR); `AbsenceService` redigeert ziekte-reden/HR-notitie/attest voor houders zonder de
  permissie, met self-exemptie voor de betrokkene (portal blijft werken); attest-download 403
  i.p.v. 404 (`AbsenceAttachmentResult`); review-context verbergt attest-bestaan.
- **M8 IBAN/NRN · DONE (bestaand + history):** live DTO's gaten al op
  `employees.view_confidential`; de dossierhistoriek dekt dit nu ook (zie M14).
- **M14 history/read-audit · DONE:** `EmployeeHistoryAccess` — historiek spiegelt de
  live-gates (vertrouwelijke velden, medische velden op ziekte, gevoelige documentcategorieën);
  read-audits `HealthDataViewed` (attest door niet-betrokkene), `SensitiveDocumentDownloaded`
  (ID/medisch/contract, met dataclassificatie) en `DataExported` (KPI-, boekhoud-,
  winstgevendheids- en colli-exports, met filter).
- **Tests:** `Security/Phase6AuditForensicsTests.cs` (14) + `DefaultRoleSeederTests.Version22…`.
- **Open (bewust, klein):** vrije medische tekst verder minimaliseren en audit-viewer-masking
  in de frontend; gestructureerde events → centrale sink is OPS (checklist #14/#15).

## Fase 7 — GDPR, retentie, data subject rights · **DONE (repo-deel)**

- **H13 · DONE:** `Modules/Gdpr` — configureerbaar retentiebeleid (`Retention`-sectie, defaults
  365d outbox / 30d securitytokens / 14d dev-sink) met **legal hold** die alle purges bevriest;
  dagelijkse `GdprRetentionHostedService` naast de bestaande refresh-token-sweep; audit-logs
  bewust NIET in-app gepurged (append-only, checklist #29); data-subject-export
  (`GET /api/employees/{id}/gdpr-export`, permissie `employees.view_confidential`,
  read-audit `DataExported` classificatie Health); anonimisering
  (`POST /api/employees/{id}/anonymize`, nieuwe permissie `employees.anonymize` in géén enkel
  default-template) — identificerende + bijzondere gegevens gewist, dossiers/attesten HARD
  verwijderd (ExecuteDelete voorbij de soft-delete-interceptor, bestanden uit storage), account
  gedeactiveerd + sessies dood, businessstructuur behouden; auditrij zonder oude waarden.
- **Documentatie:** `docs/security/gdpr.md` — retentiebeleid, anonimiseringsmatrix,
  DSR-procedure, databronnenlijst, open juridische vragen (Legal/DPO, checklist #20/#27).
- **Tests:** `Security/Phase7GdprTests.cs` (sweep/legal hold/export/anonimisering).

## Fase 8 — CI/CD & supply chain · **PLANNED**

- **H11:** GitHub Actions — backend restore/build (warnings-as-errors waar haalbaar)/test/coverage/
  `dotnet list package --vulnerable`/analyzers; frontend `npm ci`/typecheck/lint/test/build/
  `npm audit`; security gitleaks + CodeQL + dependency-review + SBOM; geen deploy bij kritieke/hoge
  bevindingen; Dependabot/Renovate; `SECURITY.md`, PR- en release-securitychecklist.
- **L5 dode permissies:** `packages.export`/`users.delete`/`qualification_types.manage` verwijderen
  of handhaven; architectuurtest catalogus vs. daadwerkelijke checks.
- **L6 credential-logging:** `DevAdminSeeder` logt geen wachtwoord; conventietest op gevoelige termen.
- **L11 provider-credentials:** tenant-aware providerinterfaces; geen singleton met globale
  credentials; fail-fast placeholder (nu al voor mail via `UnconfiguredEmailProvider`).

## Fase 9 — DB / data-at-rest-hardening · **PLANNED + OPS**

- Kolomencryptie voor NRN/medische/bijzondere identifiers (pgcrypto of .NET Data Protection) met
  key-rotation, aparte keys per omgeving, keys buiten DB/repo, migratie + backwards-compatibele
  decrypt/migrate; DB least-privilege (aparte migratie- en runtime-accounts); append-only
  audit-privileges; RLS-voorbereiding; veilige connectionstring (OPS); TLS naar DB (OPS).

## Fase 10 — Systematische securitytestsuite · **PLANNED**

- Reflectie (auth/permissie per actie — deel al in Fase 1), tenant-isolatie (geparametriseerd),
  privilege-escalatie, sessie, upload/XSS, API-hardening, audit/GDPR. Zie originele opdracht §Fase 10.
