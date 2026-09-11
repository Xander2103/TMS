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
