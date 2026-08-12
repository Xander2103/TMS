# Magazijnlocaties & scans zonder rit (Wave 4)

## Locaties

Elk magazijn kan opslaglocaties krijgen: **zones** (bv. `A — Bulkzone`) met daaronder
**posities** (bv. `A-01`). Maximaal twee niveaus; codes zijn uniek per magazijn. Beheer op de
magazijnenpagina ("Locaties beheren", `warehouse.manage`); scanners lezen de lijst met
`scanning.execute`. Een locatie met posities of met colli erop kan niet worden verwijderd.

## Waar is een collo?

`Package.CurrentWarehouseLocationId` is een **projectie** — de append-only custody-events
(`PackageEvent.WarehouseLocationId`) blijven de bron van waarheid. Elke locatie-relevante scan
stempelt de locatie op het event én werkt de projectie bij.

## Scans zonder rit

`POST /api/warehouse/scans` (`scanning.execute`) — zelfde pijplijn (barcode-register,
custody-events, scanledger) als de ritgebonden scans, nooit een fork. Idempotent via
`clientEventId` (zelfde sleutel = zelfde uitkomst, nooit een tweede rij).

| Scansoort | Werking |
|---|---|
| **Ontvangst** (`Received`) | Aankomstregistratie: `Created/Labelled → AwaitingLoading`; al aanwezig = alleen locatie bijwerken. |
| **Verplaatsen** (`Moved`) | Locatie verplicht; custody-event + projectie, nooit een statuswijziging. |
| **Klaarzetten** (`Staged`) | Operationele marker voor vertrek; geen statuswijziging. |
| **Retour inboeken** (`Return`) | Trip-loos: `ReturnPending/ReturnLoaded/DeliveryFailed/Refused → ReturnedToDepot`. |

Onbekende barcodes en onverwachte statussen worden als **waarschuwingsrij geregistreerd**,
nooit stil weggegooid. In het scanledger hebben deze rijen `TripId = null` — ritgebonden
tellingen filteren op `TripId` en zien ze dus nooit.

## Trace & voorraad (Magazijn → Trace & voorraad)

- **Waar is X**: barcode → collo, huidige locatie, order/klant, laatste 10 custody-events.
- **Overzicht per magazijn**: colli per locatie; **"had vandaag buiten gemoeten"** (collo staat
  hier terwijl zijn order op een rit van vandaag zit) en **"wacht op morgen"**.
- Zelfde pagina voert de vier scans hierboven uit — het magazijnstation zonder rit.
