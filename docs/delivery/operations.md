# Operations & Deployment — TransportationService

Doelgroep: platform-/applicatiebeheer. Stack: .NET 10 (ASP.NET Core API), EF Core 10,
PostgreSQL 16, React + Vite (SPA, PWA-shell). Alle beweringen in dit document zijn
geverifieerd tegen de code op branch `nav-redesign` (stand 2026-08-12).

Verwante documenten:

- `docs/security/operational-checklist.md` — operationele securitychecklist (29 punten,
  release-blocking gemarkeerd)
- `docs/security/dev-setup.md` — lokale secrets via user-secrets
- `docs/background-jobs.md` — achtergrondjobs in detail
- `docs/peppol.md` — Peppol-architectuur incl. webhook en provider-aansluiting
- `docs/dossiers.md`, `docs/pricing.md`, `docs/warehouse-scanning.md`, `docs/storage.md`,
  `docs/problems.md` — functionele documentatie van de redesign-waves

---

## 1. Databasemigraties

### 1.1 Toepassen

De applicatie voert **geen** automatische migraties uit bij het opstarten (er is bewust geen
`Database.Migrate()` in `Program.cs`). Migraties worden altijd expliciet toegepast vóór de
nieuwe binaries starten:

```bash
dotnet ef database update --project TransportationService.Api
```

De connection string komt uit `ConnectionStrings:DefaultConnection` (user-secrets of
omgevingsvariabele `ConnectionStrings__DefaultConnection`). Voor een productiedatabase kan
ook een idempotent SQL-script gegenereerd en door een DBA beoordeeld worden:

```bash
dotnet ef migrations script --idempotent --project TransportationService.Api -o migrate.sql
```

### 1.2 Redesign-migraties (dossier-centric redesign, Waves 0–6 + afrondingsgolf P0–P13)

De volgende veertien migraties vormen samen de redesign (nr. 1–8) en de afrondingsgolf
P0–P13 (nr. 9–14). Ze zijn **allemaal reeds toegepast op de dev-database** en zijn
**strikt additief**: in geen enkele `Up()` komt een `DropTable` of `DropColumn` voor.
Volgorde (= volgorde van toepassing):

| # | Migratie | Nieuwe tabellen | Wijzigingen aan bestaande tabellen |
|---|---|---|---|
| 1 | `20260811214114_DossierActivityFoundation` | `activity_types`, `dossier_activities` | `transport_dossiers`: +`CustomerReference`, `DossierDate`, `LegalEntityId`, `OriginTransportOrderId`, `Version`; `transport_orders`: +`Version` |
| 2 | `20260812001932_CommercialFoundation` | `customer_allowed_legal_entities` | 14 kolommen, o.a. `transport_orders`.`InvoiceReadiness(+Reasons)`, `order_pricing_snapshots`.`CoverageStatus`/`IsStale`, `SalesCategoryId` op `order_pricing_lines`/`order_service_lines`/`price_rules`/`pricing_agreements`/`service_options`, `customers`.`InvoiceGrouping`, `invoices`.`LanguageCode`, uitbreidingen op `sales_categories` |
| 3 | `20260812071320_PricingGeneralization` | `tenant_holidays` | `transport_orders`: +`DistanceKm`, `LoadingMeters`; `price_rules`: +`OriginZoneId` |
| 4 | `20260812074930_WarehouseLocationsAndStandaloneScans` | `warehouse_locations` | `packages`: +`CurrentWarehouseLocationId`; `package_events`/`scan_events`: +`WarehouseLocationId` |
| 5 | `20260812080131_StandaloneScanOrderNullable` | — | `scan_events`: `AlterColumn` (orderreferentie nullable — losse magazijnscans) |
| 6 | `20260812081943_StorageStays` | `storage_stays` | — |
| 7 | `20260812083503_ProblemsResponsibilityCharge` | — | `incidents`: +8 kolommen (`ResponsibleParty`, `ResponsibilityNotes`, `Charge*`, `LinkedRedeliveryOrderId`) |
| 8 | `20260812085739_EtaShiftThreshold` | — | `tenant_settings`: +`EtaShiftNotifyMinutes` (nullable) |
| 9 | `20260812170208_DocumentStrategy` | `tenant_document_rules` | `customers`: +`DocumentStrategy`; `transport_orders`: +`DocumentPreference` |
| 10 | `20260812171230_RedeliveryAndChargePolicy` | `incident_charge_policies` | `tenant_settings`: +`RedeliveryMode`; `incidents`: +`SourceStopId` (unieke gefilterde index — max. één auto-incident per mislukte stop) + `RedeliverySuggested` |
| 11 | `20260812172239_PricingDimensions` | — | `price_rules`: +`ActivityTypeId`; `transport_orders`: +`PlateauRequired`, `MoffettRequired`, `IsReturnMovement` |
| 12 | `20260812173308_ServiceQuantitySource` | — | `service_options`: +`QuantitySource` |
| 13 | `20260812174522_OrderImport` | `order_import_profiles`, `order_import_batches`, `order_import_rows` | `notification_rules`: +`RequiresReview` (nullable) |
| 14 | `20260812175816_InvoiceSnooze` | — | `transport_orders`: +`InvoiceSnoozeUntil`, `InvoiceSnoozeReason` |
| 15 | `DatabaseBackups` (settings/system-wave) | `database_backups` (systeemniveau, bewust zonder TenantId — permissiegestuurd) | — |

De afrondingsgolf voegt ook nieuwe **instellingsoppervlakken** toe die na een deploy geen
extra configuratie vereisen maar wel bekend moeten zijn bij de beheerder:
**Documentregels** (`/settings/document-rules`), **Doorrekenbeleid**
(`/settings/charge-policies`), en in de bedrijfsinstellingen de **herleveringsmodus**
(`RedeliveryMode`, default `Manual` = gedrag van vóór de golf) en de **ETA-drempel**
(`EtaShiftNotifyMinutes`, nu instelbaar via de UI; leeg = functie uit). Het
Excel-importprofiel "Generiek v1" wordt per tenant idempotent geseed bij eerste gebruik.

### 1.2b Wave 1 datamigraties (productieblokkers, 2026-08-30)

Twee **datamigraties** — ze wijzigen géén schema, alleen bestaande rijen. Ze horen bij dezelfde
release als de frontend-tijdconventie (C-03) en de stabiele stopidentiteit (C-01) en mogen daar
niet van gescheiden worden: het deployscript stopt de oude app, migreert en start pas daarna de
nieuwe, wat precies de vereiste volgorde is.

| Migratie | Wat ze doet |
|---|---|
| `20260830133955_StopWindowTenantZoneReencoding` | Her-encodeert de acht tijdvenstercolommen van `transport_order_stops` van "wandklok gestempeld als UTC" naar echte UTC-instants, per tenant-tijdzone. Rijen van EDI-opdrachten worden **uitgesloten** (die dragen al een echte instant). |
| `20260830134439_PackageStopPinRepair` | Herstelt `packages."LoadingStopId"/"DeliveryStopId"` die naar een soft-deleted stop wijzen, maar uitsluitend waar de match eenduidig is (zelfde opdracht, `Sequence`, `StopType` én plaats). |

Beide schrijven hun tellingen naar `audit_logs` (`EntityType = 'DataMigration'`), één rij per
tenant: `RAISE NOTICE` van PostgreSQL is **niet zichtbaar** via `dotnet ef database update`, en dat
is hoe `scripts/deploy-transportationservice.sh` migreert. De tellingen zijn dus ook achteraf nog
opvraagbaar, en zichtbaar in de auditpagina.

#### Vóór toepassen (op een **teruggezette kopie** van productie, nooit live)

```sql
-- 1. Omvang per tenant + welke tijdzone gebruikt wordt (NULL = geen tenant_settings-rij → default).
SELECT t."Id", t."Name", ts."Timezone",
       count(s.*) FILTER (WHERE s."PlannedFrom" IS NOT NULL OR s."PlannedTo" IS NOT NULL
                            OR s."RequestedFrom" IS NOT NULL OR s."RequestedTo" IS NOT NULL
                            OR s."ConfirmedFrom" IS NOT NULL OR s."ConfirmedTo" IS NOT NULL
                            OR s."EarliestAllowed" IS NOT NULL OR s."LatestAllowed" IS NOT NULL) AS stop_rows
  FROM tenants t
  LEFT JOIN tenant_settings ts ON ts."TenantId" = t."Id"
  LEFT JOIN transport_order_stops s ON s."TenantId" = t."Id"
 GROUP BY 1, 2, 3
 ORDER BY 4 DESC;

-- 2. Tenants met een tijdzone die PostgreSQL niet kent (de migratie gebruikt dan Europe/Amsterdam,
--    exact zoals de C#). Corrigeer die instelling liefst vooraf.
SELECT ts."TenantId", ts."Timezone"
  FROM tenant_settings ts
 WHERE lower(btrim(ts."Timezone")) NOT IN ('utc', 'etc/utc', 'universal', 'zulu')
   AND NOT EXISTS (SELECT 1 FROM pg_timezone_names z
                    WHERE lower(z.name) = lower(btrim(ts."Timezone")));

-- 3. Tenants zonder tenant_settings-rij die wél stops hebben (idem: default Europe/Amsterdam).
SELECT t."Id", t."Name"
  FROM tenants t
  LEFT JOIN tenant_settings ts ON ts."TenantId" = t."Id"
 WHERE ts."Id" IS NULL
   AND EXISTS (SELECT 1 FROM transport_order_stops s WHERE s."TenantId" = t."Id");

-- 4. Stopregels van EDI-opdrachten. Die worden NIET omgezet; het getal hoort te kloppen met
--    "skippedEdi" in de auditrij.
SELECT count(*)
  FROM transport_order_stops s
 WHERE EXISTS (SELECT 1 FROM edi_messages m
                WHERE m."TenantId" = s."TenantId" AND m."IsDeleted" = false
                  AND m."Direction" = 'Inbound' AND m."Status" = 'Processed'
                  AND m."ResultEntityType" = 'TransportOrder'
                  AND m."ResultEntityId" = s."TransportOrderId"::text);

-- 5. Droogloop van de omzetting: verwacht -02:00:00 (zomer), -01:00:00 (winter) of 00:00:00 (UTC).
--    Elke andere waarde = stoppen en onderzoeken.
SELECT ((s."PlannedFrom" AT TIME ZONE 'UTC') AT TIME ZONE coalesce(ts."Timezone", 'Europe/Amsterdam'))
         - s."PlannedFrom" AS shift,
       count(*)
  FROM transport_order_stops s
  LEFT JOIN tenant_settings ts ON ts."TenantId" = s."TenantId"
 WHERE s."PlannedFrom" IS NOT NULL
 GROUP BY 1 ORDER BY 2 DESC;

-- 6. Omvang van de verweesde colli-pins (C-01), vóór het herstel.
SELECT count(*) FROM packages p
 WHERE p."IsDeleted" = false
   AND (EXISTS (SELECT 1 FROM transport_order_stops d WHERE d."Id" = p."LoadingStopId"  AND d."IsDeleted")
     OR EXISTS (SELECT 1 FROM transport_order_stops d WHERE d."Id" = p."DeliveryStopId" AND d."IsDeleted"));
```

#### Na toepassen

```sql
-- A. De tellingen die de migraties zelf hebben vastgelegd.
SELECT a."Timestamp", a."TenantId", a."Action", a."OldValuesJson", a."NewValuesJson"
  FROM audit_logs a
 WHERE a."EntityType" = 'DataMigration'
 ORDER BY a."Timestamp" DESC;

-- B. Geen omgekeerde vensters meer (de migratie klapt DST-inversies dicht; moet 0 zijn).
SELECT count(*) FROM transport_order_stops
 WHERE "PlannedTo" < "PlannedFrom" OR "RequestedTo" < "RequestedFrom"
    OR "ConfirmedTo" < "ConfirmedFrom" OR "LatestAllowed" < "EarliestAllowed";

-- C. Restant verweesde colli-pins = "stillAmbiguous" uit A; die moeten met de hand nagekeken
--    worden (diagnosequery in het taakrapport task-5-report.md).
SELECT count(*) FROM packages p
 WHERE p."IsDeleted" = false
   AND (EXISTS (SELECT 1 FROM transport_order_stops d WHERE d."Id" = p."LoadingStopId"  AND d."IsDeleted")
     OR EXISTS (SELECT 1 FROM transport_order_stops d WHERE d."Id" = p."DeliveryStopId" AND d."IsDeleted"));
```

Sluit af met `VACUUM (ANALYZE) transport_order_stops;` — de omzetting herschrijft elke rij met een
venster, wat de tabel tijdelijk ongeveer verdubbelt tot autovacuum bijtrekt, en de statistieken
verouderd achterlaat. Controleer daarna in de applicatie zelf: een opdrachtdetail (tijdvensters),
een **collietiket** (ophaal-/leveruur) en een **planningsvoorstel** met venster — dat zijn de drie
schermen die de wandklok tonen.

### 1.3 Verifiëren

Na `database update`:

```sql
SELECT "MigrationId"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId" DESC
LIMIT 10;
```

Verwacht: `20260812175816_InvoiceSnooze` bovenaan, met daaronder de dertien overige
redesign-migraties in aflopende volgorde. Alternatief vanaf de buildmachine:
`dotnet ef migrations list --project TransportationService.Api` (toegepaste migraties zonder
`(Pending)`-markering).

---

## 2. Seeding

### 2.1 Startup-seeders in **elke** omgeving

`Program.cs` draait bij elke start, ook in productie, drie idempotente seeders:

1. **`CountrySeeder.SyncAsync`** — synchroniseert de globale ISO-landenlijst (vereist voor
   landvalidatie en comboboxen, tenant-onafhankelijk).
2. **`DossierBackfillSeeder.SyncAsync`** — wikkelt per tenant elk pre-redesign transportorder
   zonder dossier in een eigen wrapper-dossier (dossiernummering via `TenantSettings`;
   gefilterde unieke index op `OriginTransportOrderId` maakt duplicaten onmogelijk). Deze
   seeder roept per tenant ook **`ActivityTypeSeeder.EnsureSeededAsync`** aan: de
   standaard-activiteitstypecatalogus (o.a. het systeem-default transporttype) wordt per
   tenant aangelegd als die ontbreekt. Na de eerste run is dit één goedkope indexquery per
   tenant.
3. **`CoverageStatusBackfillSeeder.SyncAsync`** — getypeerde coverage-roll-up voor
   pre-Wave-2-prijssnapshots (idempotent).

`ActivityTypeSeeder` wordt daarnaast lazy aangeroepen op runtime-paden
(`ActivityTypeService`, snelle orderaanmaak in `TransportOrderService`), dus ook een tenant
die ná de deploy wordt aangemaakt krijgt zijn catalogus automatisch.

### 2.2 Startup-seeders **alleen in Development**

Binnen het `IsDevelopment()`-blok in `Program.cs` draaien bij elke start:

- `MasterDataSeeder.SeedAsync` — maakt de dev-tenant + gebruikers aan, **alleen** als de
  database nog géén tenants bevat.
- `PermissionCatalogSeeder.SyncAsync` — houdt de permissiecatalogus in sync met
  `PermissionCodes`.
- `ReferenceDataSeeder.SeedAsync` — starter-lookups.
- `RateCardConversionService.ConvertAsync` — eenmalige conversie van legacy rate cards naar
  pricing agreements (idempotent).
- `DefaultRoleSeeder.SyncAsync` — zie §2.3.
- `LegalEntitySeeder`, `ExpiryPolicySeeder`, `IssuedItemTemplateSeeder`.
- `DevAdminSeeder.EnsurePasswordAsync` — garandeert dat `admin@dev.local` een bruikbaar
  wachtwoord heeft (alleen wanneer nog geen wachtwoord gezet is; een bewust gewijzigd
  wachtwoord wordt nooit teruggezet).

> **Operationele waarschuwing:** de permissiecatalogus- en rolsjabloonsynchronisatie draait
> in de huidige code **uitsluitend in Development**. Op een productiehost worden nieuwe
> permissies en rol-upgrades dus **niet** automatisch toegepast bij een release. Neem in het
> releasedraaiboek een bewuste stap op om `PermissionCatalogSeeder` en `DefaultRoleSeeder`
> tegen de productiedatabase uit te voeren (of verplaats deze aanroepen buiten het
> Development-blok in een toekomstige release) — anders missen bestaande rollen de
> permissies van nieuwe modules.

### 2.3 Rolsjablonen en versie-upgrades

`DefaultRoleSeeder` werkt in drie idempotente fasen (zie de klasse-documentatie in
`TransportationService.Api/Data/DefaultRoleSeeder.cs`):

1. **Backfill** — oude, ongestempelde standaardrollen krijgen eenmalig hun `TemplateCode`.
2. **Create** — ontbrekende sjabloonrollen worden per tenant aangemaakt met de volledige
   actuele permissieset; een gelijknamige tenant-eigen rol wordt met rust gelaten.
3. **Upgrade** — versiestappen uit `DefaultRoleUpgrades` (huidige
   `CurrentVersion = 28`) voegen nieuw geïntroduceerde standaardpermissies exact één keer
   per tenant toe aan gestempelde rollen; de toegepaste versie staat in
   `role_template_states`. Er wordt nooit iets verwijderd — tenant-maatwerk (inclusief het
   naderhand weghalen van een geüpgradede permissie) overleeft elke re-run.

### 2.4 Demodata

Fictieve demodata wordt **alleen op expliciet verzoek** geseed, en alleen in Development:

```bash
dotnet run --project TransportationService.Api -- --seed-demo
```

(De vlag is `DevDemoDataSeeder.CommandLineFlag = "--seed-demo"`.)

Lokale ontwikkeldatabase: `docker compose up -d` start PostgreSQL 16 (database
`transportation_service`, poort 5432; wachtwoord via omgevingsvariabele
`POSTGRES_PASSWORD`, lokaal default `postgres`).

---

## 3. Deployment

### 3.1 Backend

```bash
dotnet publish TransportationService.Api -c Release -o <publishmap>
```

Draaien met `ASPNETCORE_ENVIRONMENT=Production` (checklistpunt 1). Bij het opstarten
valideert `StartupSecurityValidator` fail-fast; een productiehost **weigert te starten**
wanneer:

- `Dev:AllowImpersonationHeaders` aan staat (authenticatiebypass);
- er geen echte e-mailprovider geconfigureerd is (de Development-filesink en de
  "unconfigured"-placeholder zijn verboden buiten Development) — praktisch betekent dit:
  `Email:Smtp:*` moet ingevuld zijn, met `UseTls = true`;
- `ColumnEncryption:Key` ontbreekt (bijzondere-categorie-identifiers zouden anders als
  platte tekst opgeslagen worden);
- de JWT-signingkey ontbreekt, korter dan 32 bytes is of op de deny-list staat
  (`JwtOptionsValidator`, `ValidateOnStart`).

Overige runtimekenmerken:

- Kestrel request-body-plafond 32 MB (uploads begrensd per endpoint).
- `App_Data/` onder de content root is de bestandsopslag (`LocalFileStorageService`) voor
  álle documenten, POD-handtekeningen, Peppol-payloads enz. Deze map moet persistent zijn,
  buiten de webroot liggen en in de back-upscope zitten (checklistpunten 16/21).
- Buiten Development staan HSTS en HTTPS-redirect aan in de app zelf.

### 3.2 Frontend

```bash
cd TransportationService.Web
npm ci
npm run build        # = tsc -b && vite build → dist/
```

- **`VITE_API_BASE_URL`** wordt tijdens de build ingebakken (`src/config/env.ts`); zonder
  deze variabele valt de bundle terug op de lokale dev-API. Zet hem dus vóór `npm run build`
  op de publieke API-URL.
- De build is een statische SPA (`dist/`) met PWA-shell: `public/manifest.webmanifest` en
  `public/sw.js` (serviceworker wordt alleen in productiebuilds geregistreerd). Serveer
  `index.html` als SPA-fallback voor alle niet-bestandsroutes en cache `sw.js` kort
  (anders blijven clients op een oude shell hangen).

### 3.3 Serving en reverse proxy

Uit `appsettings.Production.json` blijkt de aangenomen topologie:

- Eén publiek domein (huidige demo: `https://tms-demo.vanmalderstudio.be`) dat zowel in
  `Cors:AllowedOrigins` als `Frontend:BaseUrl` staat. `Frontend:BaseUrl` wordt gebruikt om
  activatie-/resetlinks in e-mails te bouwen — moet dus exact het publieke SPA-adres zijn.
- Een reverse proxy **op dezelfde host** (`Network:KnownProxies: ["127.0.0.1"]`) die TLS
  termineert en `X-Forwarded-For`/`X-Forwarded-Proto` doorgeeft. De app vertrouwt forwarded
  headers uitsluitend van expliciet geconfigureerde proxy's (`Network:KnownProxies` /
  `Network:KnownNetworks`, `ForwardLimit = 1`) — een verkeerd geconfigureerde proxy breekt
  de per-IP rate limiting op de auth-endpoints.
- CORS staat nooit op wildcard; buiten Development betekent een lege originlijst
  "fail closed".

Het deploymentscript staat sinds het migratie-incident van 2026-08 **in de repo**
(`scripts/deploy-transportationservice.sh`) en wordt op de server geïnstalleerd met:

```bash
sudo install -m 0755 scripts/deploy-transportationservice.sh /usr/local/bin/deploy-transportationservice.sh
```

Uitvoeren: `ssh deploy@<server>` gevolgd door
`sudo /usr/local/bin/deploy-transportationservice.sh [git-ref]` (default `nav-redesign`).

### 3.4 Releasevolgorde (afgedwongen door het script)

Het script (`set -Eeuo pipefail`, ERR-trap met stap + exitcode + back-uplocatie,
getimestampte stappen, exitcodes nooit gemaskeerd door pipes/`tail`) dwingt deze
volgorde af — de kern is dat **migraties ALTIJD vóór de herstart lopen**:

1. Git-werkboom moet schoon zijn (anders stopt de deploy).
2. `pg_dump -Fc` back-up naar `/var/backups/transportationservice/` (chmod 600).
3. Gevraagde branch/commit ophalen (`fetch` + `--ff-only`).
4. `dotnet restore`.
5. `dotnet build -c Release` — de ENIGE compilatie; EF hergebruikt ze.
6. `npm ci`.
7. `npm run build` (faalt hard als `VITE_API_BASE_URL` ontbreekt in de env).
8. `nginx -t`.
9. De omgeving komt uit **hetzelfde** bestand als systemd
   (`/etc/transportationservice/tms.env`, EnvironmentFile-formaat) en wordt geladen
   zonder ooit waarden te tonen — EF ziet dus exact de productieconnectionstring.
10. Connectiviteitscheck (`SELECT 1`).
11. **Preflight eigenaarschap** (§3.5) + openstaande migraties tonen
    (`dotnet ef migrations list --no-build --configuration Release`).
12. `dotnet ef database update --project TransportationService.Api
    --startup-project TransportationService.Api --no-build --configuration Release`
    — additieve migraties in EF-volgorde; **nooit** handgeschreven `ALTER TABLE`,
    **nooit** migraties handmatig als toegepast markeren. Geen openstaande = gewoon door.
13. Faalt de migratie → het script stopt onmiddellijk vóór elke herstart: de vorige
    release blijft draaien (symlink onaangeroerd), de foutmelding noemt de stap én de
    back-uplocatie.
14. Publiceren naar een NIEUWE release-map + symlink-switch
    (`/opt/transportationservice/current`), frontend-`dist/` naar de webroot,
    `systemctl restart transportationservice-api`. De vorige release-map blijft staan
    voor handmatige terugrol (het script print het terugrolcommando bij falen).
15. Wachten op opstart (startup-seeders §2.1 draaien automatisch en idempotent).
16. `systemctl is-active` moet slagen.
17. Lokale healthcheck: de API heeft bewust geen `/health`; het script accepteert elke
    HTTP-status < 500 op `/api/auth/me` (401 zonder token = levend proces).
18. Pas na ál die checks: samenvatting met commit, release-map, back-uppad,
    migratieresultaat, servicestatus en healthcheckresultaat.

Daarna handmatig: rol-/permissiesync (waarschuwing §2.2) en verificatiechecklist §6.
Secrets worden nooit geëcho'd of gelogd; het env-bestand wordt nooit ge-`cat` naar de
uitvoer; er staan geen credentials in het script (alles komt uit het env-bestand).

### 3.5 Incident 2026-08: tabel-eigenaarschap en migratievolgorde

Twee productielessen, allebei structureel afgedekt:

1. **Code vóór migraties.** Een deploy herstartte de API vóór `dotnet ef database
   update`; de nieuwe code verwachtte kolommen die nog niet bestonden en de service
   crashte bij opstart. Het script dwingt sindsdien de volgorde back-up → build →
   migraties → pas dán herstart af, en stopt bij een migratiefout vóór elke herstart.
2. **Eigenaarschap.** Migraties draaien als databankgebruiker `tms`, maar de public-
   applicatietabellen bleken eigendom van `postgres` — `ALTER TABLE` door migraties
   faalt dan. Dit is destijds eenmalig handmatig gecorrigeerd (alle 171 public-tabellen
   zijn nu van `tms`). Het script controleert dit voortaan als **preflight vóór de
   migraties**: is ook maar één public-tabel niet van de migratiegebruiker, dan stopt
   de deploy met de tabellijst en een aanwijzing. Het script wijzigt eigenaarschap
   bewust **nooit zelf** (en gebruikt géén breed `REASSIGN OWNED`): herstel is een
   bewuste beheerdersactie, per tabel na review
   (`ALTER TABLE public."<tabel>" OWNER TO tms;`).
   **Provisioning/restores** moeten objecten meteen met de juiste eigenaar aanmaken:
   verbind als `tms`, of gebruik `pg_restore --no-owner --role=tms`.

---

## 4. Omgeving & configuratie

Alle secrets via omgevingsvariabelen (`Sectie__Sleutel`) of een vault; in Development via
`dotnet user-secrets`. **Nooit** echte waarden in `appsettings*.json` of de repo (zie §8).

| Sleutel | Betekenis |
|---|---|
| `ConnectionStrings:DefaultConnection` | Npgsql-connectionstring naar PostgreSQL 16. Leeg in alle getrackte configs. |
| `Jwt:Issuer` / `Jwt:Audience` | Token-issuer/-audience; staan per omgeving in appsettings. |
| `Jwt:SigningKey` | **Secret.** Symmetrische HMAC-SHA256-key, ≥ 32 bytes. Startup faalt bij ontbreken, te kort of deny-listed (oude gelekte dev-key). |
| `Jwt:PreviousSigningKey` | Optioneel, alleen tijdens een rotatievenster: oude key blijft geldig voor **validatie**, nieuwe tokens gebruiken de nieuwe key. Verwijderen na het venster. |
| `Jwt:KeyId` / `Jwt:PreviousKeyId` | `kid`-stempels (default `primary`/`previous`) zodat validators de juiste key kiezen. |
| `Jwt:AccessTokenMinutes` | Levensduur access token (code-default 15; appsettings zetten 60). |
| `Jwt:RefreshTokenDays` | Levensduur refresh token (default 14; HttpOnly-cookie). |
| `Security:Authentication:*` | Lockoutknoppen: `MaxFailedLoginAttempts` (8), `BaseLockoutMinutes` (5, verdubbelt), `MaxLockoutMinutes` (60), `MaxActiveSessionsPerUser` (10), `RefreshTokenRetentionDays` (30). |
| `Security:PasswordPolicy:*` | Gedeeld wachtwoordbeleid (gebonden met `ValidateOnStart`). |
| `ColumnEncryption:Key` | **Secret.** Base64, exact 32 bytes; AES-256-GCM kolomversleuteling van bijzondere-categorie-identifiers (rijksregisternummer, identiteitskaartnummer). Zonder key = pass-through, alleen toegestaan in Development. |
| `ColumnEncryption:PreviousKeys` | Array van oudere keys; alleen voor **lezen** tijdens rotatie. Nieuwe writes gebruiken altijd de actieve key. |
| `Email:Smtp:Host/Port/Username/Password/FromAddress/FromName/UseTls` | **Password = secret.** SMTP-provider voor de mail-outbox. Buiten Development verplicht (anders start de host niet); `UseTls` moet `true` zijn. Zonder `Host` in een niet-dev-omgeving wordt de fail-closed placeholder geregistreerd en weigert de startupvalidator te booten. SMS heeft nog geen echte provider (fail-closed `UnconfiguredSmsProvider`). |
| `Peppol:Webhook:Secret` | **Secret.** Shared secret voor `POST /api/peppol/webhook/{providerKey}` (header `X-Peppol-Webhook-Secret`, constant-time vergelijking). Zonder enig secretmateriaal weigert het endpoint alles. |
| `Peppol:Webhook:PreviousSecrets` | Array; rotatievenster voor het shared secret. |
| `Peppol:Webhook:Hmac:Secret` | **Secret.** Activeert de sterkere HMAC-modus: HMAC-SHA256 over `"{timestamp}.{body}"` via headers `X-Peppol-Timestamp` + `X-Peppol-Signature`, met replay-window. |
| `Peppol:Webhook:Hmac:PreviousSecrets` | Array; HMAC-rotatievenster. |
| `Peppol:Webhook:Hmac:ToleranceSeconds` | Replay-window in seconden (default 300). |
| `Peppol:Webhook:Providers:{providerKey}:…` | Per-provider scoping: definieert deze sectie secretmateriaal, dan vervangt zij het globale `Peppol:Webhook`-materiaal volledig voor die provider (een secret van provider A werkt nooit op de route van provider B). |
| `Peppol:Providers:{key}:…` | Gereserveerd voor echte Access-Point-adapters (endpoint, credentials) volgens het options-patroon; momenteel is alleen de `sandbox`-provider geregistreerd. Provider- en omgevingskeuze zelf zit **per juridische entiteit in de database** (`PeppolSettings`: Enabled, Environment Sandbox/Live, ProviderKey), beheerd via de UI (`peppol.configure`). |
| `Retention:LegalHold` | `true` = elke geautomatiseerde purge opgeschort (litigation hold). |
| `Retention:OutboxRetentionDays` (365), `Retention:SecurityTokenRetentionDays` (30), `Retention:SinkFileRetentionDays` (14) | GDPR-retentievensters voor de 24-uurs-sweep. Auditlogs worden nooit door de app gepurged (append-only trigger; checklistpunt 29). |
| `Cors:AllowedOrigins` | Expliciete originlijst; leeg buiten Development = niets toegestaan. |
| `Frontend:BaseUrl` | Publieke SPA-URL, gebruikt in gemailde links. |
| `Network:KnownProxies` / `Network:KnownNetworks` | Vertrouwde reverse-proxy-adressen/-netwerken voor forwarded headers. |
| `Dev:AllowImpersonationHeaders` | Alleen Development; elke andere omgeving weigert te starten met deze vlag aan. |
| `AllowedHosts` | Standaard ASP.NET-hostfilter (`*` in basisconfig; op de edge beperken). |
| `POSTGRES_PASSWORD` (docker-compose) | Wachtwoord van de lokale dev-database. |
| `VITE_API_BASE_URL` (frontend, buildtime) | Publieke API-basis-URL, ingebakken in de SPA-build. |

---

## 5. Achtergrondjobs & hosted services

Alle jobs zijn `BackgroundService`-implementaties met `PeriodicTimer`, één DI-scope per
tick, fouten gelogd zonder de lus te breken. Ze draaien **in-process** in de API — één
draaiende API-instantie volstaat; bij meerdere instanties draaien de sweeps dubbel
(idempotent, maar houd er rekening mee). Volledige lijst, geverifieerd in code
(intervallen uit de bronbestanden; zie ook `docs/background-jobs.md`):

| Hosted service | Interval | Functie |
|---|---|---|
| `OutboxDispatcherHostedService` | 30 s | E-mail-/sms-outbox afleveren (backoff 5·2ⁿ min, max 5 pogingen, quiet hours, fallbackkanaal) |
| `PeppolDispatcherHostedService` | 30 s | Peppol-transmissies indienen en providerstatus pollen |
| `CalendarSyncHostedService` | 60 s | Agenda-syncqueue (goedgekeurd verlof, bevestigde shifts) naar `ICalendarProvider` (nu: fake provider) |
| `ExpiryNotificationHostedService` | 6 u | Vervaldata kwalificaties, wagenparkdocumenten, tankkaarten → notificaties |
| `TokenRetentionHostedService` | 6 u | Opruimen verlopen/ingetrokken refresh tokens |
| `GdprRetentionHostedService` | 24 u | GDPR-retentiepurge volgens `Retention:*` (volledig bevroren bij `LegalHold`) |
| `InventorySweepHostedService` | 1 u | Voorraadstatus-reconciliatie, retoursignalen, escalaties (per tenant) |
| `TaskSweepHostedService` | 15 min | Taakherhalingen, deadline-notificaties, geplande berichten, escalaties (per tenant) |
| `NotificationMaintenanceHostedService` | 6 u | Verlopen notificaties archiveren; gearchiveerd > 180 dagen soft-deleten (batches van 500) |

Er bestaat **geen** aparte ETA-hosted-service: ETA-berekening en de bijbehorende
notificaties (drempel `TenantSettings.EtaShiftNotifyMinutes`, sinds de afrondingsgolf
instelbaar via de bedrijfsinstellingen-UI) lopen synchroon mee in `EtaService` tijdens
rituitvoering — inclusief het zetten van ETA's en de "chauffeur onderweg"-berichten bij
ritstart. Ook de controlewachtrij voor gevoelige klantmail (`AwaitingReview`) vergt geen
aparte job: vastgehouden berichten worden pas door de bestaande `OutboxDispatcher`
opgepikt nadat iemand ze vrijgeeft.

---

## 6. Productieverificatie-checklist

Er is momenteel **geen** dedicated health-endpoint (`MapHealthChecks` is niet
geconfigureerd) en OpenAPI/Scalar is alleen in Development gemapt. Basale liveness:
een ongeauthenticeerde `GET /api/...` moet een `401 application/problem+json` teruggeven —
dat bewijst dat Kestrel, de middleware-pipeline en de securityheaders staan.

Na elke deploy:

1. **Opstartlog controleren** — geen exceptions van `StartupSecurityValidator`; regels van
   `DossierBackfillSeeder` ("created N wrapper dossiers" of stil bij 0) en geen
   seeder-fouten.
2. **Migratiestand** — `__EFMigrationsHistory` eindigt op `20260812175816_InvoiceSnooze`
   (§1.3).
3. **Login** — `POST /api/auth/login` met een geldig account: 200, access token + HttpOnly
   refresh-cookie; fout wachtwoord: 401 en (na herhaling) rate limiting/lockout.
4. **Dossier aanmaken** — via de UI (dossierlijst is de landingspagina) een dossier snel
   aanmaken; dossiernummer volgt de tenantreeks.
5. **Order aanmaken** — binnen het dossier een transportactiviteit/order aanmaken; controle:
   dossier toont de activiteit, `Version`-token muteert mee (een stale mutatie geeft 409).
6. **Prijsberekening** — op de order een prijsberekening uitvoeren: pricing-snapshot met
   coverage-status verschijnt; geen "geen dekking"-verrassingen op een tarifair gedekte
   klant.
7. **Scan** — een pakketbarcode (of losse magazijnscan zonder order, Wave 4) scannen:
   scan-event geregistreerd, magazijnlocatie bijgewerkt.
8. **Factuur** — vanuit een afgeronde order een factuur genereren: nummer in de reeks van de
   juridische entiteit, PDF downloadbaar; optioneel Peppol "Valideren" op de factuur
   (sandbox-provider).
9. **Portaallogin** — klantportaalaccount inloggen: dashboard, orders en facturen zichtbaar;
   interne notities/foutdetails niet.
10. **Achtergrondjobs** — na ±2 minuten logregels van `OutboxDispatcher`/`PeppolDispatcher`;
    testmail (bv. wachtwoordreset) komt via SMTP aan.
11. **Headers/proxy** — respons bevat de securityheaders; `X-Forwarded-For` van de proxy
    wordt gehonoreerd (rate-limit-partitionering per echt client-IP).

Voor uitgebreidere smoke-runs bestaat het vaste patroon van `smoke-*.mjs`-scripts (Node,
login + API-flows) uit eerdere milestones; die staan buiten de repo in sessie-scratchpads.

---

## 6b. Systeeminformatie, versies en back-upbeheer (settings/system-wave 2026-08)

### Versiestrategie

- Het semver staat op ÉÉN plek: `Directory.Build.props` (`<Version>0.2.0</Version>`).
  Releases bumpen alleen dat bestand.
- Het deployscript stempelt de commit met `-p:SourceRevisionId=<sha>`; de
  InformationalVersion wordt "0.2.0+<sha>" en de Systeeminformatie-pagina splitst dat in
  versie + build. `deployment.json` in de release-map (geschreven door het script) is de
  waarheid over wat LIVE draait — git HEAD is hooguit een kandidaat.
- Frontendbuilds krijgen `VITE_BUILD_COMMIT` mee voor dezelfde traceerbaarheid.

### Systeeminformatie

`GET /api/system-info` (permissie `system_info.view`; pagina Instellingen →
Systeeminformatie): omgeving, versie, build, laatste deployment, API-/databankstatus met
latency, laatst toegepaste migratie en openstaande migraties. Verbindingsdetails lekken
nooit; een onbereikbare databank toont enkel "Unavailable".

### Back-upbeheer

- Opslag: `Backups:Directory` (default `App_Data/backups`, altijd buiten de webroot);
  `Backups:ExternalDirectories` kan de dumpmap van het deployscript alleen-lezen tonen
  (type Pre-deployment). `Backups:PgDumpPath`/`PgRestorePath` wijzen naar de binaries.
- Permissies: `backups.view` (rollen v29: management) en de bewuste per-beheerder-rechten
  `backups.create/download/delete/restore`. Delete en restore zijn bovendien fail-closed
  in de service zelf.
- Retentie: `Backups:AutomaticRetentionDays` / `PreRestoreRetentionDays` (default 30);
  de nieuwste voltooide back-up en een lopende restore zijn beschermd; handmatige
  back-ups worden nooit automatisch verwijderd. Dagelijkse automatische back-up via
  `Backups:AutomaticEnabled` (default aan) op `Backups:AutomaticHourUtc` (default 02:00).
- **Terugzetten** (nooit "activeren"): vereist het exact getypte bestandsnaam als
  bevestiging, maakt éérst een veiligheidsback-up (bron PreRestore), sluit andere
  databankverbindingen, draait `pg_restore --clean --if-exists --exit-on-error` en doet
  een healthcheck. Herstart daarna de API-service gecontroleerd (seeders/caches) en werk
  verificatiechecklist §6 af. Alles is geauditeerd (create/download/delete/restore/
  retentie-opruiming).

## 7. Rollback-overwegingen

**Uitgangspunt: code eerst terugrollen, database laten staan.** Alle redesign-migraties
zijn additief (§1.2): de vorige applicatieversie draait probleemloos tegen het nieuwe
schema, want zij kent de nieuwe tabellen/kolommen simpelweg niet. Dat is de veilige en
aanbevolen rollbackroute.

Een **database-rollback** (`dotnet ef database update <vorige-migratie>`) is destructief en
alleen te overwegen met een verse back-up en een expliciet besluit:

- `Down()` van `DossierActivityFoundation` verwijdert `activity_types` en
  `dossier_activities` — en daarmee álle dossieractiviteiten, inclusief de wrappers die de
  backfill (§2.1) heeft aangemaakt.
- `Down()` van `CommercialFoundation` dropt 14 kolommen (o.a. factuurgereedheid,
  coverage-status, verkoopcategoriekoppelingen) en `customer_allowed_legal_entities`.
- `StorageStays`, `TenantHolidays` en `WarehouseLocations` verdwijnen met hun volledige
  inhoud (opslagverblijven, feestdagkalenders, magazijnlocaties + locatiestempels op
  pakketten/scans).
- `StandaloneScanOrderNullable` terugdraaien maakt de orderkolom op `scan_events` weer
  verplicht — dat **faalt** zodra er losse magazijnscans (NULL-waarden) bestaan.
- `ProblemsResponsibilityCharge` en `EtaShiftThreshold` terugrollen wist respectievelijk
  verantwoordelijkheids-/doorrekenbeslissingen op incidenten en de ETA-notificatiedrempel.
- De zes migraties van de afrondingsgolf (nr. 9–14) terugrollen verwijdert de
  documentregels, het doorrekenbeleid en de importprofielen/-batches met hun volledige
  inhoud, plus de documentkeuze-, uitrusting- en uitstelkolommen op orders en de
  koppeling mislukte stop → incident.

Let op: seeder-effecten (wrapper-dossiers, activiteitstypes, geclaimde dossiernummers) staan
buiten het migratiemechanisme en worden door een schema-rollback deels vernietigd, deels
achtergelaten. Ook daarom: bij problemen binaries terugzetten, niet het schema.

---

## 8. Veilig secretbeheer

### 8.1 Bronnen

- **Development**: `dotnet user-secrets` (zie `docs/security/dev-setup.md` voor de exacte
  commando's voor `Jwt:SigningKey` en de connection string). Getrackte
  `appsettings*.json`-bestanden bevatten uitsluitend lege placeholders.
- **Productie**: omgevingsvariabelen of een vault (checklistpunt 2). Geen
  `appsettings.*` met echte waarden in de publish-output.
- CI/lokaal draait `node scripts/secret-scan.mjs` over alle git-getrackte bestanden
  (exit 1 bij vondsten; waarden worden nooit geprint).

> **Actiepunt:** de bestanden `StartUp.txt`/`StartUp.local.txt` in de repo-root bevatten
> momenteel plaintext secrets (JWT-key, kolomencryptiekey, databasewachtwoord en een echt
> gebruikerswachtwoord), en `StartUp.local.txt` is git-getrackt. Behandel al deze waarden
> als gecompromitteerd: roteren, bestanden uit tracking halen en de history-purge van
> checklistpunt 4 meenemen. Dit document herhaalt die waarden bewust nergens.

### 8.2 Rotatieprocedures (zero-downtime, allemaal in code voorzien)

**JWT-signingkey** — zet de oude key in `Jwt:PreviousSigningKey` (+ `PreviousKeyId`), de
nieuwe in `Jwt:SigningKey`, herstart. Bestaande tokens blijven geldig tot ze verlopen
(access tokens zijn kort); na het venster `PreviousSigningKey` verwijderen. De ooit gelekte
dev-key staat op een deny-list en kan geen enkele omgeving meer starten.

**Kolomencryptiekey** — nieuwe key in `ColumnEncryption:Key`, oude key(s) in
`ColumnEncryption:PreviousKeys`. Nieuwe writes versleutelen met de actieve key; reads vallen
terug op de vorige keys tot de data doorgeschreven is. Legacy-plaintextrijen blijven leesbaar
en worden bij hun eerstvolgende write versleuteld (geen big-bang-migratie).

**Peppol-webhooksecrets (M2)** — beide modi ondersteunen rotatie via een keyring:

1. Nieuw secret zetten in `Peppol:Webhook:Hmac:Secret` (of `…:Secret` in shared-secret-modus)
   en het oude verplaatsen naar `…:Hmac:PreviousSecrets` (resp. `…:PreviousSecrets`).
2. Provider het nieuwe secret geven.
3. Na het rotatievenster de `PreviousSecrets` verwijderen.

HMAC-modus wint zodra een `Hmac:Secret` bestaat en vereist een timestamp binnen het
replay-window (`Hmac:ToleranceSeconds`, default 300 s); replays binnen het window worden
geneutraliseerd door idempotente verwerking (dedupe op provider-message-id). Zonder enig
geconfigureerd secretmateriaal weigert het webhook-endpoint álles (secure by default).
Bij meerdere providers: per-provider secrets via `Peppol:Webhook:Providers:{key}` — die
sectie vervangt het globale materiaal voor die provider volledig.

**Databasewachtwoord** — roteren in PostgreSQL en gelijktijdig de
`ConnectionStrings__DefaultConnection`-variabele bijwerken + herstart.

### 8.3 Kruisverwijzingen

Release-blocking secretgerelateerde punten uit `docs/security/operational-checklist.md`:
punt 1 (omgevingsvlag), 2 (secretbron + webhooksecrets), 3 (JWT-rotatie na de gelekte
dev-key), 6 (TLS), 9 (back-upencryptie), 19 (echte SMTP/SMS/Peppol-provider), 21
(`App_Data` niet publiek). GDPR-kant: `docs/security/gdpr.md`.
