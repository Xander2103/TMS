# Operations & Deployment — TransportationService

Doelgroep: platform-/applicatiebeheer. Stack: .NET 10 (ASP.NET Core API), EF Core 10,
PostgreSQL 16, React + Vite (SPA, PWA-shell). Oorspronkelijk geverifieerd tegen de code van
2026-08-12; §1.1, §1.3, §2.1, §2.3, §3, §4, §5, §6, §6b en §7 zijn op 2026-10-19 herschreven
voor de production-hardening (deployscript, forward-only releases, duurzame opslag).

Verwante documenten:

- `scripts/deploy-transportationservice.sh` — het deployscript (§3.4); lokale simulatie:
  `scripts/deploy-sim/run.sh`
- `deploy/systemd/`, `deploy/nginx/` — voorbeeldunit, nginx-site en onderhoudspagina (§3.3)
- `docs/storage.md` — duurzame bestandsopslag (`Storage:RootPath`) en migratie van `App_Data/`
- `docs/operations/backup-restore.md` — off-host back-up en offline restore-runbook
- `docs/operations/provisioning.md` — nieuw bedrijf / eerste beheerder (`--provision-tenant`)

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

Twee eigenschappen die elke productie-upgrade bepalen (geverifieerd, production-readiness
MIG-02/MIG-03):

- **EF commit per migratie.** Elke migratie loopt in haar eigen transactie. Faalt migratie *k*,
  dan rolt alleen *k* terug; 1..*k*-1 blijven toegepast. Een herstart van `database update`
  hervat bij *k*. Er is dus géén "alles of niets".
- **Standaard command timeout = 30 s per commando.** Grote `UPDATE`s en tabelherschrijvingen
  overschrijden dat op een productiegrote database. Het deployscript voegt daarom **alleen voor de
  migratiestap** `Command Timeout=0` (`MIGRATION_COMMAND_TIMEOUT`) en
  `Options=-c lock_timeout=60s` (`MIGRATION_LOCK_TIMEOUT`) toe aan de connectionstring: geen
  tijdslimiet op het werk zelf, wel snel falen als een andere sessie een lock vasthoudt. Handmatig
  migreren: doe hetzelfde, bv.
  `ConnectionStrings__DefaultConnection="<conn>;Command Timeout=0;Options=-c lock_timeout=60s"`.

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
| `20260830133955_StopWindowTenantZoneReencoding` | Her-encodeert de acht tijdvenstercolommen van `transport_order_stops` van "wandklok gestempeld als UTC" naar echte UTC-instants, per tenant-tijdzone. Vensters die **mét een inkomend EDI-bericht** zijn meegekomen worden per rij uitgesloten (die dragen al een echte instant); een venster dat later in het webformulier op zo'n opdracht is ingetypt wordt wél omgezet. |
| `20260830134439_PackageStopPinRepair` | Herstelt `packages."LoadingStopId"/"DeliveryStopId"` die naar een soft-deleted stop wijzen, maar uitsluitend waar de match eenduidig is (zelfde opdracht, `Sequence`, `StopType` én plaats). |

Beide schrijven hun tellingen naar `audit_logs` (`EntityType = 'DataMigration'`), één rij per
tenant: `RAISE NOTICE` van PostgreSQL is **niet zichtbaar** via `dotnet ef database update`, en dat
is hoe `scripts/deploy-transportationservice.sh` migreert. De tellingen zijn dus ook achteraf nog
opvraagbaar, en zichtbaar in de auditpagina. `audit_logs` is append-only (trigger
`trg_audit_logs_append_only`): deze rijen kunnen niet meer verwijderd worden, en een
`Down`→`Up`-cyclus stapelt generaties op. Sorteer daarom altijd op `Timestamp`.

Beide migraties vereisen **PostgreSQL 13 of hoger** (`gen_random_uuid()`) en bewaken dat zelf met
een `RAISE EXCEPTION` bovenaan het `DO`-blok — de migratie faalt dan meteen en volledig, in plaats
van halverwege.

**Twee harde randvoorwaarden:**

1. Corrigeer een onbruikbare `tenant_settings."Timezone"` **vóór** het toepassen (query 2/3), en
   **wijzig die instelling daarna niet meer** zolang een rollback nog mogelijk is: `Down`
   herberekent de zone uit de *huidige* instelling en zou dan met de verkeerde offset terugdraaien.
2. Reken vóór het toepassen query 4b uit (EDI). Levert die rijen op, beslis dan expliciet per rij
   in plaats van de migratie te laten beslissen.

#### Vóór toepassen (op een **teruggezette kopie** van productie, nooit live)

```sql
-- 0. Serverversie: beide migraties eisen >= 13.
SHOW server_version;

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

-- 2. BLOKKEREND. Tenants met een tijdzone die PostgreSQL niet kent (de migratie gebruikt dan
--    Europe/Amsterdam terwijl de API de .NET-interpretatie kan gebruiken). Corrigeer vooraf.
SELECT ts."TenantId", ts."Timezone"
  FROM tenant_settings ts
 WHERE lower(btrim(ts."Timezone")) NOT IN ('utc', 'etc/utc', 'universal', 'zulu')
   AND NOT EXISTS (SELECT 1 FROM pg_timezone_names z
                    WHERE lower(z.name) = lower(btrim(ts."Timezone")));

-- 3. BLOKKEREND. Tenants zonder tenant_settings-rij die wél stops hebben (idem: default
--    Europe/Amsterdam, zowel in SQL als in de C#).
SELECT t."Id", t."Name"
  FROM tenants t
  LEFT JOIN tenant_settings ts ON ts."TenantId" = t."Id"
 WHERE ts."Id" IS NULL
   AND EXISTS (SELECT 1 FROM transport_order_stops s WHERE s."TenantId" = t."Id");

-- 3b. Stops van een TenantId zonder tenants-rij: de migratie loopt over `tenants`, dus die worden
--     nooit omgezet en nooit gerapporteerd. Moet leeg zijn.
SELECT s."TenantId", count(*)
  FROM transport_order_stops s
 WHERE NOT EXISTS (SELECT 1 FROM tenants t WHERE t."Id" = s."TenantId")
 GROUP BY 1;

-- 4. Stopregels MET een venster die per rij als EDI-geschreven gelden. Dit getal hoort exact te
--    kloppen met de som van "skippedEdi" over de auditrijen (zelfde predikaat).
SELECT count(*)
  FROM transport_order_stops s
 WHERE (s."PlannedFrom" IS NOT NULL OR s."PlannedTo" IS NOT NULL
     OR s."RequestedFrom" IS NOT NULL OR s."RequestedTo" IS NOT NULL
     OR s."ConfirmedFrom" IS NOT NULL OR s."ConfirmedTo" IS NOT NULL
     OR s."EarliestAllowed" IS NOT NULL OR s."LatestAllowed" IS NOT NULL)
   AND EXISTS (SELECT 1 FROM edi_messages m
                WHERE m."TenantId" = s."TenantId" AND m."IsDeleted" = false
                  AND m."Direction" = 'Inbound' AND m."Status" = 'Processed'
                  AND m."ResultEntityType" = 'TransportOrder'
                  AND m."ResultEntityId" = s."TransportOrderId"::text
                  AND s."CreatedAt" <= coalesce(m."ProcessedAt", m."CreatedAt"));

-- 4b. BLOKKEREND, per rij. Elke stop met een venster op een EDI-aangemaakte opdracht, met de twee
--     tijdstippen waarop de uitsluiting berust. `stop_created <= edi_processed` = het venster kwam
--     mét het bericht mee (niet omzetten); later = in het webformulier ingetypt (wél omzetten).
--     Leeg resultaat ⇒ de uitsluiting is gratis en er hoeft niets beslist te worden.
SELECT s."Id", s."TransportOrderId",
       s."CreatedAt" AS stop_created,
       m."CreatedAt" AS edi_created,
       coalesce(m."ProcessedAt", m."CreatedAt") AS edi_processed,
       (s."CreatedAt" <= coalesce(m."ProcessedAt", m."CreatedAt")) AS treated_as_edi_written,
       s."PlannedFrom", s."RequestedFrom", s."ConfirmedFrom", s."EarliestAllowed"
  FROM transport_order_stops s
  JOIN edi_messages m
    ON m."TenantId" = s."TenantId" AND m."IsDeleted" = false
   AND m."Direction" = 'Inbound' AND m."Status" = 'Processed'
   AND m."ResultEntityType" = 'TransportOrder'
   AND m."ResultEntityId" = s."TransportOrderId"::text
 WHERE s."PlannedFrom" IS NOT NULL OR s."PlannedTo" IS NOT NULL
    OR s."RequestedFrom" IS NOT NULL OR s."RequestedTo" IS NOT NULL
    OR s."ConfirmedFrom" IS NOT NULL OR s."ConfirmedTo" IS NOT NULL
    OR s."EarliestAllowed" IS NOT NULL OR s."LatestAllowed" IS NOT NULL
 ORDER BY 1;

-- 5. Droogloop van de omzetting: verwacht -02:00:00 (zomer), -01:00:00 (winter) of 00:00:00 (UTC).
--    Elke andere waarde = stoppen en onderzoeken. De zonekeuze en de EDI-uitsluiting zijn identiek
--    aan de migratie: zonder de pg_timezone_names-guard breekt deze query af op precies de tenants
--    waarvoor die guard bestaat.
SELECT ((s."PlannedFrom" AT TIME ZONE 'UTC') AT TIME ZONE
          CASE WHEN ts."Timezone" IS NOT NULL
                AND EXISTS (SELECT 1 FROM pg_timezone_names z
                             WHERE lower(z.name) = lower(btrim(ts."Timezone")))
               THEN btrim(ts."Timezone") ELSE 'Europe/Amsterdam' END)
         - s."PlannedFrom" AS shift,
       count(*)
  FROM transport_order_stops s
  LEFT JOIN tenant_settings ts ON ts."TenantId" = s."TenantId"
 WHERE s."PlannedFrom" IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM edi_messages m
                    WHERE m."TenantId" = s."TenantId" AND m."IsDeleted" = false
                      AND m."Direction" = 'Inbound' AND m."Status" = 'Processed'
                      AND m."ResultEntityType" = 'TransportOrder'
                      AND m."ResultEntityId" = s."TransportOrderId"::text
                      AND s."CreatedAt" <= coalesce(m."ProcessedAt", m."CreatedAt"))
 GROUP BY 1 ORDER BY 2 DESC;

-- 6. Omvang van de verweesde colli-pins (C-01), vóór het herstel.
SELECT count(*) FROM packages p
 WHERE p."IsDeleted" = false
   AND (EXISTS (SELECT 1 FROM transport_order_stops d WHERE d."Id" = p."LoadingStopId"  AND d."IsDeleted")
     OR EXISTS (SELECT 1 FROM transport_order_stops d WHERE d."Id" = p."DeliveryStopId" AND d."IsDeleted"));
```

**Optionele voorbereidende index.** Het EDI-predikaat is een gecorreleerde subquery en
`edi_messages` heeft geen index op `("TenantId","ResultEntityId")`. Bij een grote `edi_messages` of
veel tenants is dat de dominante kostenpost. Vooraf, buiten de migratietransactie:

```sql
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_edi_messages_tenant_result
    ON edi_messages ("TenantId", "ResultEntityId");
-- Na afloop desgewenst: DROP INDEX CONCURRENTLY ix_edi_messages_tenant_result;
```

#### Na toepassen

```sql
-- A. De tellingen die de migraties zelf hebben vastgelegd.
SELECT a."Timestamp", a."TenantId", a."Action", a."OldValuesJson", a."NewValuesJson"
  FROM audit_logs a
 WHERE a."EntityType" = 'DataMigration'
 ORDER BY a."Timestamp" DESC;
```

Controleer per tenant in die rijen:

* `timezoneUsed` / `timezoneSource` zijn wat je verwacht (`tenant_settings`, niet `default (...)`,
  tenzij je dat bewust zo laat);
* **`converted` = `candidateStopRows`** voor elke niet-UTC-tenant. Een verschil betekent dat rijen
  vergrendeld waren of dat het predikaat is afgeweken — onderzoeken;
* `skippedEdi` klopt met query 4;
* `dstInvertedWindowsRepaired` / `dstInvertedStopIds`: vensters die over het lentegat vielen en
  omgekeerd uitkwamen. Ze zijn hersteld met behoud van hun **oorspronkelijke breedte**
  (`ondergrens_nieuw + originele breedte`), niet dichtgeklapt. De ids staan in de auditrij (max.
  500, met `dstInvertedStopIdsTruncated`) — dat is de enige plek waar ze terug te vinden zijn;
* `alreadyInvertedLeftUntouched` / `alreadyInvertedStopIds`: vensters die al vóór de migratie
  omgekeerd stonden. Die zijn **bewust niet aangeraakt** (de applicatie kan ze niet aanmaken; ze
  kunnen alleen van een directe DB-schrijfactie komen) en verklaren een niet-nul uitkomst van
  query B hieronder.

```sql
-- B. Omgekeerde vensters. Moet gelijk zijn aan de som van "alreadyInvertedLeftUntouched",
--    normaal 0. Elke andere waarde betekent dat de omzetting iets heeft gebroken.
SELECT count(*) FROM transport_order_stops
 WHERE "PlannedTo" < "PlannedFrom" OR "RequestedTo" < "RequestedFrom"
    OR "ConfirmedTo" < "ConfirmedFrom" OR "LatestAllowed" < "EarliestAllowed";

-- C. Restant verweesde colli-pins = "stillAmbiguous" uit A; die moeten met de hand nagekeken
--    worden (diagnosequery in het taakrapport task-5-report.md). Per tenant hoort te gelden:
--    danglingPackages - repairedLoadingPins - repairedDeliveryPins = stillAmbiguous.
SELECT count(*) FROM packages p
 WHERE p."IsDeleted" = false
   AND (EXISTS (SELECT 1 FROM transport_order_stops d WHERE d."Id" = p."LoadingStopId"  AND d."IsDeleted")
     OR EXISTS (SELECT 1 FROM transport_order_stops d WHERE d."Id" = p."DeliveryStopId" AND d."IsDeleted"));
```

Sluit af met `VACUUM (ANALYZE) transport_order_stops;` — de omzetting herschrijft elke rij met een
venster, wat de tabel tijdelijk ongeveer verdubbelt tot autovacuum bijtrekt, en de statistieken
verouderd achterlaat. Controleer daarna in de applicatie zelf, op de vijf schermen die nu een
wandklok tonen: een **opdrachtdetail** (tijdvensters + openingsurenwaarschuwingen), een
**collietiket** (ophaal-/leveruur), een **planningsvoorstel** met venster, een
**klant/dag-documentrun** met een levering vroeg in de ochtend, en een **prijsherberekening** op een
opdracht met een stop vlak naast het weekend (weekendtoeslag).

### 1.3 Verifiëren

Na `database update`:

```sql
SELECT "MigrationId"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId" DESC
LIMIT 10;
```

Verwacht: bovenaan de **nieuwste migratie in `TransportationService.Api/Migrations/` van de
gedeployde commit** (stand 2026-10-19: `20261018213037_CustomerPortalFinalRemediation`, 191
migraties), en `count(*)` gelijk aan het aantal migratiebestanden (zonder `.Designer.cs`). Het
deployscript controleert precies dat in stap 12 (0 openstaand volgens EF, nieuwste toegepast ==
nieuwste in de broncode, aantallen gelijk). Alternatief: `dotnet ef migrations list` (geen
`(Pending)`-markering), of Instellingen → Systeeminformatie (`schemaVersion`,
`pendingMigrations = 0`).

---

## 2. Seeding

### 2.1 Startup-seeders in **elke** omgeving

`Program.cs` draait bij elke start, ook in productie, **zeven** idempotente seeders/backfills, na
elkaar en **vóór** Kestrel luistert (de API is dus pas "ready" als ze klaar zijn; de eerste start
na een release met nieuwe backfills kan minuten duren — het deployscript wacht daarom op
`GET /health/ready`, max. `STARTUP_WAIT_SECONDS`, default 300 s). Een exception in een seeder
stopt het proces; systemd herstart (`Restart=on-failure`, max. 5 starts / 10 min).

Naast de vier hieronder beschreven draaien ook `AddressMasterBackfillSeeder` (adressleutels van
`locations`), `IssuedTransportDocumentSnapshotBackfill` (bevriest uitgegeven CMR/leverbon/
werkbon) en `InvoicePartySnapshotBackfill` (bevriest partijgegevens en UBL-notitie van
uitgegeven facturen); die laatste twee doen hun echte werk eenmalig bij de eerste start na de
release (N+1-queries per document/factuur, production-readiness MIG-06). De vier oudere:

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
4. **`AuthorizationCatalogSync.SyncAsync`** (closure sprint 2026-09-23) — de release-flow voor
   autorisatiedata: eerst `PermissionCatalogSeeder.SyncAsync` (catalogus volgt `PermissionCodes`,
   systeemrollen krijgen de volledige catalogus), daarna `DefaultRoleSeeder.SyncAsync` (rolsjablonen
   en versie-upgrades, zie §2.3). Idempotent en tenant-veilig; een release heeft dus **geen**
   handmatige databasestap meer nodig voor nieuwe permissiecodes of rol-upgrades. De sync logt
   op Information wanneer er iets veranderde (aantal permissies vóór/na, laagste sjabloonversie).

5. **`TenantReferenceDataSync.SyncAsync`** (productiehardening B3, 2026-10-19) — de
   add-if-missing **referentie**-seeders voor bestaande tenants: `QualificationTypeSeeder`
   (globale kwalificatiecatalogus), `ReferenceDataSeeder` zonder geprijsde startlijsten,
   `ExpiryPolicySeeder` en — alleen voor een tenant zonder — `LegalEntitySeeder` uit de eigen
   bedrijfsinstellingen. Soft-verwijderde rijen tellen als aanwezig (tenantbeheer wordt nooit
   ongedaan gemaakt); nooit gebruikers, rollen of demodata; een fout wordt gelogd en blokkeert
   de start niet.

**Nieuw bedrijf / eerste beheerder:** niet via een seeder maar via het expliciete commando
`dotnet TransportationService.Api.dll --provision-tenant …` — zie
[`docs/operations/provisioning.md`](../operations/provisioning.md).

`ActivityTypeSeeder` wordt daarnaast lazy aangeroepen op runtime-paden
(`ActivityTypeService`, snelle orderaanmaak in `TransportOrderService`), dus ook een tenant
die ná de deploy wordt aangemaakt krijgt zijn catalogus automatisch.

### 2.2 Startup-seeders **alleen in Development**

Binnen het `IsDevelopment()`-blok in `Program.cs` draaien bij elke start:

- `MasterDataSeeder.SeedAsync` — maakt de dev-tenant + gebruikers aan, **alleen** als de
  database nog géén tenants bevat.
- `AuthorizationCatalogSync.SyncAsync` — nogmaals, omdat de dev-tenant en -admin zonet door
  `MasterDataSeeder` aangemaakt kunnen zijn (de release-flow-sync in §2.1 draaide daarvóór).
- `ReferenceDataSeeder.SeedAsync` — starter-lookups.
- `RateCardConversionService.ConvertAsync` — eenmalige conversie van legacy rate cards naar
  pricing agreements (idempotent).
- `LegalEntitySeeder`, `ExpiryPolicySeeder`, `IssuedItemTemplateSeeder`.
- `DevAdminSeeder.EnsurePasswordAsync` — garandeert dat `admin@dev.local` een bruikbaar
  wachtwoord heeft (alleen wanneer nog geen wachtwoord gezet is; een bewust gewijzigd
  wachtwoord wordt nooit teruggezet).

> **Opgelost (closure sprint 2026-09-23):** tot deze release draaide de permissiecatalogus- en
> rolsjabloonsynchronisatie uitsluitend in Development, waardoor productie op rolsjabloon v24
> bleef en 20 codes (`dossiers.price`, `dossiers.override_entity`, `activity_types.*`,
> `locations.view_sensitive`, `order_imports.manage_profiles`, `problems.approve_charge`,
> `system_info.view`, `backups.*`, `attendance.*`) in de catalogus ontbraken. Sinds
> `AuthorizationCatalogSync` (§2.1) gebeurt dit in elke omgeving bij de eerste start na de
> deploy; `AuthorizationCatalogSyncTests` bewaakt het scenario en de plaats in `Program.cs`.

### 2.3 Rolsjablonen en versie-upgrades

`DefaultRoleSeeder` werkt in drie idempotente fasen (zie de klasse-documentatie in
`TransportationService.Api/Data/DefaultRoleSeeder.cs`):

1. **Backfill** — oude, ongestempelde standaardrollen krijgen eenmalig hun `TemplateCode`.
2. **Create** — ontbrekende sjabloonrollen worden per tenant aangemaakt met de volledige
   actuele permissieset; een gelijknamige tenant-eigen rol wordt met rust gelaten.
3. **Upgrade** — versiestappen uit `DefaultRoleUpgrades` (huidige
   `CurrentVersion = 40`) voegen nieuw geïntroduceerde standaardpermissies exact één keer
   per tenant toe aan gestempelde rollen; de toegepaste versie staat in
   `role_template_states`. Toevoegingen zijn add-if-missing: tenant-maatwerk (een naderhand
   weggehaalde permissie) overleeft elke re-run. **Uitzondering — bewuste intrekkingen:** v35
   haalt `absences.view/create` weg bij `chauffeur`, v36 `legal_entities.view` bij
   `planner`/`dispatcher` (securitycorrecties; vermeld ze in de releasenotes).

Let op bij elke codeversie: `PermissionCatalogSeeder` **verwijdert permissiecodes die de
draaiende code niet kent, met al hun toekenningen**. Daarom mag een oudere release nooit starten
op een database die al door een nieuwere release is gesynchroniseerd: na terug-vooruitrollen
krijgen sjabloonrollen die permissies niet terug (`AppliedVersion` staat al op 40). Dit is één
van de redenen waarom de release forward-only is (§3.7).

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
- **Bestandsopslag** (alle documenten, POD-handtekeningen/-foto's, personeelsdocumenten,
  Peppol-payloads, logo's, in-app back-ups) staat onder **`Storage:RootPath`**
  (`Storage__RootPath` in `tms.env`): een absoluut pad **buiten** elke release-/bin-map, bv.
  `/var/lib/transportationservice/storage`, eigenaar de service-gebruiker, `0750`, en in de
  back-upscope (checklistpunten 16/21). Buiten Development weigert de API te starten zonder
  geldige waarde; het oude `<ContentRoot>/App_Data` mag alleen nog via
  `Storage:AllowContentRootStorage=true`. Layout, aanmaak, de `--verify-storage`-integriteitscontrole
  en het migreren van een oude `App_Data/`-map: **`docs/storage.md`** (sectie *Bestandsopslag*).
  Het deployscript wijzigt deze map nooit en weigert te deployen als ze ontbreekt, relatief is,
  binnen een release-map ligt of niet schrijfbaar is (§3.4 stap 2).
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

Voorbeeldconfiguratie in de repo (paden aanpassen aan de server):

| Bestand | Doel |
|---|---|
| `deploy/systemd/transportationservice-api.service` | systemd-unit: `WorkingDirectory` = release-map, `EnvironmentFile=/etc/transportationservice/tms.env`, `ASPNETCORE_URLS=http://127.0.0.1:5019`, `After=postgresql.service`, `Restart=on-failure` met `StartLimitBurst=5`/`StartLimitIntervalSec=600`. |
| `deploy/nginx/transportationservice.conf` | nginx-site: TLS, SPA-fallback, `/api/` → 127.0.0.1:5019 met `X-Forwarded-For`/`X-Forwarded-Proto`, 32 MB body, onderhoudsmodus (§3.8), alleen `/health/live` publiek. |
| `deploy/nginx/maintenance.html` | onderhoudspagina (503) tijdens een deploy. |

> **EXACT ÉÉN API-INSTANTIE.** De API draait zijn achtergrondjobs (mail-outbox, Peppol,
> sweeps, retentie, automatische back-up) en zijn startup-seeders in-process. Een tweede
> instantie — een tweede unit, een template-unit `@N`, blue/green met twee levende processen, of
> een tweede host op dezelfde database — wordt voor de pilot **niet ondersteund**: mails en
> Peppol-verzendingen kunnen dubbel vertrekken en de seeders racen op unieke indexen. Ook niet
> "even" tijdens een deploy: het script stopt de oude instantie vóór de nieuwe start.

#### Serverlayout (wat het script verwacht)

| Pad | Inhoud |
|---|---|
| `/var/www/transportationservice/TMS` | git-checkout (eigenaar `deploy`), `REPO_DIR` |
| `…/TMS/TransportationService.Api/bin/Release/net10.0` | **release-map** (`API_BIN_DIR`): hier start systemd de API; wordt bij elke deploy met `rsync --delete` vervangen (altijd met `--exclude=/App_Data/`) |
| `…/TMS/TransportationService.Web/dist` | live frontend (nginx-root) |
| `/var/lib/transportationservice/storage` (voorbeeld) | **duurzame bestandsopslag** (`Storage__RootPath`), nooit binnen een release-map — `docs/storage.md` |
| `/var/www/transportationservice/backups` | `BACKUP_DIR`: finale deploydumps (`<db>-<ts>.dump` + `.toc`/`.log`), opslag-snapshots (`storage-<db>-<ts>.tar.gz`) en `<db>-<ts>.sha256`; retentie 10 dumps / 3 snapshots |
| `/var/www/transportationservice/releases/stage-<ts>-<sha>` | `STAGE_ROOT`: staging-build (`build/Release/net10.0`), frontend (`web/`), `previous-api/`, `previous-web/`, `migration.log`, `migration-timings.tsv`; de nieuwste 3 blijven staan |
| `/var/www/transportationservice/maintenance/maintenance.on` | onderhoudsvlag (`MAINTENANCE_FLAG`) |
| `/etc/transportationservice/tms.env` | EnvironmentFile (root:deploy 0640), §4 |
| `/home/deploy/.dotnet/tools/dotnet-ef` | EF-tool van `deploy` (buiten een deploy installeren) |

#### Installeren en uitvoeren

```bash
sudo install -m 0750 -o root -g root \
    /var/www/transportationservice/TMS/scripts/deploy-transportationservice.sh \
    /usr/local/bin/deploy-transportationservice.sh
sudo /usr/local/bin/deploy-transportationservice.sh <exacte-commit-sha>
```

De **git-ref is verplicht** (er is geen default-branch meer; zonder argument weigert het script,
exitcode 2). Gebruik bij voorkeur een exacte commit-sha. Bewaar de volledige uitvoer van elke
deploy (bv. `… 2>&1 | tee ~/deploy-<sha>.log`).

Belangrijkste knoppen (omgevingsvariabelen van het script, nooit secrets):

| Variabele | Default | Betekenis |
|---|---|---|
| `RESTORE_PREVIOUS_ON_FAILURE` | `0` | Forward-only. `1` heeft enkel effect bij een release **zonder** migraties waarvan het plaatsen van de binaries faalde vóór de eerste start; met openstaande migraties weigert het script (§3.7). |
| `STARTUP_WAIT_SECONDS` | `300` | Max. wachttijd op `GET /health/ready` = 200 na de start (eerste start draait backfills). |
| `MIGRATION_LOCK_TIMEOUT` | `60s` | `lock_timeout` voor de migratiestap. |
| `MIGRATION_COMMAND_TIMEOUT` | `0` | Npgsql `Command Timeout` voor de migratiestap (0 = geen limiet). |
| `MIGRATION_VERBOSE` | `0` | `1` = `dotnet ef database update --verbose` (SQL in `migration.log`). |
| `STORAGE_SNAPSHOT` | `1` | tar-snapshot van `Storage__RootPath` direct na de dump. `0` alleen als er een gelijktijdige off-host bestandsback-up is. |
| `MAINTENANCE_MODE` / `MAINTENANCE_FLAG` | `1` / zie layout | Onderhoudspagina tijdens de downtime (§3.8). |
| `READY_PATH` / `LOCAL_API_URL` | `/health/ready` / `http://127.0.0.1:5019` | Readiness-check. |
| `PUBLIC_URL` | `Frontend__BaseUrl` uit tms.env | Publieke eindcontrole. |
| `REPO_DIR`, `API_BIN_DIR`, `WEB_DIST_DIR`, `BACKUP_DIR`, `STAGE_ROOT`, `ENV_FILE`, `SERVICE_NAME` | zie layout | Paden. |

De externe commando's (`SYSTEMCTL`, `NGINX`, `PG_DUMP`, `PG_RESTORE`, `PSQL`, `DOTNET`,
`DOTNET_EF`, `NPM`, …) zijn overschrijfbaar; met `DEPLOY_SIMULATE=1` draait het script zonder root
tegen sandboxpaden. Daarop steunt de lokale simulatie `scripts/deploy-sim/run.sh` (echte
PostgreSQL 16 en dotnet-ef, gestubde systemctl/nginx; scenario's: geslaagde release,
migratiefout, opstartfout, lock-timeout, onveilige opslag, ontbrekende ref, …). Simulatiemodus
weigert de productiepaden.

### 3.4 Releasevolgorde (afgedwongen door het script)

Alles vóór stap 9 is read-only voor het live systeem: een fout daar laat de draaiende release
ongemoeid.

| # | Stap | Faalt → |
|---|---|---|
| 1 | Preflight: root, tools, deploy-gebruiker, repo, `tms.env` leesbaar, `dotnet-ef`, unit bestaat en `ExecStart` wijst naar `API_BIN_DIR`, lock. **Git-ref verplicht.** | weigering, niets aangeraakt |
| 2 | **Opslag-preflight**: `Storage__RootPath` (uit `tms.env`, anders uit de unit) gezet, absoluut, zonder `.`/`..`, niet binnen (en niet rond) `API_BIN_DIR`/`REPO_DIR`/`STAGE_ROOT`/`dist`/`WorkingDirectory`, bestaat, is een map, schrijfbaar voor de service-gebruiker (schrijfproef). Legacy `App_Data/` met bestanden in de release-map of `WorkingDirectory` → stop met verwijzing naar `docs/storage.md` (het script verplaatst nooit bestanden). | weigering, niets aangeraakt |
| 3 | Databaseverbinding via 0600-`PGPASSFILE`; PostgreSQL ≥ 13 verplicht (16 ondersteund), `pg_dump` niet ouder dan de server. | stop, niets aangeraakt |
| 4 | Git-werkboom schoon, `fetch`, checkout van de ref. **Schema vs. code**: staat er in `__EFMigrationsHistory` een migratie die deze code niet kent → weigering (dat zou oude code op een nieuwer schema zetten). Exacte set openstaande migraties = broncode − historiek; met `RESTORE_PREVIOUS_ON_FAILURE=1` en openstaande migraties → weigering. | weigering, niets aangeraakt |
| 5 | `dotnet restore` + `dotnet build -c Release` naar `stage-…/build/` (MSBuild `BaseOutputPath`), in een **schone omgeving zonder `tms.env`** (geen DB-wachtwoord, JWT-, kolom- of SMTP-sleutel). `deployment.json` (versie, commit, ref, tijd) in de build. | stop, niets aangeraakt |
| 6 | `npm ci` + `npm run build` naar `stage-…/web/`, schone omgeving met enkel `VITE_API_BASE_URL` (+ `VITE_BUILD_COMMIT`). | stop, niets aangeraakt |
| 7 | `nginx -t`. | stop, niets aangeraakt |
| 8 | DB-preflight: eigenaarschap (§3.5), `CREATE` op `public`, **master-data/wagenpark-preflight** (§3.6), en `dotnet ef migrations list` tegen de **gestagede** build: moet dezelfde openstaande set melden als stap 4. | stop, niets aangeraakt |
| 9 | **Downtime start**: onderhoudsvlag aan, `systemctl stop`, controle dat de service niet meer actief is. | vorige release (ongewijzigd) wordt herstart als hij draaide |
| 10 | **Finale** `pg_dump -Fc` (service gestopt = geen writes meer) → `pg_restore --list` moet de dump lezen en `__EFMigrationsHistory`-data bevatten; **opslag-snapshot** (`tar` van `Storage__RootPath`, zonder `backups/` en `message-sink/`) + `sha256`-bestand. | vorige release (ongewijzigd) wordt herstart als hij draaide |
| 11 | `dotnet ef database update --no-build -c Release` tegen de gestagede build, met `Command Timeout=0` en `lock_timeout=60s` (alleen deze stap); uitvoer met tijdstempels in `stage-…/migration.log`, **duur per migratie** in `migration-timings.tsv` en in de uitvoer. | **forward-only**: service blijft gestopt, onderhoud aan (§3.7) |
| 12 | Validatie: EF meldt 0 openstaand, nieuwste toegepaste = nieuwste in broncode, aantallen gelijk. | forward-only |
| 13 | Vorige binaries naar `stage-…/previous-api/` (zonder `App_Data/`), nieuwe binaries met `rsync --delete --exclude=/App_Data/` naar `API_BIN_DIR` (service is gestopt). | forward-only (zonder migraties: zie `RESTORE_PREVIOUS_ON_FAILURE`) |
| 14 | `systemctl start`, wachten tot `GET http://127.0.0.1:5019/health/ready` = 200 (max. `STARTUP_WAIT_SECONDS`). Unit `failed` of ≥ 3 herstarts → meteen falen. | forward-only: nieuwe service wordt **gestopt**, vorige wordt **niet** gestart |
| 15 | Huidige `dist/` naar `stage-…/previous-web/`, nieuwe frontend naar `dist/`, `nginx -t`, `systemctl reload nginx`, onderhoudsvlag weg. | forward-only |
| 16 | Publieke check `PUBLIC_URL/` = 200. | release is live; alleen gemeld, niets teruggedraaid |
| 17 | Retentie: 10 dumps, 3 opslag-snapshots, 3 staging-mappen. Samenvatting (commit, dump + tijdstip, snapshot, checksums, migraties, schema, vorige binaries/frontend). | — |

Waarom zo: de dump is pas de echte eindtoestand als niets meer schrijft (H11); migraties lopen
terwijl géén applicatie draait; nieuwe code start nooit tegen een ongemigreerd schema (incident
2026-08, §3.5); oude code start nooit tegen een (deels) gemigreerd schema (§3.7). Secrets worden
nooit getoond: `tms.env` wordt niet geëxporteerd; alleen `dotnet-ef` krijgt de connectionstring
(via de omgeving, nooit via argv); `psql`/`pg_dump` krijgen het wachtwoord via een tijdelijk
0600-`PGPASSFILE`.

**Downtime** = stap 9 t.e.m. 15: dump + snapshot + migraties + eerste start (backfills). Meet ze op
een teruggezette productiekopie (de samenvatting en `migration-timings.tsv` geven de cijfers) en
publiceer het onderhoudsvenster op basis daarvan.

Na de deploy: AFTER DEPLOY-checks (§6), `VACUUM (ANALYZE)` op de grote tabellen na zware
migraties (`transport_order_stops, transport_orders, trips, invoices, invoice_lines,
order_documents`), en dump + snapshot + `.sha256` off-host kopiëren
(`docs/operations/backup-restore.md`).

### 3.5 Incident 2026-08: tabel-eigenaarschap en migratievolgorde

Twee productielessen, allebei structureel afgedekt:

1. **Code vóór migraties.** Een deploy herstartte de API vóór `dotnet ef database
   update`; de nieuwe code verwachtte kolommen die nog niet bestonden en de service
   crashte bij opstart. Het script dwingt sindsdien de volgorde build (staging) → stop →
   finale back-up → migraties → validatie → binaries → pas dán start af, en start bij een
   migratiefout niets (§3.4, §3.7).
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


### 3.6 Master-data-preflight vóór de migraties van 2026-10-18

Enkele migraties maken identiteiten uniek of hoofdletterongevoelig en **breken af**
(`RAISE EXCEPTION` of een unieke index) zodra ze dubbels tegenkomen — ze voegen bewust nooit zelf
samen of hernoemen niets. Alleen de falende migratie rolt terug; eerder in dezelfde run toegepaste
migraties blijven staan (§1.1). Daarom draait deze controle vóór de downtime:

| Migratie | Controle (soort in de uitvoer) |
|---|---|
| `20261017210920_FleetReadinessIdentityAndDamageLinks` | kentekens per tenant op letters+cijfers in hoofdletters, VIN getrimd in hoofdletters, enkel niet-verwijderde voertuigen/opleggers (`vehicle_plate`, `vehicle_vin`, `trailer_plate`, `trailer_vin`) |
| `20261018120257_ReferenceIntegrityAndFinalInvoiceSnapshot` | klantnummers uniek per tenant op `upper(btrim(nummer))`, **verwijderde klanten tellen mee** (`customer_number`) |
| `20261018123011_IdentityNormalization` | e-mailadressen uniek per tenant op `lower(btrim(email))`; rolnamen uniek per tenant op `lower(naam)` (`user_email`, `role_name`) |
| `20261018143508_WarehouseInventoryIntegrity` | minder dan 10 000 uitgiftesjablonen per tenant (BM-nnnn-codering) (`issued_item_template_count`) |

Niet in de preflight (de migraties bewaken het zelf): tekstlengtes van `sales_categories`
(`CommercialWaveAuditFixes`). PostgreSQL ≥ 13 controleert het deployscript in stap 3.

**Controle (read-only, ook los te draaien vóór het onderhoudsvenster):**

```bash
psql -X -A -t -F $'\t' -v ON_ERROR_STOP=1 -h <host> -U tms -d <db> -f scripts/preflight-master-data.sql
```

Geen uitvoer = geen conflicten. Elke regel = één conflict: `soort  tenant  genormaliseerde-waarde
aantal  rijen` (per rij: id, de waarde zoals opgeslagen, naam, `[verwijderd]` / `[inactief]` /
`[systeemrol]`, bij rollen het aantal gebruikers). Het deployscript draait dezelfde query in
**stap 8** en stopt vóór de downtime als er iets gevonden wordt.

**Oplossen (manueel, per conflict, na overleg met de eigenaar van de gegevens):**

1. **Dubbel klantnummer** (`customer_number`): beslis welke klant het nummer houdt (meestal die met
   facturen / opdrachten). Geef de andere in de applicatie een nieuw, vrij nummer
   (Klant → Bewerken → Klantnummer). Is het een verwijderde klant, wijzig het nummer via SQL in een
   transactie, bv. `UPDATE customers SET "CustomerNumber" = "CustomerNumber" || '-OUD1' WHERE "Id" = '<id>';`
   — de historiek (facturen, documenten) bewaart het nummer al in haar eigen snapshot.
2. **Dubbel e-mailadres** (`user_email`): twee accounts die hetzelfde adres in andere hoofdletters
   gebruiken. Bepaal welk account de persoon echt gebruikt (laatste login in het audit-log). Zet het
   andere inactief en geef het een uniek adres (bv. `+oud` vóór de `@`) via Gebruikers → Bewerken, of
   via SQL op `users."Email"`. **Niet** samenvoegen door rijen te verwijderen: audit en historiek
   verwijzen naar het account-id.
3. **Dubbele rolnaam** (`role_name`): hernoem de niet-systeemrol (bv. `Planner (2)`) via Rollen →
   Bewerken. Hernoem nooit een `[systeemrol]`. Wil je ze samenvoegen, wijs eerst de gebruikers van de
   ene rol aan de andere toe en deactiveer daarna de overbodige rol.
4. **Dubbel kenteken/VIN** (`vehicle_plate`, `vehicle_vin`, `trailer_plate`, `trailer_vin`): beslis
   welk voertuig echt bestaat; corrigeer het kenteken/VIN van het andere in Wagenpark → Bewerken of
   archiveer het (verwijderde voertuigen tellen niet mee).
5. **Te veel uitgiftesjablonen** (`issued_item_template_count`): niet zelf oplossen — contacteer
   ontwikkeling (de codering moet eerst verbreed worden).
6. Draai de preflight opnieuw tot ze **niets** meer toont; daarna pas de deploy (of
   `dotnet ef database update`).

Breekt een migratie toch af (bv. een conflict dat ná de preflight ontstond), dan staat de melding
met de botsende waarden in de deploylog en in `stage-…/migration.log`. De service is dan al
gestopt en **blijft gestopt** met de onderhoudspagina aan; de migraties vóór de falende zijn
toegepast. Los de data op zoals hierboven en draai de deploy opnieuw met **dezelfde** ref (EF
hervat bij de falende migratie), of rol terug via restore (§3.7). Start nooit de vorige release.

### 3.7 Rollback: forward-only, terugrollen = restore

**Beleid.** Elke release met migraties is **forward-only**. Code-only terugrollen (de vorige
binaries starten op het nieuwe schema) is bewezen schadelijk (production-readiness B2/MIG-01): de
oude code kan geen tweede klant, gebruiker of voertuig meer aanmaken (unieke indexen op
genormaliseerde kolommen), leest NULL-factuurnummers van concepten niet, nummert facturen zonder
`Kind` en zijn `PermissionCatalogSeeder` verwijdert de nieuwe permissiecodes met hun toekenningen.
`dotnet ef database update <oudere-migratie>` (`Down()`) is evenmin een optie: meerdere Downs
falen of verliezen data (MIG-09).

**Wat het script doet bij een fout** (zie ook de tabel in §3.4):

| Fout in | Service | Onderhoud | Automatisch |
|---|---|---|---|
| stap 1–8 (preflight, build, DB-preflight) | vorige release blijft draaien | uit | niets aangeraakt |
| stap 9–10 (stop, dump, snapshot, verificatie) — **nog geen migratie** | vorige, **ongewijzigde** release wordt herstart als hij draaide | uit na geslaagde herstart | geen rollback nodig: binaries en schema zijn onaangeroerd |
| stap 11–15 (migratie gestart, validatie, binaries, start, readiness, frontend) | **blijft gestopt** (een gestarte nieuwe service wordt gestopt) | **blijft aan** | niets; het script print wat faalde, het dumppad (met tijdstip), de snapshot, het aantal in deze run toegepaste migraties, en deze procedure |
| stap 16 (publieke check) | nieuwe release draait | uit | niets (release is live) |

`RESTORE_PREVIOUS_ON_FAILURE=1` wordt **geweigerd** zodra er openstaande migraties zijn (al in
stap 4, vóór de downtime) en wordt in de foutafhandeling genegeerd met een expliciete melding
zodra een migratie gestart is of de nieuwe code al draaide. Het heeft enkel effect bij een
release zonder migraties waarvan het plaatsen van de binaries faalde vóór de eerste start. Een
poging om een **oudere ref** te deployen op een database die al nieuwere migraties heeft, weigert
het script ook (stap 4) — dat is code-only terugrollen.

Let op: na een forward-only stop is de unit nog `enabled`; een reboot start hem opnieuw (met de
binaries die er dan staan). Overweeg `systemctl disable transportationservice-api` tot het herstel
rond is, en `enable` daarna.

**Beslissen.** Na een mislukte deploy, met de service gestopt en de onderhoudspagina aan:

1. **Vooruit herstellen** (voorkeur bij een data- of configuratieprobleem, bv. een dubbel dat na
   de preflight ontstond, een ontbrekende `tms.env`-sleutel, een te kort `lock_timeout`): los de
   oorzaak op (deploylog, `stage-…/migration.log`, `journalctl -u transportationservice-api`) en
   draai de deploy opnieuw met **dezelfde** sha. EF hervat bij de eerste niet-toegepaste
   migratie; de nieuwe dump is dan de deels gemigreerde toestand — bewaar de dump van de eerste
   poging.
2. **Terugrollen via restore** (als de nieuwe release zelf onbruikbaar is):
   1. Service gestopt laten, onderhoud aan. Maak voor forensisch onderzoek een `pg_dump -Fc` van
      de huidige (kapotte) database. **Drop die database nooit tijdens het incident.**
   2. Zet de **finale deploydump** (pad en tijdstip in de deployuitvoer:
      `BACKUP_DIR/<db>-<ts>.dump`, controleer eerst `sha256sum -c BACKUP_DIR/<db>-<ts>.sha256`)
      terug in een **nieuwe** database, `createdb -O <eigenaar> <db>_rb_<ts>` +
      `pg_restore --no-owner --role=<eigenaar> --exit-on-error -d <db>_rb_<ts> <dump>`, en zet
      `Database=` in `ConnectionStrings__DefaultConnection` (`tms.env`) daarop. Volledige
      procedure, rechten en verificatie: **`docs/operations/backup-restore.md`**.
   3. Binaries: als stap 13 al liep,
      `rsync -a --delete --exclude=/App_Data/ <stage>/previous-api/ <API_BIN_DIR>/`; anders staan
      de vorige binaries nog in `API_BIN_DIR`.
   4. Frontend: als stap 15 al liep, `rsync -a --delete <stage>/previous-web/ <dist>/` en
      `nginx -s reload`; anders is de vorige frontend nog live.
   5. Bestanden: zet `Storage__RootPath` **niet** terug (de snapshot dient voor rampherstel, niet
      voor een rollback). Bestanden die na het dumptijdstip werden geüpload hebben geen DB-rij
      meer; noteer ze als wees.
   6. `systemctl start transportationservice-api`, wacht op `GET /health/ready` = 200, controleer
      Systeeminformatie (vorige `buildCommit`, vorige `schemaVersion`, 0 openstaand) en de smoke
      (§6). Daarna pas `rm -f /var/www/transportationservice/maintenance/maintenance.on`.
   7. Meld gebruikers welk tijdvenster opnieuw moet worden ingevoerd (normaal geen: de dump werd
      genomen nadat de service gestopt was).

Een rollback **na** een geslaagde, al gebruikte release (uren later) verliest alles wat sindsdien
werd ingevoerd; weeg dat af tegen vooruit herstellen.

### 3.8 Onderhoudsmodus

nginx toont `deploy/nginx/maintenance.html` met HTTP 503 (SPA én `/api/`) zolang
`/var/www/transportationservice/maintenance/maintenance.on` bestaat (per request geëvalueerd, geen
reload nodig). Het script zet de vlag vóór de stop en verwijdert ze pas als de nieuwe API ready is
en de nieuwe frontend live staat; bij een forward-only fout blijft ze staan. Handmatig:

```bash
sudo touch /var/www/transportationservice/maintenance/maintenance.on   # aan
sudo rm -f /var/www/transportationservice/maintenance/maintenance.on   # uit
```

De vlag stopt alleen gebruikersverkeer; achtergrondjobs stoppen pas met `systemctl stop`. Zonder
de nginx-configuratie uit `deploy/nginx/` heeft de vlag geen effect (dan geeft nginx 502 op
`/api/` zolang de service gestopt is).

### 3.9 Nieuw bedrijf (eerste tenant en beheerder)

Een productiedatabase start leeg: de dev-seeders (§2.2) draaien niet buiten Development. Een
nieuw bedrijf wordt aangemaakt met het provisioning-commando van de API
(`--provision-tenant …`, eenmalig, als de service-gebruiker met dezelfde `tms.env`), dat tenant,
instellingen, juridische entiteit, systeemrollen en referentiedata aanmaakt en een eenmalige
activatielink voor de beheerder verstuurt. Procedure en parameters:
**`docs/operations/provisioning.md`**. Gebruik nooit een Development-start op een
productiedatabase (die maakt `admin@dev.local` aan).

## 4. Omgeving & configuratie

Alle secrets via omgevingsvariabelen (`Sectie__Sleutel`) of een vault; in Development via
`dotnet user-secrets`. **Nooit** echte waarden in `appsettings*.json` of de repo (zie §8).

| Sleutel | Betekenis |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` op de server (in `tms.env`). |
| `ASPNETCORE_URLS` | `http://127.0.0.1:5019` (in de systemd-unit; alleen nginx spreekt de API aan). |
| `ConnectionStrings:DefaultConnection` | Npgsql-connectionstring naar PostgreSQL 16 (`Host=…;Port=…;Database=…;Username=…;Password=…`). Leeg in alle getrackte configs. Geen `Command Timeout` nodig: het deployscript voegt die alleen voor de migratiestap toe. |
| `Storage:RootPath` | **Verplicht buiten Development.** Absoluut pad van de duurzame bestandsopslag, **buiten** elke release-/bin-map (bv. `/var/lib/transportationservice/storage`), eigenaar de service-gebruiker. De API weigert te starten zonder geldige waarde; het deployscript weigert te deployen bij een ontbrekend, relatief, niet-schrijfbaar pad of een pad binnen een release-map. Zie `docs/storage.md`. |
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
| `Peppol:Providers:{key}:…` | Gereserveerd voor echte Access-Point-adapters (endpoint, credentials) volgens het options-patroon; momenteel is alleen de `sandbox`-provider geregistreerd. Provider- en omgevingskeuze zelf zit **per juridische entiteit in de database** (`PeppolSettings`: Enabled, Environment Sandbox/Live, ProviderKey), beheerd via de UI (`peppol.configure`). **Live is fail-closed**: alleen mogelijk met een provider die `SupportsLiveNetwork` claimt (nu geen) — zie `docs/peppol.md` § Live-beleid en health check `peppol`. |
| `Retention:LegalHold` | `true` = elke geautomatiseerde purge opgeschort (litigation hold). |
| `Retention:OutboxRetentionDays` (365), `Retention:SecurityTokenRetentionDays` (30), `Retention:SinkFileRetentionDays` (14) | GDPR-retentievensters voor de 24-uurs-sweep. Auditlogs worden nooit door de app gepurged (append-only trigger; checklistpunt 29). |
| `Cors:AllowedOrigins` | Expliciete originlijst; leeg buiten Development = niets toegestaan. |
| `Frontend:BaseUrl` | Publieke SPA-URL, gebruikt in gemailde links. |
| `Network:KnownProxies` / `Network:KnownNetworks` | Vertrouwde reverse-proxy-adressen/-netwerken voor forwarded headers. |
| `Dev:AllowImpersonationHeaders` | Alleen Development; elke andere omgeving weigert te starten met deze vlag aan. |
| `AllowedHosts` | Standaard ASP.NET-hostfilter (`*` in basisconfig; op de edge beperken). |
| `POSTGRES_PASSWORD` (docker-compose) | Wachtwoord van de lokale dev-database. |
| `VITE_API_BASE_URL` (frontend, buildtime) | Publieke API-basis-URL, ingebakken in de SPA-build. Het deployscript leest **alleen deze sleutel** uit `tms.env` (of uit zijn eigen omgeving) en geeft hem expliciet aan `npm run build`; de rest van `tms.env` bereikt de builds nooit. |

**Minimale `tms.env` op de server** (root:deploy `0640`, EnvironmentFile-formaat `KEY=VALUE`,
sleutels alleen `[A-Za-z0-9_]` — punten zijn niet toegestaan, zet logniveaus in
`appsettings.Production.json`):

```ini
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=Host=127.0.0.1;Port=5432;Database=<db>;Username=tms;Password=<secret>
Storage__RootPath=/var/lib/transportationservice/storage
Jwt__SigningKey=<secret, >= 32 bytes>
ColumnEncryption__Key=<secret, base64 32 bytes>
# ColumnEncryption__PreviousKeys__0=<na een rotatie>
Email__Smtp__Host=<smtp-host>
Email__Smtp__Port=587
Email__Smtp__Username=<user>
Email__Smtp__Password=<secret>
Email__Smtp__FromAddress=<noreply@domein>
Email__Smtp__FromName=<naam>
Email__Smtp__UseTls=true
Cors__AllowedOrigins__0=https://<echt-domein>
Frontend__BaseUrl=https://<echt-domein>
Network__KnownProxies__0=127.0.0.1
VITE_API_BASE_URL=https://<echt-domein>
```

`Cors__AllowedOrigins__0` en `Frontend__BaseUrl` moeten het **echte** domein zijn
(`appsettings.Production.json` bevat het demodomein). Het deployscript gebruikt
`Frontend__BaseUrl` ook voor de publieke eindcontrole. Bewaar `ColumnEncryption__Key` (+ vorige
keys) en `Jwt__SigningKey` in een vault, gescheiden van de back-ups.

---

## 5. Achtergrondjobs & hosted services

Alle jobs zijn `BackgroundService`-implementaties met `PeriodicTimer`, één DI-scope per
tick, fouten gelogd zonder de lus te breken. Ze draaien **in-process** in de API. **Draai exact
één API-instantie** (§3.3): meerdere instanties zijn voor de pilot niet ondersteund. De sweeps,
de automatische back-up en de opstartsync lopen wel onder een PostgreSQL-advisory-lock (een
tweede instantie slaat de run over), de outbox claimt elke rij afzonderlijk en bewaart het
resultaat per bericht (een herstart verstuurt een batch dus niet opnieuw), en Peppol claimt elke
transmissie vóór het indienen. Wat (nog) niet multi-instance-veilig is (o.a. de in-memory
rate-limiter): `docs/operations/single-instance.md`. Volledige lijst, geverifieerd in code
(intervallen uit de bronbestanden; zie ook `docs/background-jobs.md`):

| Hosted service | Interval | Functie |
|---|---|---|
| `OutboxDispatcherHostedService` | 30 s | E-mail-/sms-outbox afleveren (backoff 5·2ⁿ min, max 5 pogingen, quiet hours, fallbackkanaal) |
| `PeppolDispatcherHostedService` | 30 s | Peppol-transmissies indienen en providerstatus pollen |
| `CalendarSyncHostedService` | 60 s | Agenda-syncqueue (goedgekeurd verlof, bevestigde shifts) naar `ICalendarProvider` (fake provider alleen in Development; elders "niet geconfigureerd": items blijven Pending, nooit Synced) |
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

Health-endpoints (anoniem, niet rate-limited, geen HTTPS-redirect):
`GET /health/live` (proces leeft, raakt geen afhankelijkheden: altijd 200) en
`GET /health/ready` (database `SELECT 1`, 0 openstaande migraties, kolomversleutelingssleutel
past bij de database, plus de opslagcheck: 200 = alles Healthy, anders 503). Het deployscript
en de interne monitoring pollen `http://127.0.0.1:5019/health/ready`; publiek is via nginx
alleen `/health/live` bereikbaar (§3.3). `GET /health/details` (aangemeld +
`system_info.view`) toont daarnaast outbox, Peppol, back-up, optionele integraties en de
heartbeat per achtergrondjob. Zie `docs/operations/single-instance.md`. OpenAPI/Scalar is
alleen in Development gemapt.

Na elke deploy:

1. **Opstartlog controleren** — geen exceptions van `StartupSecurityValidator`; regels van
   `DossierBackfillSeeder` ("created N wrapper dossiers" of stil bij 0), de
   `Authorization catalogue synced … v40`-regel en geen `fail:`/`crit:` of seeder-fouten
   (`journalctl -u transportationservice-api`).
2. **Migratiestand** — `__EFMigrationsHistory` eindigt op de nieuwste migratie van de
   gedeployde commit en telt evenveel rijen als er migratiebestanden zijn (§1.3); de
   deploysamenvatting toont `Schema`. `SELECT min("AppliedVersion"), max("AppliedVersion") FROM
   role_template_states` → 40/40.
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

- Volledig runbook (inhoud van een back-upset, bewaking, off-host kopie, offline herstel):
  `docs/operations/backup-restore.md`.
- Een back-up is een set: `pg_dump -Fc` + een `.files.tar.gz` van de storage root + een
  `.sha256`-manifest. De set is pas *Voltooid* nadat beide delen geverifieerd zijn. Anders
  komt er een rij *Mislukt* met de reden, en de stderr van `pg_dump`/`pg_restore` staat in het
  log. Encryptiesleutels zitten er bewust niet in: die horen in de vault. Een in-app back-up
  staat wel op dezelfde host: kopieer de sets versleuteld off-host (runbook).
- Opslag: `Backups:Directory` (default `<Storage:RootPath>/backups`, absoluut en buiten de
  releasemap; de deploy-opslag-snapshot sluit die map uit, want de dumps zijn al back-ups).
  `Backups:ExternalDirectories` kan de dumpmap van het deployscript
  (`/var/www/transportationservice/backups`) alleen-lezen tonen (type Pre-deployment).
  `Backups:PgDumpPath`/`PgRestorePath` wijzen naar de binaries (zelfde hoofdversie als de server).
- Permissies: `backups.view` (rollen v29: management) en de bewuste per-beheerder-rechten
  `backups.create/download/delete/restore`. Delete en restore zijn bovendien fail-closed
  in de service zelf.
- Retentie: `Backups:AutomaticRetentionDays` / `PreRestoreRetentionDays` (default 30);
  de nieuwste voltooide back-up en een lopende restore zijn beschermd; handmatige
  back-ups worden nooit automatisch verwijderd. Dagelijkse automatische back-up via
  `Backups:AutomaticEnabled` (default aan) op `Backups:AutomaticHourUtc` (default 02:00).
- Versheid: de healthcheck `backup` (tag `backup`, niet `ready`) en Systeeminformatie
  melden het als de laatste geslaagde back-up ouder is dan `Backups:MaxAgeHours`
  (default 26).
- **Downloaden** vraagt `backups.download` én de systeemrol Administrator: een dump bevat
  alle tenants.
- **Terugzetten** gebeurt buiten Development **niet** vanuit de app: het endpoint geeft 409
  en de knop is verborgen. Herstel is een offline procedure (runbook hierboven). In
  Development blijft de oude flow: getypte bestandsnaam, eerst een veiligheidsback-up, dan
  `pg_restore --clean --if-exists --exit-on-error` en een healthcheck. Alles is
  geauditeerd (create/download/delete/restore/retentie-opruiming).

## 7. Rollback-overwegingen

**Uitgangspunt (sinds de release van 2026-10-18): forward-only; terugrollen = restore van de
finale deploydump + vorige binaries + vorige frontend** — procedure in §3.7, offline
restore-runbook in `docs/operations/backup-restore.md`.

De vroegere regel "code eerst terugrollen, database laten staan" geldt **niet meer**: vanaf de
migraties van 2026-08-27 zijn er niet-additieve wijzigingen waar de vorige code niet tegen kan
(production-readiness MIG-01): `NormalizedCustomerNumber`/`NormalizedEmail`/
`NormalizedLicensePlate` met unieke indexen (oude code faalt bij de tweede klant, gebruiker of
voertuig), nullable `InvoiceNumber` op concepten, dossier-only documenten
(`TransportOrderId` nullable), factuurnummering per `Kind`, her-encodeerde tijdvensters
(`StopWindowTenantZoneReencoding`), en de permissiecatalogus (§2.3). Het deployscript start de
vorige binaries daarom nooit meer na een migratie (§3.4/§3.7) en weigert een oudere ref op een
nieuwer schema.

Een **schema-rollback** (`dotnet ef database update <vorige-migratie>`) is géén optie: elke
`Down()` commit apart, `AccountingIssueSnapshotAndNumbering.Down` faalt aantoonbaar (unieke index
op `invoice_sequences`), `DossierDocumentScopeAndIssuedTransportDocuments.Down` faalt zodra er
dossier-only documenten zijn, `numeric(14,4)→(12,2)` rondt bedragen af, en Downs van
`ImmutableIssuedTransportDocuments`/`IssuedInvoiceUblNoteSnapshot` vernietigen bevroren juridische
snapshots (MIG-09). Ter historische referentie, de oudere Downs (allemaal destructief):

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

Let op: seeder-effecten (wrapper-dossiers, activiteitstypes, geclaimde dossiernummers, bevroren
snapshots, permissiesync) staan buiten het migratiemechanisme en worden door een schema-rollback
deels vernietigd, deels achtergelaten. Ook daarom: herstel = restore van de dump van vóór de
migratie, nooit `Down()` en nooit code-only.

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
