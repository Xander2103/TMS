# Dossierbevestiging, lifecycle en dossierzoeken — sprint 2026-09-23

Status: uitgevoerd, NIET gecommit/gepusht/gedeployd. Basis: working tree van `nav-redesign` na de
master sprint (2026-09-21) en de closure sprint (2026-09-23).

## 1. Bestaande lifecycle vóór deze sprint

- `DossierStatus = { Open, Closed }`; enige metadata `ClosedAt`. Geen `ClosedBy`, geen annulering.
- "Open" = alles wat niet handmatig gesloten was. "Closed" = handmatig gesloten via `POST /close`
  (`dossiers.manage`, enige regel: geen open incidenten) of door de `DossierBackfillSeeder`
  (wrapper voor een al afgeronde/geannuleerde legacy-order). `POST /reopen` zonder reden.
- Geen enkel operationeel completion-pad raakte het dossier: `TripService.ChangeStatusAsync`
  (Completed → orders `InProgress → Completed`, readiness, kosten, audit) en het handmatige
  `POST /transport-orders/{id}/status` waren de enige plekken waar een order `Completed` wordt;
  POD zet niets; `DossierActivity` heeft geen status (bewust: transportactiviteiten leiden hem af
  van hun order, zelfstandige activiteiten zijn beschrijvend).
- Terminale statussen: order `Completed | Invoiced | Cancelled`; trip `Completed | Cancelled`;
  stop-uitvoering `Completed | PartiallyCompleted | Failed | Skipped`; er bestaat geen
  Failed/Suspended op order- of tripniveau. Factuur `Draft | Sent | Paid | Cancelled`.
- Lijst: `GET /api/dossiers` ongepagineerd (`Take(500)`), zoeken op nummer/titel/referentie/klant,
  geen datum, geen sortering.

## 2. Nieuwe lifecycle

```
Open ──(bevestigen: handmatig | automatisch)──▶ Closed (= "Bevestigd")
Open ──(annuleren, reden)─────────────────────▶ Cancelled ("Geannuleerd")
Closed | Cancelled ──(heropenen, reden, dossiers.reopen)──▶ Open
```

`Closed` blijft de technische waarde (backward compatible; UI-label "Bevestigd"). `Cancelled` is
nieuw (string-opslag, additief). Metadata op `TransportDossier` (migratie
`20260923161733_DossierConfirmationLifecycle`, alles nullable): `ClosedAt` (= bevestigd op, naam
behouden), `ConfirmedByUserId`, `ConfirmationSource` (Manual|Automatic), `ConfirmationReason`,
`CancelledAt`, `CancelledByUserId`, `CancellationReason`. Heropenen wist deze velden; de vorige
bevestiging staat in de audit-rij (`Reopened`, old values). Indexen: `(TenantId, DossierDate)`,
`(TenantId, ClosedAt)`, `(TenantId, CreatedAt)` — voor sortering/filters van de zoekpagina.

Eén service: `DossierLifecycleService` (`EvaluateAsync`, `ConfirmAsync`, `TryAutoConfirmAsync`,
`TryAutoConfirmForOrdersAsync`, `ReopenAsync`, `CancelAsync`). `DossierService.CloseAsync/ReopenAsync`
zijn verwijderd; `POST /close` blijft als compat-alias (= bevestigen met erkende warnings).

Bevestiging is **operationeel**: prijs- en factuurstatussen worden niet aangeraakt; een bevestigd
dossier blijft factureerbaar. Bestaande "gesloten dossier = read-only"-regels gelden ongewijzigd
voor Closed én Cancelled (`RequireOpen`-sites).

## 3. Completion-matrix

| Check (code) | Handmatig | Automatisch | Bron |
|---|---|---|---|
| Dossier niet Open (`dossier.not_open`) | **blocker** | blocker | status |
| Open incident New/InProgress (`incident.open`) | **blocker** | blocker | `Incidents` (bestaande close-regel) |
| Geen activiteiten (`activity.none`) | warning | blocker | activiteiten |
| Transportactiviteit zonder opdracht (`route.order_missing`) | warning | blocker | `HasStops` + geen `LinkedTransportOrderId` |
| Opdracht niet Completed/Invoiced (`order.not_completed`) | warning | blocker | orderstatus; **Cancelled wordt genegeerd** |
| Laatste stop-uitvoering Failed/Skipped/PartiallyCompleted (`delivery.not_completed`) | warning | blocker | `StopExecution` (laatste per stop) |
| Geen POD (`delivery.pod_missing`) | info | — | `InvoiceReadinessReasons` bevat `pod.missing` |
| Uitvoerbare zelfstandige activiteit nog niet uitgevoerd (`activity.not_executed`) | warning | blocker | type `PlanningRelevant && !HasStops` én (`PlannedDate` null of > vandaag) |
| Niets uitgevoerd (`nothing.completed`) | warning | blocker | geen voltooide opdracht én geen uitgevoerde activiteit |
| Prijsaandacht (`pricing.*`, uit readiness) | warning/info | — | readiness (bevestiging ≠ facturatie) |
| Geen CMR (`documents.cmr_missing`) | info | — | documenten |

- Handmatig: blockers weigeren; warnings vereisen `acknowledgeWarnings = true` (de dialoog toont
  ze; Info hoeft niet erkend). Zo kan een historisch dossier (transport gisteren, dossier vandaag,
  order in Draft, geen rit) direct bevestigd worden zonder fictieve statussen.
- Automatisch: alle blockers leeg. Trigger: `TripService.ChangeStatusAsync(Completed)` (voor alle
  orders van de rit) en `TransportOrderService.ChangeStatusAsync(Completed)` (handmatig voltooien),
  beide na hun eigen save, via `TryAutoConfirmForOrdersAsync` → dossiers via `DossierOrders` ∪
  `DossierActivity.LinkedTransportOrderId`. Idempotent: niet-Open → false, geen tweede audit.
- Cancelled orders tellen niet mee en blokkeren niet; een dossier met uitsluitend geannuleerde
  orders wordt nooit automatisch bevestigd (`nothing.completed`).
- Failed/Suspended: er is geen order-/tripstatus "Failed"; falen leeft op stopniveau en blokkeert
  automatische bevestiging via `delivery.not_completed`. Een gefaalde levering leidt meestal ook tot
  een incident (`incident.open`, blocker voor beide flows).
- "Uitvoerbaar" is configuratie (`ActivityType.PlanningRelevant`), nooit een code-stringcheck.
  Opslag (niet planning-relevant) is puur commercieel en blokkeert nooit; Plateau (planning-relevant)
  telt als uitgevoerd zodra de geplande datum voorbij is.

Audit (`TransportDossier`): `ConfirmedManually` (gebruiker, tijdstip, reden, erkende warnings),
`ConfirmedAutomatically` (tijdstip, trigger `trip:RIT-x` / `order:ORD-x`, aantallen), `Reopened`
(old = vorige bevestiging/annulering, new = reden, gebruiker), `Cancelled` (reden, gebruiker,
mee-geannuleerde conceptorders). Geen notificatie-event (secundair, bewust niet toegevoegd; het
bestaande `MessageKinds`-patroon volstaat later).

## 4. Annuleren

Alleen voor werk dat nooit is uitgevoerd: reden verplicht; geblokkeerd zodra een gekoppelde
opdracht een andere status heeft dan Draft/Cancelled (Submitted/Confirmed/Planned/InProgress/
Completed/Invoiced → eerst op de opdracht beslissen; uitgevoerd werk bevestig je). Conceptorders
worden mee geannuleerd met dezelfde reden (audit per order). Heropenen brengt het dossier terug naar
Open, maar **reactiveert de mee-geannuleerde opdrachten niet** (geen stilzwijgend herstel): ze blijven
Cancelled, zichtbaar op de activiteitkaarten en in de heropen-dialoog vermeld; herstel loopt via de
bestaande corrigerende opdrachtovergang `Cancelled → Draft` (reden verplicht). Daarna is het gedrag
voorspelbaar: niets uitgevoerd → nooit automatisch bevestigd; handmatig alleen met erkenning.

**Gelijktijdige completion-events** (twee ritten die bijna tegelijk afsluiten): de overgang
Open → Closed is één atomaire compare-and-set (`UPDATE … WHERE Status = Open`) in
`DossierLifecycleService.ConfirmAtomicallyAsync`; alleen de eerste UPDATE raakt een rij, de tweede
slaat status én audit over — exact één bevestiging, één `ClosedAt`, geen concurrency-exception op het
verzoek. Handmatig bevestigen gebruikt dezelfde CAS (verliezer krijgt de al bevestigde toestand terug).

## 5. Legacy Closed-dossiers

Bestaande Closed-rijen blijven Closed (tonen "Bevestigd"), `ClosedAt` blijft, `ConfirmationSource`
blijft **null** → UI toont "bron onbekend (historisch)". Geen backfill van gebruiker of bron, geen
verzonnen audit. De `DossierBackfillSeeder` (wrapper voor legacy orders) labelt nieuwe wrappers wél
eerlijk: afgeronde order → `Automatic` met reden "Backfill: opdracht was al afgerond"; geannuleerde
order → `Cancelled`.

## 6. Permissies

`dossiers.manage` — bevestigen, annuleren. `dossiers.reopen` (nieuw, rolsjabloon v34: planner,
management) — heropenen. `dossiers.view|manage` — evaluatie en zoeken. Alle queries tenant-scoped.

## 7. Zoeken (`GET /api/dossiers/search`, `DossierSearchQuery`)

Globaal `search` (dossiernummer, titel, klantreferentie van dossier én opdrachten, klantnaam/-nummer,
opdrachtnummer, stad, postcode (prefix), chauffeur, kenteken). Filters (AND): status,
confirmationSource, confirmedFrom/To, createdFrom/To, customerId, dateFrom/To (dossierdatum),
dossierNumber, orderNumber, customerReference, customerNumber, planningFrom/To (ritdatum of
geplande datum zelfstandige activiteit), driverId, vehicleId, trailerId, licensePlate,
activityTypeId, loadingCity, unloadingCity, postalCode, countryCode, priceStatus
(Priced|Partial|Unpriced), invoiceStatus (NotInvoiced|Draft|Sent|Paid), hasCmr. Sortering
(whitelist): number, date (default, desc), customer, status, confirmedAt, planningDate, createdAt +
stabiele secundaire sleutel `DossierNumber, Id`; NULLs altijd achteraan. Pagina ≤ 200.

Query-vorm: tenant-scoped basisquery → structurele filters → relationele filters als
gecorreleerde EXISTS op geïndexeerde joinkolommen → `COUNT` → ORDER BY → Skip/Take → gedeelde
lijstprojectie → pagina-gebonden hydratatie (stops, ritten, facturen, CMR). Tekstzoeken is
`LOWER(col) LIKE '%term%'` (bestaand patroon, SQLite- én PostgreSQL-compatibel); een trigram-index
op `transport_dossiers.DossierNumber/CustomerReference` is een latere optie wanneer metingen dat
rechtvaardigen. `GET /api/dossiers` (ongepagineerd) blijft bestaan voor bestaande aanroepers.

Lijstkolommen erbij: dossierdatum, bevestigd op + bron, activiteitenaantal, eerste laad-/laatste
losplaats, chauffeur-/voertuigsamenvatting ("Jan Peeters" of "2 chauffeurs" — nooit één
willekeurige), planningsdatum, factuurstatus, CMR aanwezig.
