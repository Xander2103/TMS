# EDI (handelspartners — logistieke orders)

Inkomende transportopdrachten en uitgaande statusberichten per **handelspartner**,
met een generiek JSON-profiel. Backend in `Modules/Edi`, frontend
`features/edi` (`/edi`). **Scope-afbakening**: Peppol is uitdrukkelijk géén
EDI-handelspartner — e-facturatie is een aparte module met eigen entiteiten,
permissies en schermen (zie `docs/peppol.md`); de EDI-module raakt facturen nooit aan.

## Architectuur

```
POST api/edi/inbound/{partnerCode}/{messageType} ──► EdiService.IngestAsync
        │ SHA-256-hash + externalOrderId-dedupe (Duplicate: bewaard, nooit herverwerkt)
        ▼
EdiMessage (Received) ──► ProcessAsync ──► PrepareAsync (parse + mapping + eenheden)
        │ ok → TransportOrderService.CreateAsync → Processed (+ ResultEntityId)
        │ fout → Failed (FailureKind: validation | mapping | processing)
        │        na 3 pogingen (MaxAttempts) → DeadLettered
        ▼
Replay (POST api/edi/messages/{id}/replay): Failed/DeadLettered opnieuw;
DeadLettered krijgt een vers pogingbudget. Statuswijziging van een EDI-order
──► QueueOutboundStatusAsync ──► uitgaand "status"-bericht (direct Processed).
```

## Entiteitsmodel

- **`TradingPartner`** — code, naam, gekoppelde klant (`CustomerId`) + het
  klantkenmerk aan hun kant (`ExternalCustomerIdentifier`), `MappingProfile`
  (vandaag enkel `generic-json-v1`; partner-specifieke wire-formaten volgen pas
  zodra er een echte specificatie/sample is), actief-vlag, notities.
- **`EdiPartnerLocation`** — mapt de locatiecode van de partner
  (`ExternalLocationCode`, case-insensitief) op één masterlocatie.
- **`EdiMessage`** — richting (Inbound/Outbound), payload + `PayloadHash`
  (SHA-256), status `Received / Processed / Failed / DeadLettered / Duplicate`,
  `AttemptCount`, foutdetail + `ValidationErrorsJson`, machine-leesbare
  `FailureKind` (`mapping`/`validation`/`processing`; null op oude rijen — de
  mapping-issue-filter valt daar terug op een tekstmatch, één bronwaarheid in
  `EdiController.MappingIssueExpression`), en de resultaatlink
  (`ResultEntityType`/`ResultEntityId` → TransportOrder).

## Verwerking (generic-json-v1)

- **Dedupe bij ingest**: zelfde payloadhash óf zelfde `externalOrderId` bij dezelfde
  partner ⇒ rij wordt bewaard als `Duplicate` (audit) maar nooit herverwerkt.
- **`PrepareAsync`** is de ene herbruikbare kern achter échte verwerking én dry-run:
  parse van het generieke JSON (externalOrderId, customerReference,
  goodsDescription, stops, cargoItems), partner moet aan een klant gekoppeld zijn
  (anders `mapping`-fout), locatiecodes resolven via `EdiPartnerLocation`
  (onbekende code = blokkerende mapping-fout), eenheidscodes resolven — de
  EDI-code uit de klantconfiguratie (`CustomerPreferredUnit`) wint, dan een directe
  match op de globale eenheidscode; onresolveerbaar blijft vrije tekst zodat de
  order tóch importeert.
- Ordercreatie loopt door de ene interne `TransportOrderService.CreateAsync`
  (zelfde validaties/nummering/audit als handmatige invoer), met bronvermelding
  "EDI-bericht van {partner}".
- **Dry-run**: `POST api/edi/validate` draait exact dezelfde parse/mapping/
  eenheden-pipeline maar persisteert niets — retour `Valid`, `Errors` en een
  `WouldCreate`-samenvatting (aantallen stops/vrachtregels, geresolvede locatie- en
  eenheidscodes). `POST api/edi/simulate` bouwt een geldig voorbeeldpayload en
  ingest het echt (ontwikkelsimulator; zelfde payloadvorm als het Testen-tabblad).
- **Uitgaand**: enkel orders die via EDI binnenkwamen rapporteren hun status terug;
  het payload is klaar voor een transportprovider en is zonder provider meteen
  definitief (`Processed`).

## Endpoints & permissies (v20-splitsing)

Leesrecht, replay en testen zijn bewust gesplitst van partnerbeheer zodat een
breder publiek de inbox kan monitoren en mappings kan oefenen zonder configuratie
te kunnen wijzigen (doccomment `EdiController`). Elke schrijfactie is geauditeerd.

| Code | Endpoints | Standaard |
|---|---|---|
| `edi.view` | `GET api/edi/messages`, `…/messages/{id}`, `…/partners`, `…/stats` | management |
| `edi.test` | `POST api/edi/validate`, `POST api/edi/simulate` | management |
| `edi.retry` | `POST api/edi/messages/{id}/replay` | management |
| `edi.manage` | alles hierboven + `POST api/edi/inbound/{partnerCode}/{messageType}`, partner-CRUD (`POST/PUT api/edi/partners…`), locatiemappings (`POST/DELETE …/locations`) | management |

## Schermen (`/edi`)

Tabbladen verbergen zich zonder de vereiste permissie (niet uitgegrijsd):

- **Dashboard** — tellers per status, mislukte/dead-lettered, mapping-issues,
  verwerkt laatste 7 dagen, partners (incl. partners zonder klantkoppeling).
- **Berichten** — filterbare lijst (richting, status, partner, mapping-issues,
  zoeken op externe referentie), detailmodal met payload/validatiefouten, replay
  (`edi.retry`).
- **Handelspartners** / **Mappings** (`edi.manage`) — partner-CRUD incl.
  mappingprofiel en de locatiecode-mappings.
- **Testen** (`edi.test`) — vooringevuld voorbeeldpayload, "Valideren zonder te
  versturen" (dry-run) en echt simuleren.

## Bekende beperkingen

- Alleen berichttype **`order`** wordt inbound ondersteund; al het andere is
  "wordt (nog) niet ondersteund". Het inbound-endpoint is permissie-gated
  (`edi.manage`) — echte partnerkoppelingen zouden met API-keys authenticeren
  zodra een partnerintegratie bestaat; die bestaat vandaag niet. Uitgaande
  statusberichten worden opgeslagen maar nergens heen getransporteerd (geen
  transportprovider — rijen zijn meteen `Processed`). Er is één mappingprofiel
  (`generic-json-v1`); het profielveld is voorbereid op partnerformaten maar er is
  geen tweede parser. Mapping-issue-detectie op rijen van vóór de
  `FailureKind`-kolom steunt op een tekstmatch van de exacte Nederlandse
  foutzinnen.
