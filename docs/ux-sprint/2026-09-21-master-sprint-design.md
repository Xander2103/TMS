# Master sprint 2026-09-21 — ontwerpbeslissingen

Status: in uitvoering, NIET gecommit. Dit document is het gedeelde contract voor de uitvoering.
Bron: vijf read-only audits (domeinmodel, stops/adressen/goederen, pricing, documenten/notities,
frontend) op `nav-redesign` @ `bcb8ec4` (baseline: backend 2578/2578, frontend 1580/1580 groen).

## 0. Leidende regels

- Eén bron van waarheid per gegeven. Bestaande relaties hergebruiken; nieuwe alleen waar het model
  de functie niet kan dragen.
- Activiteitstypes zijn **data** (`ActivityType`-rijen met capability-vlaggen). Nieuwe logica stuurt
  op vlaggen, nooit op een code als `PLATEAU` of `KRAANTRANSPORT`.
- Een opdracht is het uitvoeringsrecord van een `HasStops`-activiteit. Geen veld op `DossierActivity`
  dat al op `TransportOrder` bestaat.
- Geen verzonnen data, geen ontbrekende prijs als € 0, historische prijzen/snapshots blijven staan.
- Alle migraties additief; geen kolom/tabel verwijderd; legacy vrije-tekstkolommen blijven bestaan.
- Tenant: globale queryfilter + `TenantReferenceGuard` voor elk binnenkomend kind-id.

## 1. Waar hoort wat

| Gegeven | Niveau | Drager |
|---|---|---|
| Klant, dossiernummer, algemene documenten, dossiernotities, historiek | Dossier | `TransportDossier`, `TransportOrderDocument` (scope dossier), `DossierNote` |
| Type dienst, volgorde, geplande duur, activiteitnotities, prijs van zelfstandige dienst | Activiteit | `DossierActivity`, `DossierNote.DossierActivityId`, `DossierActivityPricing` + lijnen |
| Stops, goederen, kraanopdrachtsoort, werkomschrijving, lastgegevens, verkooplijnen, status | Opdracht | `TransportOrder`, `TransportOrderStop`, `CargoItem`, pricing snapshot + lijnen |
| Chauffeur, voertuig, oplegger, ritstatus, conflicten | Rit | `Trip` (ENIGE bron) |
| Adres-snapshot, tijdvenster, tijdseis, werfstop | Stop | `TransportOrderStop` |
| Gewicht per eenheid / totaal, afmetingen | Goederenlijn | `CargoItem` (velden bestaan al) |
| Bedrag, hoeveelheid, koppeling naar goederen | Verkooplijn | `TransportOrderPricingLine` (+ koppeltabel op `LineKey`), `DossierActivityPriceLine` |
| Uitgegeven vrachtbrief/werkbon met uniek nummer | Opdracht | `IssuedTransportDocument` |

## 2. Beslissingen per onderdeel

### D1 — Toewijzing (spec §13): geen tweede bron
Chauffeur/voertuig/oplegger blijven uitsluitend op `Trip`. De **effectieve toewijzing** van een
activiteit = de rit(ten) van haar opdracht (`TripOrder`). Twee activiteiten met verschillende
chauffeurs = twee ritten. De activiteit krijgt dus géén eigen chauffeurkolom.
- `DossierActivityDto` krijgt een read-only `Assignment` (rit-id/nummer, chauffeurnaam, voertuignummer +
  kenteken, opleggernummer, aantal ritten, aantal ándere opdrachten op de rit).
- Nieuw: `POST /api/dossiers/{id}/activities/{activityId}/plan` — maakt voor de opdracht van de
  activiteit een Draft-rit (datum uit opdracht/activiteit) of hergebruikt de bestaande niet-afgesloten
  rit; optioneel meteen chauffeur/voertuig/oplegger. Wijzigen loopt via de bestaande rit-endpoints.
  Bevat de rit andere opdrachten, dan toont de UI "geldt voor de hele rit (N opdrachten)".
- Bestaande conflictregels (Warning vs Blocking, override met reden) blijven ongewijzigd.
- **Vast voertuig:** bron blijft `Vehicle.FixedDriverId`. Nieuw `Trip.VehicleSelectionSource`
  (`Suggested` | `Manual`, nullable = legacy/onbekend → behandeld als Manual). Bij chauffeurkeuze
  wordt het vaste voertuig voorgesteld als er geen voertuig is of de bron `Suggested` is; een
  `Manual` keuze wordt nooit overschreven. Een voorgesteld voertuig is pas "toegewezen" na opslaan.

### D2 — Kraan: twee soorten (spec §11/§12)
- Nieuwe capability-vlag `ActivityType.SupportsOnSiteWork` (seed: KRAANTRANSPORT = true; bestaande
  tenantrijen met code KRAANTRANSPORT krijgen de vlag in de migratie — eenmalige datafix, geen
  runtime-codecheck).
- `TransportOrder.CraneJobKind` (`None` | `TransportWithCrane` | `OnSiteLifting`), `WorkDescription`,
  en lastgegevens `LiftLoadWeightKg`, `LiftLoadDimensions`, `LiftRadiusMeters`, `LiftHeightMeters`,
  `LiftConditions`, `LiftEquipment` (alle nullable). Een last is geen goederenlijn.
- **Werfstop:** `StopType.Site` (enum wordt als string opgeslagen → geen schemawijziging). Alle
  plaatsen die op Loading/Unloading leunen worden expliciet nagelopen. Geen fictieve laad/los-stops.
- Validatie `OnSiteLifting`: ≥ 1 werfstop met plaats of adres + verplichte `WorkDescription`;
  goederen-minimumregel vervalt; Loading/Unloading-stops niet toegestaan. Andere types: ongewijzigd.
- **Tijd:** start = `PlannedFrom` van de werfstop, einde = `PlannedTo`, duur = bestaande
  `DossierActivity.DurationHours`. Nieuw `TransportOrderStop.PlannedToIsManual`. Zolang niet manual:
  einde = start + duur (server is leidend, ook over middernacht — het zijn `DateTime`s). Tijdseisen
  (`TimeRequirement*`) en klantvensters worden hier nooit door geraakt. Alleen voor werfstops.
- **Documenten:** `OnSiteLifting` ⇒ standaard geen CMR; nieuw documentsoort `WorkOrder` (werkbon).
- **Pricing:** via bestaande one-off prijs en handmatige verkooplijnen (uren × tarief, vaste prijs,
  toeslag als lijn). Engine ongewijzigd. Automatisch uurtarief uit `DurationHours` en
  verplaatsingskosten bestaan niet in de engine → buiten scope, gerapporteerd.

### D3 — Adressen (spec §5–§7): geen tweede adresdatabase
- De stop-snapshot IS de dossieroverride. Adresvelden blijven altijd zichtbaar en bewerkbaar, ook
  met `LocationId`. Nieuw `TransportOrderStop.AddressOverridden` (afwijking t.o.v. adresboek zichtbaar
  maken; "Adres opnieuw overnemen" zet hem terug). Het centrale record wordt nooit stil gewijzigd.
- `TransportOrderStopInput.SaveToAddressBook` (default false): bij het OPSLAAN van de opdracht, in
  dezelfde transactie, wordt het adres via de bestaande locatielogica aangemaakt en aan de
  dossierklant gekoppeld; vereist `locations.create`. Exacte actieve duplicaat (bestaande
  `AddressNormalizer`-sleutels) ⇒ bestaande locatie koppelen i.p.v. aanmaken. Daarna heeft de stop
  een `LocationId` ⇒ herhaald opslaan maakt niets nieuws (idempotent).
- Picker `GET /api/addresses/picker` blijft de ene zoekbron (tenant-breed, dossierklant eerst);
  uitgebreid met klantnaam per resultaat. Ook het straatveld gebruikt dezelfde hook.
- Postcodevalidatie per land (BE/NL/FR/DE/LU); onbekend land = geen formaatcheck. Alleen voor nieuwe
  of gewijzigde waarden, zodat bestaande data opslaan nooit blokkeert.

### D4 — Goederen (spec §9/§10)
- `CargoItem.WeightPerUnitKg` bestaat al → geen migratie. Totaal = aantal × gewicht/eenheid zolang
  het totaal niet handmatig is gezet; alleen-totaal leidt NOOIT tot een afgeleid stukgewicht.
  Verschillende gewichten = aparte lijnen.
- Capaciteit: numeriek bestaat alleen `Vehicle.PayloadKg`/`Trailer.CapacityKg`. Nieuw (nullable)
  `Vehicle.TailLiftCapacityKg`. Zwaarste eenheid > laadklepcapaciteit ⇒ Warning; onbekend ⇒
  "Capaciteit nog te controleren". Transpallet/heftruck zijn niet gemodelleerd ⇒ altijd "nog te
  controleren". Nooit "veilig".
- Koppeling verkooplijn ↔ goederenlijn: tabel `order_price_line_cargo_links`
  (`TransportOrderId`, `LineKey`, `CargoItemId`) — op `LineKey` omdat Auto-lijnen bij herberekening
  herschreven worden. Dekking per goederenlijn (afgeleid, niet opgeslagen): `SeparatelyPriced`
  (gekoppeld) · `Included` (opdracht geprijsd én vaste/one-off prijs of eenheidsdekking `Full`) ·
  `ToReview`.

### D5 — Prijs per activiteit (spec §15)
- Opdracht-activiteiten: bestaande orderlijnen (`SaveOrderPriceLinesAsync`) — alleen UI-ontsluiting.
- Zelfstandige activiteiten: `DossierActivityPricing` uitgebreid met `DossierActivityPriceLine`
  (omschrijving, aantal, eenheid, eenheidsprijs, bedrag, verkoopcategorie); `ActivityPricingSource`
  += `Lines`; `AgreedPrice` = lijnentotaal. `FreeConfirmed` voor expliciet gratis.
- Afgeleide `PriceStatus` per activiteit: `NotPriced` · `PartiallyPriced` · `Priced` · `Free`.
  "Geprijsd" blijft herkomst, nooit bedrag (`OrderPricingState`/`ActivityPricingState`).
- Geen dossierbrede verkooplijnen: die bestaan niet en zouden het eerste dubbeltelrisico zijn.
- Facturatie van activiteitprijzen: bestaat niet (bekend sinds 11-09) → buiten scope.

### D6 — Documenten (spec §14/§20/§21)
- Eén entiteit, één bestand: `TransportOrderDocument` krijgt `DossierId`; `TransportOrderId` wordt
  nullable. `TransportOrderId == null` ⇒ dossierdocument. Backfill `DossierId` uit de bestaande
  dossierkoppeling; check-constraint: minstens één van beide gezet. Niets wordt verplaatst.
- Endpoints: lijst/upload op dossierniveau, `POST /api/order-documents/{id}/move` (doelopdracht moet
  in hetzelfde dossier zitten; het bestand wordt niet aangeraakt). Verwijderen van een
  dossierdocument kan alleen vanuit dossierniveau.
- Telling = unieke documenten van het dossier. Klantzichtbaarheid staat los van scope; portaal
  controleert bij lijst én download opnieuw klant + `CustomerVisible`.
- `IssuedTransportDocument` (opdracht, soort, uniek `DocumentNumber`, `RequestId` voor idempotentie
  bij dubbelklik, optioneel extern nummer). Nummers via `TenantNumbering` (concurrency-veilig) +
  unieke index. Meerdere vrachtbrieven per opdracht toegestaan. De PDF drukt documentnummer,
  dossiernummer en opdrachtnummer af.

### D7 — Notities (spec §18/§19)
`DossierNote` naar het patroon van `EmployeeNote` (tekst, auteur, tijdstip, optioneel
`DossierActivityId`). Legacy `TransportDossier.Notes`/`DossierActivity.Notes` blijven bestaan en
worden in de migratie als eerste notitie overgenomen; de UI schrijft ze niet meer. Notities staan
los van `AuditLog`.

### D8 — Layout
Ref-geteld body-scrollslot gedeeld door `Modal` en `SectionDrawer`; ondermarge voor de sticky
actiebalk; sidebar `max-height` i.p.v. vaste hoogte. SearchableSelect: één end-adornment-zone.

## 3. Migraties
Eén migratiestroom, sequentieel gegenereerd, alleen op de lokale Docker-DB toegepast.

## 4. API-contracten (JSON camelCase, enums als string) — bindend voor backend én frontend

### 4.1 Notities (D7)
- `GET  /api/dossiers/{dossierId}/notes[?activityId=]` → `DossierNoteDto[]`, nieuwste eerst. Zonder
  `activityId`: alle notities van het dossier (dossierniveau = `dossierActivityId: null`).
- `POST /api/dossiers/{dossierId}/notes` `{ text, dossierActivityId? }` → `DossierNoteDto`
- `PUT  /api/dossiers/{dossierId}/notes/{noteId}` `{ text }` → `DossierNoteDto`
- `DELETE /api/dossiers/{dossierId}/notes/{noteId}` → 204
- `DossierNoteDto { id, dossierId, dossierActivityId, text, authorName, createdAt, updatedAt, canEdit, canDelete }`
- Rechten: lezen `dossiers.view`, schrijven `dossiers.manage`. Tekst verplicht, max 4000.
- `DossierActivityDto` += `noteCount`, `latestNotePreview` (max 160 tekens), `latestNoteAt`.
- Een notitie is geen auditregel; aanmaken/wijzigen/verwijderen wordt wél ge-audit (`DossierNote/...`).

### 4.2 Documenten (D6)
- `GET  /api/dossiers/{dossierId}/documents` → `DossierDocumentDto[]` (alle documenten van het dossier,
  beide scopes).
- `POST /api/dossiers/{dossierId}/documents`
  `{ transportOrderId, documentType, customTypeName, title, issueDate, notes, customerVisible }`
  (`transportOrderId: null` = dossierdocument) → `DossierDocumentDto`. Bestand daarna via het bestaande
  `POST /api/order-documents/{id}/document`; downloaden/bijwerken/verwijderen via de bestaande
  `api/order-documents/{id}`-routes (tri-state `customerVisible`: null = ongewijzigd).
- `POST /api/order-documents/{id}/move` `{ targetTransportOrderId }` (null = naar dossierniveau) →
  `DossierDocumentDto`. Doelopdracht moet in hetzelfde dossier zitten. Bestand blijft onaangeroerd.
- `DossierDocumentDto { id, dossierId, transportOrderId, orderNumber, scope: 'Dossier'|'Order',
  documentType, customTypeName, title, fileName, contentType, issueDate, notes, customerVisible,
  hasFile, createdAt, createdByName }`
- De bestaande `GET /api/transport-orders/{orderId}/documents` blijft de EIGEN documenten van de
  opdracht teruggeven; dossierdocumenten komen uit de dossierlijst (geen kopieën).
- Rechten: dossierniveau lezen `dossiers.view`, schrijven `dossiers.manage`; opdrachtniveau zoals nu.
- `DossierDetailDto.documentCount` = aantal UNIEKE documenten van het dossier.

### 4.3 Uitgegeven transportdocumenten (D6)
- `GET  /api/transport-orders/{orderId}/issued-documents` → `IssuedTransportDocumentDto[]`
- `POST /api/transport-orders/{orderId}/issued-documents` `{ kind, requestId, externalNumber? }` →
  `IssuedTransportDocumentDto` (zelfde `requestId` = zelfde record terug, geen tweede nummer)
- `GET  /api/issued-transport-documents/{id}/pdf` → PDF met documentnummer, dossiernummer, opdrachtnummer
- `IssuedTransportDocumentDto { id, transportOrderId, kind: 'Cmr'|'DeliveryNote'|'WorkOrder',
  documentNumber, externalNumber, issuedAt, issuedByName }`
- `DossierActivityDto` += `issuedDocuments: { id, kind, documentNumber }[]`, `documentCount`.
- Rechten: lezen `orders.view`, uitgeven `orders.edit`/`orders.manage`.

### 4.4 Prijs per activiteit (D5)
- `PUT /api/dossiers/{id}/activities/{activityId}/price` blijft bestaan (vaste prijs).
- `PUT /api/dossiers/{id}/activities/{activityId}/price-lines`
  `{ version, lines: { id?, label, quantity, unit, unitPrice, salesCategoryId? }[], freeConfirmed }`
  → `DossierDetailDto`. Bedrag per lijn = aantal × eenheidsprijs (server rekent); lege lijst +
  `freeConfirmed: true` = expliciet gratis; lege lijst zonder = niet geprijsd. Recht `dossiers.price`.
- `DossierActivityDto` += `priceLines[]`, `priceStatus: 'NotPriced'|'PartiallyPriced'|'Priced'|'Free'`,
  `freeConfirmed`. Voor opdracht-activiteiten komt `priceStatus` uit de ordersnapshot.
- Orderlijnen: het bestaande `PUT /api/transport-orders/{id}/pricing/lines` krijgt per lijn een optioneel
  `cargoItemIds: string[]`; `CargoItem`-DTO += `commercialCoverage: 'SeparatelyPriced'|'Included'|'ToReview'`.
