# Closure sprint 2026-09-23 — hardening van de master sprint 2026-09-21

Status: uitgevoerd, NIET gecommit/gepusht/gedeployd. Basis: working tree van `nav-redesign` op
`bcb8ec4` + master sprint (zie `2026-09-21-master-sprint-design.md`). Dit document legt de
beslissingen en integratiepunten vast; de testaantallen staan in het eindrapport van de sessie.

## P0 — Klantwissel en klantzichtbare documenten (security)

**Regel.** Een publicatie (`TransportOrderDocument.CustomerVisible`) is voor ÉÉN klant gedaan.
Wijzigt de klant van een opdracht of dossier, dan gaat de publicatie terug naar intern; het bestand
blijft staan, interne rechten blijven gelden, en een interne gebruiker publiceert bewust opnieuw.

- `Modules/Orders/Services/DocumentPublicationWithdrawal.cs` — één helper, expliciet tenant-scoped:
  `PublishedForOrder` (opdrachtdocumenten) en `PublishedForDossier` (dossierdocumenten,
  `TransportOrderId == null`).
- `OrderCustomerChangeService.ApplyCoreAsync` trekt de opdrachtdocumenten in; `DossierCustomerChangeService.ApplyAsync`
  de dossierdocumenten (de per-opdracht-wissel doet de rest). Audit per document:
  `TransportOrderDocument` / `CustomerVisibilityWithdrawn` met `Reason = CustomerChanged`, oude en nieuwe klant.
- Impact-DTO's (`CustomerChangeImpactDto`, `DossierCustomerChangeImpactDto`) melden
  `DocumentsPublicationWithdrawn`; de preview-dialogen tonen het als waarschuwing (nl/en/fr).
- Portaalzichtbaarheid zelf (`PortalDocumentService`) was al correct: dossier-/opdrachtklant + `CustomerVisible` +
  bestand, server-side herbevestigd bij download. Niet gewijzigd.
- Bewust buiten scope: POD-handtekeningen (`ProofOfDelivery.CustomerVisible`) volgen de opdracht en zijn
  uitvoeringsfeiten van die opdracht; niet ingetrokken. Genoteerd als observatie.

Tests: `Dossiers/DossierCustomerChangeDocumentVisibilityTests` (7) + frontend `customerChangeImpactList.test.tsx`.

## P0 — Productiepermissies

**Bevinding.** `PermissionCatalogSeeder` en `DefaultRoleSeeder` draaiden uitsluitend in het
Development-blok van `Program.cs`. Productie stond daardoor op rolsjabloon v24 en miste 20 codes:
`dossiers.price`, `dossiers.override_entity`, `activity_types.view/manage`, `locations.view_sensitive`,
`order_imports.manage_profiles`, `problems.approve_charge`, `system_info.view`, `backups.*` (5),
`attendance.*` (7). De catalogus in code (`PermissionCodes.All`, 271) was wél compleet; runtime-checks
lopen via `RolePermission → Permission`, dus een ontbrekende rij = 403 voor iedereen, ook Administrator.

**Oplossing.** `Data/AuthorizationCatalogSync.SyncAsync` (catalogus vóór rollen, gelogd) draait in het
omgevingsonafhankelijke startup-blok, na de migraties. Idempotent (bestaande seeders), tenant-maatwerk
overleeft. `MasterDataSeeder` hergebruikt een reeds gevulde catalogus (dev, lege DB).
`docs/delivery/operations.md` §2.1/§2.2 bijgewerkt. Geen deploy-scriptwijziging nodig.

Tests: `Identity/AuthorizationCatalogSyncTests` (4: productiescenario v24+20 codes, idempotentie,
maatwerk overleeft, plaats in `Program.cs`) + `SeedingTests` (1).

## P0 — Activiteitprijzen → facturatie

**Bestaande flow.** `Modules/Invoicing`: `InvoiceService.ListUninvoicedOrdersAsync` (kandidaten per
klant) → `CreateAsync(orderIds, manualLines)` → lijnen uit `AgreedPrice` + servicelijnen, orders naar
`Invoiced`, snapshot naar `Invoiced`; release bij annuleren/verwijderen/lijn droppen via
`OrderPricingSnapshotRelease`. Standalone activiteiten (`DossierActivityPricing`) hadden hier geen pad.

**Integratie (zelfde patroon, geen tweede engine).**
- `InvoiceLine.DossierActivityId` (nullable, index, FK SetNull) — migratie
  `20260923083318_InvoiceLineDossierActivityLink` (additief). Nooit samen met `TransportOrderId`.
- `ListUninvoicedActivitiesAsync(customerId)` / `GET /api/invoices/uninvoiced-activities`:
  geprijsd per provenance (`ActivityPricingState.IsPricedExpression`, dus nooit een ongeprijsde als € 0),
  **geen** `LinkedTransportOrderId` (opdracht-gedekte activiteiten worden via de opdracht gefactureerd),
  dossier van de klant, type billable, niet op een levende factuur, `Status != Invoiced`.
- `CreateInvoiceRequest.DossierActivityIds`: OneOff → 1 lijn; Lines → 1 lijn per prijslijn (aantal, eenheid
  via `UnEceUnitCodeMap`, gestempelde verkoopcode); gratis (`FreeConfirmed`, € 0) → expliciete € 0-lijn.
  Entiteitscoherentie zoals bij opdrachten (dossier-entiteit). `DossierActivityPricing.Status → Invoiced`.
- Release: `Dossiers/Services/ActivityPricingRelease` (Invoiced → Locked bij annuleren/verwijderen/lijn droppen;
  → Draft bij klantwissel). Dossier-klantwissel: geblokkeerd als een activiteit op een verzonden/betaalde
  factuur staat; conceptlijnen worden losgekoppeld (`ActivityInvoiceLinesReleased`).
- `InvoiceLineDto.DossierActivityId/DossierNumber` (traceerbaarheid); dossier-`InvoicedTotal` telt
  activiteitlijnen mee (en geen geannuleerde documenten meer).
- Frontend: sectie "Factureerbare activiteiten" in `NewInvoicePage`, dossiernummer op activiteitlijnen in het
  factuurdetail (nl/en/fr).
- Niet gewijzigd: dieseltoeslag (alleen over opdrachtlijnen), creditnota's (spiegelen zonder koppeling,
  zoals bij opdrachten), historische facturen.

Tests: `Invoicing/ActivityInvoicingTests` (10) + frontend `newInvoiceActivities.test.tsx` (3) en
`invoiceDetailSend.test.tsx` (+1).

## P1 — Stale "niet alle goederen geprijsd"

**Root cause.** Twee ongesynchroniseerde paden: de engine bevriest per-eenheid-coverage in
`CoverageJson`/`CoverageStatus` bij een herberekening; de D4-koppeling verkooplijn↔goederenlijn
(`OrderPriceLineCargoLink`) werd alleen bij het LEZEN in de per-lijn-badge verwerkt. Alle waarschuwingslezers
(frontend strip, `ConfirmOrderPricingAsync`, `InvoiceReadinessEvaluator`, `DossierReadinessService`,
`ActivityPriceStatus`) lezen het bevroren blob. Bijkomend: een one-off/manuele vaste prijs liet de
engine-entries eveneens op "None" staan.

**Fix.** `Orders/Services/PricingCoverageReconciler` (puur): entry → `Full` wanneer vaste prijs of alle
`CargoItemIds` van de entry afzonderlijk geprijsd zijn (link naar levende, niet-informatieve prijslijn); anders
het engine-oordeel. Engine-oordeel blijft op de entry (`EngineStatus/EngineReason`), dus herberekenbaar:
lijn weg → waarschuwing terug. Coverage-entries dragen nu `CargoItemIds` (bevroren bij berekening; oudere
snapshots zonder ids worden bij de volgende herberekening gereconcilieerd). Persistentie:
bij de berekening én in `SaveOrderPriceLinesAsync` (ook link-only saves; readiness wordt daarna geëvalueerd
op de opgeslagen snapshot).

Tests: `Orders/PricingCoverageReconciliationTests` (7: geen/één/alle/vast/mix/verwijderen/ontkoppelen +
reload).

## P1 — Goederensamenvatting eenheid

`DossierGoodsSummary` en `DossierOverview` toonden de eenheidscode; nu dezelfde resolver als de
opdrachtpagina (`unitLabelFrom(useLookupOptions('/api/unit-types'))`): catalogusnaam, code zolang de
catalogus laadt/onbekend, legacy vrije tekst zonder code. Tests: `dossierGoodsSummary.test.tsx` (+5),
`dossierWorkSurface.test.tsx` (+1).

## P1 — Escape / drawer

`components/ui/escapeLayerStack.ts` — één documentlistener, stack van open lagen; alleen de bovenste laag
behandelt Escape. `Modal` en `SectionDrawer` gebruiken `useEscapeLayer`; drawer herstelt focus naar de opener.
Bubble-fase bewust (de bestaande `stopPropagation`-workaround in `DossierNotesPanel` blijft werken).
Tests: `escapeLayerStack.test.tsx` (5).

## P1 — Plateau prijsbaarheid

Bewijs: seed `PLATEAU` = `HasStops:false`, `IsBillable` (record-default `true`), `AllowsDuration:true` sinds
Wave 1 (`1d5ec7d`); migratie `StandaloneActivityPricing` voegde `IsBillable` toe met `defaultValue: true`,
dus bestaande rijen in elke omgeving zijn billable; geen SQL-migratie heeft ooit typerijen ingevoegd. Enige
afwijking mogelijk = bewuste tenantbewerking (types zijn data). De picker stuurt op `isBillable`/`hasStops`.
Tests: `ActivityTypeSeederTests.StandaloneCommercialDefaults_ArePriceableByConfiguration`,
`dossierActivityPricing.test.tsx` (+2: Plateau naast één transport; niet-billable Plateau niet aangeboden).

## P1 — Klantportaal browsercheck

Uitgevoerd met playwright-core + gecachte Chromium tegen lokale API + Vite, met de BESTAANDE lokale
testgebruiker `klant@acme.be` (`StartUp.local.txt`); geen gebruikers aangemaakt. Twee lokale-DB
datacorrecties waren nodig via de reguliere admin-API (geen gedragswijziging): de lokale `klantportaal`-rol
droeg een legacy `orders.view` (de identity-class-guard weigerde daardoor terecht elke rolwijziging) →
verwijderd; de optionele add-on `klantportaal_documenten` toegekend (`POST /api/users/{id}/roles`).
Flow: login → `/klantportaal/documenten` toont "E2E portaal zichtbaar" (dossier 0021) → dossier via
`PUT /api/dossiers/{id}/customer` naar klant "TEST EH" (preview `documentsPublicationWithdrawn: 1`) →
portaal klant A leeg, `CustomerVisible=false`, audit `CustomerVisibilityWithdrawn` → herpublicatie via
`PUT /api/order-documents/{id}` → klant A blijft leeg. Klant B heeft lokaal geen portaalgebruiker; "B ziet
het na herpublicatie" is enkel door `DossierCustomerChangeDocumentVisibilityTests` gedekt.

## P2 — Flaky `dockWallClock.test.tsx`

10/10 runs groen (5 geïsoleerd, 3 met alle TZ-muterende suites, 2 shuffled). Niet reproduceerbaar; bestand
ongewijzigd.

## P2 — Automatisch uurtarief / verplaatsingskosten (deferred)

De engine kent `PriceRuleBasis.Hourly`, `SurchargeKind.PerHour` en `PerKm` (met `order.DistanceKm`), maar
alleen voor **opdracht-gedekte** prijzen. Standalone activiteiten (`DossierActivityPricing`) lopen nooit door
de engine; `DurationHours` koppelen aan een uurtarief vereist een nieuwe engine-capability ("activiteit als
prijsonderwerp"). Bewust niet geïmproviseerd — volgende pricing-feature. Voor opdrachten is een km-/uurtarief
al configureerbaar (regel of dienst).

## P2 — Transpallet / heftruck

Niet gemodelleerd; `GoodsCapacityHint` zet "transpallet/heftruck" altijd onder "Capaciteit nog te
controleren" en geeft nooit een veilig oordeel (test "never says a load is safe"). Geen wijziging.
