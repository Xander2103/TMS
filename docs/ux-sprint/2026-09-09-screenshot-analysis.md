# UX-sprint 2026-09-09 — screenshotanalyse

Screenshots staan in `docs/ux-sprint/screenshots/` (gekopieerd uit Downloads; ze ontbraken in de repo).
Per screenshot: wat er te zien is, en welke code/domeinflow verantwoordelijk is
(codemapping aangevuld na de architectuurinventaris).

## 01-contact-notifications.png — contactmodal + beforeunload

**Observatie.** Klant "Van Caudenberg BV", tab Contactpersonen, contactmodal open met blok
"Ontvangt meldingen" in een fieldset-achtige omkadering. Vinkjes: Planning/leververster,
ETA/vertraging, Levering/POD, Facturen, Creditnota's aan; Orderbevestigingen, Problemen met
levering, Herlevering, Betalingsherinneringen, Algemene klantmeldingen uit. Onderaan "Primair voor
dit type", knoppen Annuleren/Opslaan. Bovenop een Chrome-dialoog "Leave site? Changes you made may
not be saved" — een `beforeunload`-waarschuwing terwijl de gebruiker net een contact bewerkte.
Achter de modal staat de klantpagina met eigen Annuleren/Opslaan (paginaniveau) én "+ Contact
toevoegen".

**Wat dit betekent.** Er zijn twee opslaglagen (modal en pagina). De contactvoorkeuren lijken
via de modal opgeslagen te worden, maar de pagina beschouwt zichzelf daarna als dirty, óf de
modal schrijft alleen naar paginastaat die pas bij pagina-Opslaan naar de server gaat. Beide
verklaren zowel het niet-persisteren als de beforeunload-prompt.

**Verantwoordelijke code (uit inventaris).** `features/customers/components/CustomerContactsPanel.tsx`
(modal + checkboxes), `utils/contactRows.ts`, `api/customersApi.ts` /
`customerNotificationsApi.ts`, `CustomerDetailPage.tsx` / `CustomerForm.tsx` (paginabaseline, dirty
state), backend `Modules/Partners` (`CustomerContact`, `CustomerContactSubscriptionService`,
`CustomerNotificationCatalog`, `CustomersController`).

## 02-contactpersonen-layout.png — geneste kaders

**Observatie.** Drie lagen kaders: (1) buitenste fieldset met legend "Contactpersonen",
(2) daarin een kop "Contactpersonen" (herhaling) met rechts "+ Contact toevoegen",
(3) Type-filter los eronder, (4) tabel in eigen omkaderde kaart. Grote lege witruimte in het
buitenste kader. Filter en primaire actie horen niet bij elkaar (verschillende regels).
Pagina-Opslaan/Annuleren zowel boven als onder.

**Doelhiërarchie.** klantpagina → sectie → sectiekop (één keer) + gedempte omschrijving →
toolbar (filter links, + Contact toevoegen rechts) → tabel met één subtiele container.

**Verantwoordelijke code.** De fieldset/legend komt uit het herbruikbare sectiecomponent
(`SectionedForm`-sectie); de tweede kop + kaart uit `CustomerContactsPanel.tsx`. Zustersecties
(Adressen, Communicatie, Facturatie, …) delen hetzelfde patroon → fix in het herbruikbare
patroon, niet per pagina.

## 03-opening-hours.png — AM/PM, validatie, uitlijning

**Observatie.** Adres aanmaken, blok "Openingstijden" (opnieuw een fieldset-legend-kader).
Ma: `08:00 AM — 12:00 AM  [Notitie]  ×   01:00 PM — 05:00 PM [Notitie] ×  + Tijdvak`, allemaal
op één horizontale rij; onder het eerste tijdvak de foutmelding "Eindtijd moet na starttijd
liggen." (12:00 AM = middernacht). Di–Zo tonen "Gesloten + Tijdvak". Knoppen "Kopieer maandag naar
weekdagen" / "Wis alles". Onderaan vrije-tekstfallback. Header/footer tonen
"Niet-opgeslagen wijzigingen" (correct: nog niet aangemaakt).

**Wat dit betekent.** Native `<input type="time">` in Chrome met en-US-locale toont AM/PM; de
gebruiker kiest "12:00" in het AM-deel en krijgt middernacht. De waarde zelf blijft "HH:mm"
(24u) maar de invoer-ergonomie misleidt. Tijdvakken zijn een flex-rij zonder kolommen; de
foutmelding staat als extra flex-kind binnen het tijdvak, waardoor de rij verspringt.

**Verantwoordelijke code.** `features/locations/components/OpeningHoursEditor.tsx` +
`opening-hours-editor.css`, `features/locations/openingHours.ts`, `LocationForm.tsx`;
backend `Modules/Locations` (opening-hours DTO/validatie).

## 04-dossier-pricing.png — Verkoop & prijs zonder handeling

**Observatie.** Dossier 0003 (Van Caudenberg BV, ref. "Pal in 9/9", activiteit Opslag).
Indicatoren "Operationeel: —  Prijs: —". Aandacht: "Nog geen verkooplijnen of prijs." met link
"Ga naar prijs". Sectie Verkoop & prijs: "€ 0,00" + "Nog geen verkooplijnen." en verder niets —
geen knop, geen invoer. Goederen: "Goederen worden bijgehouden op de transportopdracht." (er is
geen transportopdracht; alleen Opslag).

**Wat dit betekent.** Zonder transportopdracht is er geen drager voor verkooplijnen, dus het
prijsblok kan niets aanbieden; "Ga naar prijs" springt naar een sectie zonder doel. € 0,00 is hier
geen prijs maar "geen prijs" — twee betekenissen in één weergave.

**Verantwoordelijke code.** `features/dossiers/components/DossierPriceSummary.tsx`,
`AttentionPanel.tsx`, `DossierDetailPage.tsx`, readiness-regel `pricing.none` in
`Modules/Dossiers/Services/DossierReadinessService.cs`; prijsdomein `Modules/Orders`
(pricing lines) + `Modules/Tarification`.

## 05-dossier-overview.png — generieke lijst

**Observatie.** Kolommen: Nummer | Titel | Klant | Verantwoordelijke | Opdrachten | Open incidenten
| Status. Titel = "Van Caudenberg BV — 09-09-2026" (auto-gegenereerd, herhaalt klant). Zoekveld
"Zoek op nummer of titel...". Geen referentie, klantnummer of prijs.

**Doel.** Dossiernr. | Referentie | Klant | Klantnr. | Prijs | Verantwoordelijke | Opdrachten |
Open incidenten | Status; zoeken op nummer, referentie, klantnaam, klantnummer; prijs met
dezelfde semantiek als detail, "—" voor niet-geprijsd.

**Verantwoordelijke code.** `features/dossiers/pages/DossiersPage.tsx` + `dossiers.css`,
`api/dossiersApi.ts`, backend lijst-DTO/query in `Modules/Dossiers` (`DossiersController`,
`DossierService`), klantnummer op `Modules/Partners/Entities/Customer.cs`.

## 06-route-current.png — passieve route

**Observatie.** Dossier met Direct transport 0002 (Concept). Aandacht: "laad- en loslocatie zijn
nog onbekend (nodig om te bevestigen)" en "nog geen planningsdatum", beide "Ga naar route".
Route: "Laden — Nog te bepalen", "Lossen — Nog te bepalen", knop "Route bewerken". Goederen:
"+ Goederen". Verkoop & prijs: € 0,00, rij "0002 Direct transport € 0,00", knoppen "Prijsdetails"
en "+ Verkooplijn".

**Wat dit betekent.** De kernvraag "waar laden, waar lossen" is niet invulbaar zonder eerst
"Route bewerken" (opent een drawer). Beide aandachtpunten wijzen naar dezelfde sectie zonder
onderscheid in het ontbrekende veld.

**Verantwoordelijke code.** `features/dossiers/components/DossierRouteSummary.tsx`,
`RouteDrawer.tsx`, `SectionDrawer.tsx`, `orderDrawerState.ts`; hergebruik van
`features/transport-orders/components/sections/RouteSection.tsx`, `useStopMutation.ts`;
locatiezoeken `features/locations/components/LocationSelect.tsx`, `LocationQuickCreateDialog.tsx`;
backend stops in `Modules/Orders`, locaties in `Modules/Locations`.

## 07-tas-reference.png — legacy TAS (workflowreferentie, géén visueel voorbeeld)

**Observatie.** Klassiek WinForms-scherm. Bovenaan compacte kop: datum, dossiernr, klant
(nummer + naam), referentie, transporttype, dossiertype, planningsgroep, verantwoordelijke, en
rechts direct Verkoop/Aankoop-bedragen met status. Daaronder een grid "Opdrachten" met kolommen
Laadnaam | Laadplaats | Losnaam | Losplaats | CMR | Vk | Ak | BC | Nr — elke rij is een opdracht
waar de planner direct in de cel kan typen. Onder: tabs Algemeen/Opdracht/Product/Verkoop/…

**Wat we overnemen (workflow, niet vorm).** Laad- en losinformatie zijn *altijd* zichtbaar en
*direct* bewerkbaar; kop toont referentie, klantnummer én verkoopprijs zonder extra klik; de
verkoopstatus staat naast het bedrag. Wat we niet overnemen: dichte grijze WinForms-esthetiek,
afkortingen (Vk/Ak/BC), knoppen met "…".
