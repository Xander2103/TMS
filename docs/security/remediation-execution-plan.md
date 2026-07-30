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

### C3 — Raw tokens in responses/outbox/bestanden · **DEELS DONE**
- **DONE (responselek dicht):** sink alleen in Development geregistreerd; prod = fail-closed
  `UnconfiguredEmailProvider` + `StartupSecurityValidator` weigert te booten zonder echte provider.
  Hierdoor is `IsRawTokenSafeToReturn` (die op de sink-provider keyt) in productie altijd `false`,
  en worden ruwe activatie-/invite-tokens niet meer in prod-responses teruggegeven.
  `Program.cs`, `Modules/Messaging/Services/UnconfiguredMessageProviders.cs`. Test:
  `UnconfiguredEmailProvider_Throws…`, `StartupValidator_Throws_WhenNoRealEmailProvider…`.
- **PLANNED (token-persistentie-hygiëne):** stop met het opslaan van de ruwe token in
  `OutboxMessage.Body` en `IdempotencyKey` (`Modules/CustomerPortal/Services/CustomerPortalUserService.cs`
  `QueueInviteEmailAsync`, `Modules/Identity/Services/UserAccountFlowService.cs`): sla alleen een
  token-hash/-referentie op en render de link bij dispatch; verwijder de tokenprefix uit
  `IdempotencyKey`; roteer de dev-sinkbestanden; markeer bestaande open tokens als gecompromitteerd
  (invalidatie-migratie). Retentie/purge = Fase 7.
- **Tests (planned):** outbox-rij bevat na verzending geen bruikbare token; idempotencykey bevat
  geen tokenmateriaal.

### H15 — Gecommitte development secrets · **DONE (repo-deel) + OPS**
- **Code/Config:** `appsettings.Development.json` (lege signing key + lege connection string +
  toelichting), `appsettings.json` (`Dev:AllowImpersonationHeaders:false`, `Cors:AllowedOrigins`),
  `TransportationService.Api.csproj` (`UserSecretsId`), `Modules/Authentication/JwtOptionsValidator.cs`
  (weigert lege/korte/placeholder/**gecompromitteerde** key in elke omgeving), `docs/security/dev-setup.md`.
- **Tests:** `JwtOptionsValidator_RejectsUnsafeKeys` (incl. de gecommitte key), `…AcceptsAStrongKey`.
- **OPS (niet als opgelost gemarkeerd):** de gecommitte key roteren; Git-history purge
  (`git filter-repo`) is een gecoördineerde destructieve operatie — zie checklist.

---

## Fase 2 — Rollen, permissions & sessie

> **H2 en H3 zijn in iteratie 2 opgelost** (zie de C2/H2/H3-blokken hierboven). De resterende
> punten van deze fase staan hieronder en zijn nog **PLANNED**.

- **H4 refresh-reuse:** `AuthService` refresh-pad — detecteer herroepen/geroteerde token, revoke de
  familie via `ReplacedByTokenHash`, audit `RefreshReuseDetected`; per-user sessielimiet
  (config); purge-job (Fase 7). Betrouwbare familierelaties (transactioneel). Test: reuse trekt
  familie in.
- **H8 account-lockout:** `User` + migratie (`FailedLoginCount`, `LockedUntil`); `AuthService` telt
  mislukte pogingen, exponentiële lock; rate-limit-partitie ook op genormaliseerd e-mailadres;
  audit login-events (Fase 6). Test: lock na N pogingen ook vanaf nieuw IP.
- **H14 block/inactive:** *deels voorbereid in iteratie 2* — `User.SecurityStamp` + claim +
  per-request verificatie (`AccountStateAuthorizationFilter`) en `RevokeAllSessionsAsync` bestaan
  al. **Nog te doen:** `IsActive && !IsBlocked`-predicaat in `PermissionAuthorizationService`/
  `PermissionSetService`, `SetActive`/`SetBlocked` laten revoken via `RevokeAllSessionsAsync`, en
  access-token-TTL naar 10–15 min. Test: geblokkeerde gebruiker geweigerd bij volgende request.
- **M3 password-hardening:** centrale `PasswordPolicy` (min 12, config), veelgebruikte-wachtwoord-
  deny-list lokaal, `PasswordHasherOptions` expliciete iteraties (of Argon2id), rehash-on-login
  (`AuthService` handelt `SuccessRehashNeeded`). Test: legacy-hash upgrade.
- **M4 cross-tenant login:** expliciete tenantselectie (tenantcode/subdomein) of veilige
  disambiguatie; geen `FirstOrDefault`; geen N×hash-amplificatie. `AuthService`.
- **M13 JWT-hardening:** `ValidAlgorithms=[HS256]` pinnen; `IssuerSigningKeys` (huidig+vorig) +
  `kid`; migratiepad RS256/ES256 documenteren. `AuthenticationServiceCollectionExtensions`,
  `TokenService`.

## Fase 3 — Tenant-isolatie & objectautorisatie · **PLANNED**

- **H1 globale tenant-filter:** `ITenantOwnedEntity`-interface op alle tenant-entiteiten; globale
  EF `HasQueryFilter(TenantId == currentTenant && !IsDeleted)` via een ambient tenant-provider in
  `TransportationDbContext.OnModelCreating`; expliciete, geaudite bypass voor background jobs/
  migraties (`IgnoreQueryFilters` achter een helper); PostgreSQL RLS als defence-in-depth
  (Fase 9). **Architectuurtest** die tenant-entiteiten zonder filter faalt. Unieke indexen
  tenant-aware maken waar nodig. Risico: raakt elke module → gefaseerd + volledige suite per stap.
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

## Fase 4 — XSS, uploads, documenten, frontendsessie · **PLANNED**

- **H5 tokens uit localStorage:** refresh-token naar `HttpOnly; Secure; SameSite` cookie
  (`AuthController` login/refresh/logout server-side; frontend `authStorage.ts`/`apiClient.ts`/
  `AuthContext.tsx` alleen access-token in geheugen); CSRF-bescherming op de cookieflow; CORS
  `AllowCredentials` afstemmen. Grote frontend+backend-wijziging.
- **H6/L3 content-type:** server-side MIME uit gevalideerde extensie + magic bytes; mismatch
  weigeren; blob-URL-anchors voor gebruikerscontent verwijderen/forceren tot attachment
  (`PodService`, `ExecutionExceptionService`, `exceptionsApi.ts`, `podApi.ts`, detailpagina's);
  read-fallback voor bestaande rijen.
- **M5 sanitizer:** parser-gebaseerde sanitizer (AngleSharp) i.p.v. regex (`HtmlSanitizer.cs`);
  encode bij render; regressietests (svg/onload, img/onerror, malformed, slash-attrs, casing,
  protocol/data-URLs).
- **L1 SVG:** SVG weren uit uploads (of streng saneren + attachment + nosniff); bestaande logo's
  hervalideren.
- **L4/L10 storage:** root-containment met separator-guard (`LocalFileStorageService`); globale
  request-bodylimiet; magic-byte-validatie; uitbreidbare malware-scaninterface + quarantaine.

## Fase 5 — Rate limiting, headers, CORS, API-hardening · **PLANNED**

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

## Fase 6 — Audit, monitoring, forensics · **PLANNED**

- **H12 auth-events:** `AuthService`/`AuthController` auditeren Login(Succeeded/Failed),
  AccountLocked, TokenRefreshed, RefreshRejected, RefreshReuseDetected, Logout, PasswordChanged,
  PasswordResetByAdmin, relevante denials (`RequirePermissionAttribute`). Nooit wachtwoord/token.
- **M6 forensics:** `AuditService` schrijft IP (`IHttpContextAccessor`) + CorrelationId/TraceId +
  tenant/actor/action/target/UTC/result/reden; audit transactioneel met de businesswijziging;
  append-only via DB-`REVOKE UPDATE,DELETE` + trigger (migratie/OPS); retentie (Fase 7).
- **M7/M8/M14 gevoelige data:** aparte permissie voor ziekte/medische data; vrije medische tekst
  minimaliseren; IBAN/BIC-masking waar volledig zicht niet nodig; `EmployeeHistoryService`
  categoriefiltering per permissie; read-audit op medische/personeelsdownloads + bulk-exports;
  dataclassificatie op auditevents; audit-viewer maskeert bijzondere gegevens.

## Fase 7 — GDPR, retentie, data subject rights · **PLANNED**

- **H13:** configureerbaar retentiebeleid + hosted purge-sweep (audit/outbox/tokens/sink/Peppol-EDI-
  payloads/documenten/tijdelijke exports) met legal-hold; data-subject-export (tenant-aware,
  bevoegd, geaudit, korte TTL); anonimisering/pseudonimisering (NRN/IBAN/BIC/DOB/privécontact/
  vrije medische tekst) met behoud van referentiële integriteit en wettelijke financiële records;
  dataminimalisatie-review op DTO's/exports/logs; conceptdocumentatie (retentiebeleid,
  anonimiseringsmatrix, DSR-procedure, databronnenlijst, open juridische vragen).

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
