# UX-sprint 2026-09-09 — implementatierapport

Status: geïmplementeerd op branch `nav-redesign` (bovenop 194f999), **niet gecommit, niet gedeployed**.
Ontwerp: `2026-09-09-sprint-design.md`; screenshotanalyse: `2026-09-09-screenshot-analysis.md`.

## 1. Wat de inventaris opleverde

- **Meldingsabonnementen**: geen aparte entiteit; een abonnement is een koppelrij op een
  `CustomerCommunicationRule` per type, met 10 stabiele machine-keys in `CustomerNotificationCatalog`.
  Twee schrijfpaden (contactvelden PUT en `PUT …/contacts/{id}/notifications` met diff-vervanging).
  De backend was correct.
- **Route-stops**: `TransportOrderStop` is al geordend (`Sequence`), met `StopType Loading|Unloading`,
  `LocationId` + adres-/contactsnapshot, `PlannedFrom/To`, tijdseisen, referentie, instructies. Geen
  per-stop endpoint: de hele collectie gaat in `PUT /api/transport-orders/{id}` met id-behoud en
  `Version`-token (409 met actuele staat).
- **Locaties**: `Location` + `CustomerLocationLink`; `GET /api/addresses/picker` is de serverzoekbron
  (naam, code, straat, postcode, plaats, nu ook externe referentie/alias), gegroepeerd
  Klantadres → Recent → Alle.
- **Prijs**: `TransportOrder.AgreedPrice` wordt door de backend afgeleid uit de som van tellende
  prijsregels, tenzij override (`PriceIsManual` + reden + `orders.override_price`), legacy afgesproken
  prijs, of `PricingSource = OneOff` + `OneOffFixedAmount` (eenmalige prijsafspraak, geen extra
  permissie, overleeft herberekening). Verkooplijnen zijn `TransportOrderPricingLine` (vrije regel =
  Kind Manual via `PUT …/pricing/lines`, `orders.override_price|manage`).

## 2. Root causes

1. **Contactmeldingen niet persistent + valse dirty-state**: (a) in bewerkmodus stonden de zelf-opslaande
   panelen en hun modal binnen `<form onChange={touch}>` van `CustomerForm`; `Modal` was geen portal, dus
   een geneste `<form>`; React liet `change`/`submit` doorbubbelen → elk vinkje zette de pagina dirty
   (beforeunload) en de modal-Opslaan voerde ook de pagina-submit uit; (b) elke contactmutatie riep
   `reload()` aan vóór de notificatie-PUT en de pagina toonde `LoadingState` (hele boom unmount);
   (c) de asynchrone GET overschreef gezette vinkjes en een mislukte GET leidde tot een destructieve PUT.
2. **AM/PM**: uitsluitend de native `<input type="time">` in Chrome met en-US UI-taal. De opslag en de
   wire waren al 24u (`"HH:mm"`); parsing in de app was niet fout, wel de invoer-ergonomie (12:00 zonder
   PM = 00:00). Opgelost met een tekstgebaseerde 24u `TimeInput`.
3. **Ga naar prijs**: `document.getElementById('sectie-prijs').scrollIntoView` op een sectie die al in
   beeld stond, zonder focus en zonder actie in die sectie (zonder opdracht bevatte ze niets).
4. **Prijs € 0,00**: `SUM(AgreedPrice ?? 0)`; de motor schrijft 0 als er geen tellende regel is.
   Nu één definitie "geprijsd" (`OrderPricingState.IsPriced` = `PriceIsManual || AgreedPrice > 0`) in
   readiness, lijst en financials; niet-geprijsd toont "—" / "Nog geen prijs".

## 3. Resulterende UX (workflow)

- **Dossierlijst**: Dossiernr. | Referentie | Klant | Klantnr. | Prijs | Verantwoordelijke | Opdrachten |
  Open incidenten | Status; zoeken serverzijdig op nummer, referentie, klantnaam, klantnummer.
- **Dossierdetail**: Aandacht-acties gaan via een sectieregistry naar het exacte veld (`field` uit de
  backend). Route is een werkblad: laad-/losstops direct invulbaar met serverzoekende autocomplete
  (naam / straat, postcode plaats / groep · klant), "+ Nieuw adres" met automatische selectie, extra
  stops, ↑/↓, datum, 24u-tijdvenster, referentie; expliciet "Route opslaan" met dirty-indicator; bij een
  transportactiviteit zonder opdracht wordt de opdracht bij het opslaan aangemaakt. Verkoop & prijs
  toont "Nog geen prijs" of het totaal, een inline "Afgesproken prijs" (eenmalige prijsafspraak),
  de verkooplijnen met inline "+ Verkooplijn" en verwijderen van manuele regels; zonder opdracht een
  directe actie "Transportopdracht aanmaken".
- **Contactpersonen**: platte sectie (titel + omschrijving), toolbar (filter links, primaire actie
  rechts), één tabelcontainer; opslaan = contact PUT → notificaties PUT → herladen (stale-while-refetch).
- **Openingsuren**: CSS-grid per dag (dag | van | – | tot | notitie | ×), tijdvakken gestapeld, fout in
  eigen rij, 24u-invoer.

## 4. Wijzigingen (kern)

Backend (geen migratie): `OrderPricingState.cs` (nieuw), `DossierDtos.cs` (+CustomerReference,
CustomerNumber, AgreedPriceTotal, PricedOrderCount; financials +PricedOrderCount), `DossierService.cs`
(projectie + zoeken), `DossierReadinessService.cs` (Field gevuld, `pricing.missing`, precedentiebug,
`route.order_missing` → sectie route), `CustomerAddressService.cs`/`CustomerAddressDtos.cs`
(picker in SQL, +CustomerNames, ExternalReference/alias).
Frontend: `components/ui` (`TimeInput`+`timeText`, `PanelHeader`, `SelfSavingPanel`, `Modal` portal,
`FormSection` flat, `SearchableSelect` async/rijk, `Button` ref), `features/dossiers`
(`sectionRegistry.ts`, `DossierSectionsProvider`, `DossierRouteEditor`, `DossierPricePanel`,
`AttentionPanel`, `DossierDetailPage`, `DossiersPage`, css, locales `dossierSheet.json`),
`features/transport-orders/.../RouteSection.tsx` (sheet-modus, TimeInput), `useOrderFormData.ts`
(`useLocationHours`), `features/locations` (`LocationSelect` picker, `OpeningHoursEditor`,
`LocationForm`, `LocationDetailPage`), `features/customers` (`CustomerForm`, `CustomerDetailPage`,
`useCustomer`, `CustomerContactsPanel`, Adressen/Communicatie/Facturatie koppen).

## 5. Tests en poorten

Frontend: 224 bestanden / 1337 tests groen (vitest), `tsc -b` 0 fouten, `eslint .` 0 problemen,
`vite build` OK. Backend: 2477/2477 groen. Nieuwe/uitgebreide suites: TimeInput (40),
SearchableSelect async (8), LocationSelect (6), OpeningHoursEditor (+9), contactNotifications (+7),
customerFormPanelIsolation, customerDetailPageStaleWhileRefetch, dossiersPage (7),
dossierWorkSurface (9), FormSection/PanelHeader/SelfSavingPanel; backend DossierReadinessTests (10),
OrderPricingStateTests (7), DossierServiceTests (+3), CustomerAddressServiceTests (+3),
CustomerContactSubscriptionServiceTests (+1).

Handmatig in de browser (lokale stack): lijstzoeken op referentie en klantnummer; "Ga naar prijs" →
focus op afgesproken prijs; € 450 opslaan → totaal/chip/aandacht bijgewerkt en na refresh behouden;
extra losstop via autocomplete + nieuw adres via quick-create → automatisch geselecteerd, 4 stops na
refresh; "Ga naar route" op activiteit zonder opdracht → focus op laadlocatie; opslaan maakte de
opdracht aan; contact aanmaken met 5 meldingen → heropenen/refresh exact, één verwijderen → exact 4,
velden intact, geen unsaved-prompt bij verlaten; openingsuren 08:00–12:00 / 13:00–17:00 gestapeld,
foutrij verschuift de notitiekolom niet (x 784 → 784).

## 6. Open punten / risico's

- `RouteDrawer.tsx` is niet meer in gebruik op de dossierpagina (blijft getest door
  cargoStopRemap.test); kandidaat voor verwijdering.
- Native `<input type="time">` bestaat nog in warehouses, tarification, employee-planning, portaal.
- Bij dossiers met meerdere transportopdrachten werken route- en prijswerkblad op de eerste opdracht
  (de overige staan als rijen in Verkoop & prijs).
- In dev worden locatiedetails per stop meerdere keren opgehaald (hours-hint + labelcache + StrictMode).
- Lokale testdata: dossier 0007 wijst naar een verwijderde opdracht (404) — toont nu een eerlijke
  melding; dossier 0009 kreeg € 450 en dossier 0002 een transportopdracht tijdens de validatie; adres
  "Smoke Nieuw Depot" en contact "Smoke Contact" zijn aangemaakt.
- Docker Desktop + Postgres-container zijn gestart en draaien nog; API en Vite zijn gestopt.
