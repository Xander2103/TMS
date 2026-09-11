# Hardening-ronde 2026-09-10 (pre-commit, UX-sprint 2026-09-09)

Afsluitende verhardingsronde op de UX-sprint vóór commit. Geen herontwerp; elke zorg uit de
architectuurreview is eerst tegen de bestaande implementatie getoetst en daarna opgelost,
bewezen veilig, of expliciet als product-/releasebeslissing gemarkeerd.

## 1. Prijs van niet-transportactiviteiten — PRODUCTBESLISSING (bewust uitgesteld)

Domeincontract (`docs/dossiers.md`): verkooplijnen leven uitsluitend op `TransportOrder`
(`TransportOrderPricingLine`/`ServiceLine`). Zelfstandige activiteiten (opslag, kraanwerk ter
plaatse, plateau) hebben geen opdracht en dus geen prijsdrager; `InvoiceLine.TransportOrderId`
is nullable (vrije factuurregel), maar er bestaat geen dossier- of activiteitsniveau prijsmodel.
Een echt activiteits-/dossierprijsconcept is een nieuw domeinontwerp en wordt hier NIET verzonnen.

Wat wel is gedaan zodat de bestaande UI geen verkeerd gedrag uitlokt:
- Een dossier met enkel zelfstandige activiteiten toont in *Verkoop & prijs* alleen "Nog geen
  prijs" plus de tekst dat deze activiteiten in deze versie niet op het dossier geprijsd worden
  en dat er géén transportopdracht aangemaakt mag worden enkel om een prijs vast te leggen.
  De knoppen "Transportopdracht aanmaken" / "+ Activiteit toevoegen" verschijnen daar niet meer.
- Readiness geeft `pricing.none` alleen nog wanneer het dossier een transportvormige activiteit
  heeft (er is dan een actie); een opslag-only dossier krijgt geen prijsaandachtspunt.

## 2. "Eerste transportopdracht"-aanname — OPGELOST

- `ReadinessIssueDto` += `TransportOrderId`, `ActivityId` (additief). Elke opdrachtregel
  (`order.confirm.stops`, `route.date_missing`, `pricing.incomplete`, `pricing.stale`,
  `pricing.missing`) noemt haar opdracht; `route.order_missing` noemt haar activiteit.
- Dossierpagina: expliciete doelopdracht via `DossierOrderSwitcher` (route- én prijssectie,
  zelfde selectie) zodra er ≥ 2 transportactiviteiten zijn. Aandacht-acties selecteren eerst de
  juiste opdracht en focussen daarna het veld (uitgesteld tot de opdracht geladen is).
- Wisselen is vergrendeld zolang de route-editor onopgeslagen wijzigingen heeft
  (`onDirtyChange`), zodat invoer nooit op een andere opdracht kan landen.
- `DossierOrderDto` += `IsPriced` (backend-definitie, UI leidt niets meer af uit "bedrag > 0").

## 3. Financiële presentatie bij gedeeltelijke prijs — OPGELOST

Het dossiertotaal is een SOM over de geprijsde opdrachten. Bij meerdere opdrachten waarvan niet
elke geprijsd is toont detail "€ X · n van m opdrachten geprijsd" en de lijst "€ X n/m geprijsd"
(met titel). `AgreedPriceTotal`/`PricedOrderCount`/`OrderCount` bestonden al; alleen presentatie.

## 4. `OrderPricingState.IsPriced` en € 0 — OPGELOST (beslissing: € 0 is een geldige prijs)

Bewijs: een override mag 0 zijn (met reden) en `OneOffPricingError` accepteert
`OneOffFixedAmount = 0`; de motor leidt dan `AgreedPrice = 0` af — die opdracht las als
"niet geprijsd". Nieuwe definitie (provenance, niet bedrag):

```
PriceIsManual
|| (PricingSource == OneOff && OneOffFixedAmount != null)
|| AgreedPrice > 0
```

`AgreedPrice == 0` zónder provenance = de lege motor-nul ("Geen tarief geconfigureerd"),
`null` = nooit afgeleid/ingevoerd. Eén EF-vertaalbare bron: `OrderPricingState.IsPricedExpression`
(+ `IsUnpricedExpression` uit dezelfde boom); de C#-overloads zijn ervan gecompileerd. Alle
paden (lijst, financials, per-opdrachtvlag, readiness, aandachtsteller) gebruiken de expressie;
`OrderPricingStateTests` bewijst SQL-vertaling (top-level én genest) en NULL-semantiek,
`DossierServiceTests.Parity_*` de pariteit over de leespaden.

Gedocumenteerde beperking: manuele verkooplijnen die netto exact € 0 opleveren zonder override
of eenmalige afspraak lezen als "niet geprijsd" (leg zo'n opdracht vast via een override).

## 5. Prijspermissies — BEWEZEN VEILIG (bewust asymmetrisch, server-side afgedwongen)

| Actie | Server-gate |
|---|---|
| Afgesproken prijs (PUT order, OneOff + bedrag) | `orders.edit \| orders.manage` (controller) |
| Override `PriceIsManual` via dezelfde PUT | + `orders.override_price`, fail-closed in `TransportOrderService.UpdateAsync` |
| Verkooplijnen `PUT /pricing/lines` | `orders.override_price \| orders.manage` (controller) |

De afgesproken prijs is commerciële intake-data van de opdracht (zelfde formulier), vrije lijnen
vervangen motoroutput → strengere gate. Vastgelegd in `DossierWorkSurfacePermissionTests`.

## 6. Route-autoaanmaak permissies — BEWEZEN SAFE/CONSISTENT

`POST /dossiers/{id}/activities/{activityId}/create-order` vereist enkel `dossiers.manage`
(sinds de dossier-foundation-wave; zelfde regel als `AddAsync` met `CreateLinkedOrder`): de
opdracht is het uitvoeringsrecord van de activiteit. Géén `orders.create`. Frontend is strikter
(route-editor: `dossiers.manage && orders.edit|manage`, omdat hij meteen ook stops PUT). Test
in `DossierWorkSurfacePermissionTests`.

## 7. Tweestaps route-aanmaak — OPGELOST

`ensureOrder()` hergebruikt de aangemaakte opdracht via `baseOrder` én
`activity.linkedTransportOrderId`; ook wanneer de vervolg-GET faalt wordt bij retry geladen, niet
opnieuw aangemaakt (backend weigert dat bovendien). Invoer blijft staan; "Opnieuw laden" na een
mislukte opdrachtlading. Tests: `Dossier work surface — failure paths`.

## 8. 409-concurrency — BEWEZEN (test toegevoegd)

Banner, invoer blijft, Herladen neemt de collega-versie over, volgende save gebruikt die versie.

## 9. Stale-while-refetch met onopgeslagen paginabewerkingen — BEWEZEN (test toegevoegd)

`CustomerForm` leidt zijn state één keer uit `initial` af en wordt door `reload()` niet
geremount (`customerDetailPageEditRefetch.test.tsx`).

## 10. Gedeeltelijke contact-save — OPGELOST

Bewerken: retry was al convergent (test e-ter). Nieuw contact: na een geslaagde POST en een
mislukte notificatie-PUT promoveert de dialoog naar bewerk-modus voor het aangemaakte contact —
een retry doet een PUT, nooit een tweede POST; vinkjes en invoer blijven (test e-quater).

## 11. Blast-radius gedeelde UI — BEWEZEN

- `Modal` (portal, 111 call sites): CSS-variabelen staan op `:root`; regressietest
  `Modal.portal.test.tsx` (body-portal, geen geneste form, footer-submit via `form=`, Escape/
  backdrop, busy, focusherstel, overflow-herstel).
- `FormSection` flat: alleen binnen `SectionedForm` (7 hosts); framed elders ongewijzigd.
- `LocationSelect` → `/api/addresses/picker`: enige consument is `RouteSection` (orderformulier,
  intake, dossier-route-editor, ongebruikte `RouteDrawer`); picker en `GET /locations/{id}`
  dragen dezelfde `locations.view` als het oude options-endpoint. `getLocationOptions` blijft
  bestaan voor EDI-mapping, attendance-instellingen en magazijnen.
- `SearchableSelect` sync-modus: 29 consumenten, bestaande suites groen.

## 12. Afgesproken prijs geblokkeerd door onvolledige route — OPGELOST (2026-09-11)

**Regressie (browser-smoke):** dossier met transportopdracht waarvan de route nog onvolledig is →
Verkoop & prijs → Afgesproken prijs € 450 → Opslaan → "Elke stop heeft een locatie of minstens
een plaatsnaam nodig."

**Oorzaak (koppeling):** het paneel bouwde uit de geladen `TransportOrderDetail` een volledige
`PUT /api/transport-orders/{id}` (`orderValuesFromDetail` → `buildSubmitPayload`). Die echode álle
bestaande stops, en `TransportOrderService.UpdateAsync` → `ValidateAsync` valideert elke
meegestuurde stop (locatie of plaatsnaam verplicht). Een prijs-wijziging droeg dus impliciet een
routevalidatie mee. Nergens bestond een gerichte prijsmutatie (wel `/pricing/lines`,
`/recalculate`, `/status`, `/confirm`, `/reopen`; geen enkel commando voor de eenmalige afspraak).

**Oplossing:** nieuw, smal commando `POST /api/transport-orders/{id}/pricing/one-off` met body
`{ fixedAmount: decimal|null, version }` → `TransportOrderService.SetOneOffPriceAsync`:
- zet `PricingSource = OneOff` + `OneOffFixedAmount` (€ 0 blijft een bewuste prijs), of bij `null`
  terug naar contractprijs; bestaande one-off-details (inbegrepen tijd, extra uurtarief, nota)
  blijven bewaard bij enkel een bedragwijziging;
- valideert uitsluitend prijsregels (`OneOffPricingError`, `IncludedTimeOverrideError`) — raakt geen
  stop aan; routeregels blijven ongewijzigd op de order-PUT (bewezen in dezelfde test) en op de
  bevestigingsgrens;
- zelfde statusregel als de PUT (Draft/Submitted/Confirmed), versie-gate (409 met huidige staat),
  Locked/Invoiced geweigerd, een bestaande override blijft exact wat hij is en behoudt de
  `orders.override_price`-check in de pipeline (fail-closed);
- draait dezelfde prijspipeline (`ApplyPricingAsync`) → AgreedPrice, regels, snapshot, readiness en
  dossierfinancials volgen; `Version` wordt gebumpt; audit `OrderPricing/oneOffPriceSet`.
- Controller-gate `orders.edit | orders.manage` (ongewijzigde matrix).

Frontend: `setOrderOneOffPrice()` in `transportOrdersApi.ts`; `DossierPricePanel` stuurt enkel
`{ fixedAmount, version }`. Tests: `OneOffPricingTests.SetOneOffPrice_*` (scenario incl. bewijs dat
de PUT nog weigert, € 0, wissen, negatief, Locked/Invoiced, stale versie, override-permissie,
Completed), `DossierReadinessTests.AgreedPriceOnAnIncompleteRoute_*` (prijswaarschuwing weg,
routewaarschuwingen blijven, refresh toont de prijs), `DossierWorkSurfacePermissionTests`,
`dossierWorkSurface.test.tsx` (PUT wordt nooit meer aangeroepen voor de prijs).

## 13. Opdrachtwissel in een dossiersectie sprong naar de paginatop — OPGELOST (2026-09-11)

**Regressie (browser-smoke):** dossier met meerdere transportopdrachten → scroll naar Verkoop &
prijs → wissel 0001 → 0004 → de pagina springt naar boven.

**Oorzaak (gemeten in Chrome, MutationObserver op de eerste React-commit na de klik):**

| moment | documenthoogte | scrollY | sectie Route | sectie Prijs |
|---|---|---|---|---|
| vóór de klik | 2749 | 1383 | 883 | 706 |
| eerste commit ("Route laden…") | 1504 | 138 | 148 | 250 |
| opdracht geladen | 2620 | 138 | 883 | 584 |

De selectie is lokale state (`selectedActivityId`), er is geen navigatie, geen `key`-remount, geen
`scrollTo`, geen her-registratie in de sectieregistry. Wél wordt `firstOrderLoading` waar zodra
een ándere opdracht geladen wordt (`loadedOrder.id !== firstLinkedOrderId`), en dan vervangen
`DossierRouteEditor`, `DossierGoodsSummary` en `DossierPricePanel` hun body in dezelfde commit door
een éénregelige placeholder. Het document krimpt ~1200 px, de browser klemt de scrollpositie op de
nieuwe maximale hoogte (138), en die positie blijft staan als de inhoud terugkomt. Een refetch van
dezelfde opdracht (na een save) had dit nooit: dan blijft de body gemonteerd (stale-while-refetch).

**Oplossing (gedeeld, geen per-component hack):** nieuwe primitive `components/ui/RetainedHeight`
— houdt tijdens `retain` de laatst gemeten hoogte vast als `min-height` (layout-effect, vóór de
paint) en zet `aria-busy`; laat los zodra echte inhoud rendert. `DossierDetailPage` wikkelt de
bodies van Route, Goederen en Verkoop & prijs (`.dossier-section-body`) erin met
`retain={firstOrderLoading}`. Er wordt geen andere opdracht getoond onder het verkeerde label en er
is geen expliciete scroll-restauratie: de pagina krimpt gewoon niet meer. `goTo(section, field)`
(Ga naar route / Ga naar prijs / aandacht) is ongewijzigd en scrollt/focust nog steeds bewust.

**Browser-verificatie (na fix, dossier 0006 met ORD-0005/ORD-0014):** Route-wissel heen en terug:
scrollY 456 → 456, sectietop 249 → 249, documenthoogte nooit onder de eindhoogte, reservering
actief tijdens het laden. Prijs-wissel naar de kortere opdracht terwijl de pagina helemaal onderaan
stond: scrollY 1383 → 1254 (browser-klem op het paginaeinde omdat de nieuwe inhoud 129 px korter
is), sectie blijft midden in beeld (top 389 → 510 bij 1366 px hoog); terugwissel: scrollY
onveranderd. Ga naar route / Ga naar prijs selecteren de juiste opdracht, focussen het veld en
highlighten de sectie.

Tests: `dossierWorkSurface.test.tsx` "order switch keeps the page in place" — dezelfde sectienodes
vóór/tijdens/na de wissel, `min-height` = gemeten hoogte en `aria-busy` tijdens het laden, beide
weer weg erna, geen locatiewijziging in de router, geen `scrollTo`/`scrollIntoView`; plus bewijs dat
"Ga naar prijs" wél scrollt en focust.

## 14. Lege/onvolledige stop verdween stil bij "Route opslaan" — OPGELOST (2026-09-11)

**Regressie (browser-smoke):** "+ Extra losstop" → niets invullen → "Route opslaan" → de stop is
weg zonder melding. Erger (in code gevonden): `isEmptyStopRow` keek niet naar `id`, dus een
bestaande stop waarvan de plaats en vrije naam leeggemaakt werden, werd uit de PUT gefilterd en
daarmee server-side verwijderd.

**Contract (`isEmptyStopRow`, gedeeld met intake en orderformulier):** "leeg" = nieuwe stop
(`id === null`) met álle persistente velden op de `emptyStop`-default (adres, plaats, naam, land,
datum, tijdvenster, tijdseis, gevraagd/bevestigd venster, vroegst/laatst, afspraak, referentie,
alle instructies, inbegrepen-tijd-override). Al het andere is *onvolledig*, gaat mee in de PUT en
faalt inline op de bestaande regel "locatie of plaatsnaam" — verdwijnt nooit.

**Dossier-route-editor:**
- Gezaaide placeholderrijen (`seeded`: de ontbrekende laad-/losstop die de editor zelf toont)
  worden, als ze onaangeraakt zijn, zonder vraag weggelaten én na de save opnieuw gezaaid — ze
  verdwijnen dus nooit uit beeld.
- Een rij die de planner zelf toevoegde en leeg liet: "Route opslaan" opent één dialoog voor alle
  lege rijen — "Er is 1 lege stop zonder adres. Wil je deze lege stop verwijderen en de route
  opslaan?" (meervoud via `_one/_other`), acties **Terug naar route** / **Lege stop(s) verwijderen
  & opslaan**. Terug = niets gebeurt, rij blijft; bevestigen = rijen uit de editor (goederenlinks
  worden hernummerd) en de rest wordt opgeslagen.
- Verwijderen per stop: onaangeraakte rij direct; rij met gegevens of bestaande stop → bevestiging
  ("Stop verwijderen?", pas definitief bij Route opslaan).
- Validatiefouten worden op de getoonde index gezet (`toDisplayedField`), zodat een weggelaten
  placeholder vooraan nooit de verkeerde stop markeert.
- PUT-/versiesemantiek en backend-validatie ongewijzigd.

Tests: `dossierWorkSurface.test.tsx` "empty and incomplete stops in the inline route editor" (7
scenario's: vraag vóór weglaten, Terug behoudt, bevestigen slaat op met alle bestaande ids, alleen
referentie / alleen datum blokkeert inline, alleen plaats is geldig en wordt opgeslagen, bestaande
stop met leeggemaakt adres blokkeert i.p.v. te verdwijnen, Verwijderen direct vs. met bevestiging);
`stopRowClassification.test.ts` (elk persistent veld apart, bestaande stop nooit leeg, UI-state en
witruimte genegeerd). Browser-smoke op ORD-0005: dialoog verschijnt, Terug behoudt 3 stops, stop
met enkel referentie blokkeert met de inline melding.
Bevestigen & opslaan op ORD-0014 (concept): dialoog → "Lege stop verwijderen & opslaan" → toast
"Route opgeslagen.", de twee ingevulde stops blijven, geen onopgeslagen staat. Op ORD-0005 weigerde
de backend de PUT met "De prijs van deze order is vergrendeld" (prijsvergrendeling, los van deze
wijziging); de editor toonde de fout en hield beide bestaande stops — niets ging verloren.
Verwijderen op een stop met referentie vroeg bevestiging; Annuleren behield de stop.

## 15. Intentionele € 0-waarschuwing op drie niveaus + prijs voor zelfstandige activiteiten — ONTWERP 2026-09-11

De productbeslissing van §1 ("bewust uitgesteld") is herzien: zelfstandige activiteiten (Opslag,
Kraan, …) moeten zelfstandig geprijsd kunnen worden zonder kunstmatige transportopdracht.
Architectuur, contracten, permissie, concurrency, migratie, downstream-audit en testplan staan in
`2026-09-11-activity-pricing-design.md`. Dezelfde golf voegt de niet-blokkerende
`pricing.zero`-waarschuwing toe (aandacht, Verkoop & prijs, dossierlijst) volgens de bestaande
provenance-semantiek (`OrderPricingState`).
