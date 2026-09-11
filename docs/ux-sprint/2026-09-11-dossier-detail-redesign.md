# Dossierdetail — navigatiegebaseerde werkruimte (UI/UX-herontwerp 2026-09-11)

Referentie: `screenshots/DossierUIUX.png`. Status: implementatie, niet gecommit, niet gedeployed.

## Oud

Eén lange kolom (`max-width: 960px`): kop met statuschips, Aandacht-blok, daaronder ALLE
werkbladen tegelijk gemonteerd (activiteiten, route-editor, goederen, Verkoop & prijs-editor,
ingeklapte documenten/notities/meer). Begrijpen en bewerken zaten door elkaar; het
sectieregister (`goTo(section, field)`) scrolde naar een DOM-anker op dezelfde pagina.

## Nieuw: shell + subsecties

```
DossierDetailPage (shell, blijft gemonteerd bij elke subsectiewissel)
├─ DossierHeader (compact: nummer + status, klant · datum · ref · entiteit, + Activiteit, Meer ▾)
├─ 409-banner
├─ AttentionPanel als horizontale strip (icoon · tekst · Ga naar …)
├─ DossierSubnav (Overzicht · Activiteiten · Route · Goederen · Verkoop & prijs · Documenten · Historiek)
└─ actieve subsectie
   ├─ DossierOverview (ALLEEN-LEZEN, tweekoloms samenvattingskaarten + Open …-acties)
   ├─ DossierActivitiesSection (ActivityList + legacy opdrachten + toevoegen)
   ├─ DossierRouteSection (opdrachtwisselaar + bestaande DossierRouteEditor, ongewijzigd)
   ├─ DossierGoodsSection (wisselaar + DossierGoodsSummary + GoodsDrawer-flow)
   ├─ DossierPriceSection (eenheidswisselaar + bestaande DossierPricePanel, ongewijzigd)
   ├─ DossierDocumentsSection (OrderDocumentsPanel van de doelopdracht)
   └─ DossierHistorySection (notities/omschrijving/aanmaak, audittrail, financieel · relaties · incidenten)
```

- **Routing**: `/dossiers/:id/:section?` — één route-element, dus de shell (dossier, geladen
  opdracht, selectie, dirty-vlag) blijft gemonteerd; de subsectie volgt de URL. Refresh,
  deeplinks en terug/vooruit werken via de router. Onbekend segment → Overzicht.
- **Dossierstate** (dossier, `loadedOrder`, `selectedActivityId`, `routeSelectionId`,
  `routeDirty`, dialogen) blijft in de shell; een tabwissel herlaadt niets.
- **Aandacht → sectie/veld**: `goToSection` navigeert naar de subsectie en zet `pendingJump`;
  zodra de subsectie gemonteerd is (register-callback in commit) én het doel klaar is
  (opdracht geladen of zelfstandige eenheid), roept een effect het bestaande
  `registry.goTo(section, field)` aan: scroll + veldfocus + highlight, ongewijzigd.
- **Onopgeslagen route**: een tabwissel is een pathname-wissel → de bestaande
  `UnsavedChangesGuard` van de route-editor vraagt bevestiging; annuleren houdt de editor
  (en de invoer) staan.
- **Scroll**: geen `scrollTo`; RetainedHeight blijft rond de opdrachtgebonden bodies binnen
  Route/Goederen/Prijs. Bij een tabwissel wordt enkel de subnav weer in beeld gebracht
  wanneer die boven het venster uit gescrold was.
- **Overzicht**: leest uitsluitend `dossier` (activiteiten met prijsvelden, financials,
  readiness) en de al geladen doelopdracht (stops, goederen). Geen invoervelden.
- **Additieve read-model-velden** op `DossierDetailDto` (één extra query, geen N+1):
  `DocumentCount`, `DocumentTypes` (distinct), `LastChangedAt` (max van dossier/activiteiten/
  opdrachten `UpdatedAt`).
- Sectieregister-id's: `activiteiten`, `route`, `goederen`, `prijs`, `documenten`,
  `notities`/`meer` → tab `historiek`.

## Bewust buiten scope

Domein/flows (route-editor, prijs, activiteitsprijs, permissies), klant-/opening-hours-werk,
globale navigatie. `DossierRouteSummary` wordt hergebruikt voor de Goederen-tab-context, niet
herschreven.

## Implementatie (2026-09-11)

- **Route**: `<Route path="/dossiers/:id/:section?" element={<DossierDetailPage />} />` — één
  element; `useParams().section` kiest de subsectie, `isDossierTab` + beschikbaarheid (Route
  enkel met een HasStops-activiteit, Goederen enkel met SupportsGoods) valideren, onbekend →
  `<Navigate replace>` naar het overzicht.
- **Shell** `pages/DossierDetailPage.tsx`: dossier, doelopdracht (`loadedOrder`), selectie
  (`selectedActivityId` prijs, `routeSelectionId` route), `routeDirty`, dialogen, kopmenu. Levert
  `DossierWorkspaceContext` (`dossierWorkspace.ts`) aan de subsecties; editor-handles gaan als
  `ref`-props, veldfocus via `focusRouteField/focusPriceField/focusAddActivity` (leest de handle
  pas op het moment van de sprong — react-hooks/refs).
- **Subnav** `components/DossierSubnav.tsx`: `<nav aria-label="Dossieronderdelen">` met
  `NavLink`s (`aria-current="page"`), sticky, horizontaal scrollbaar.
- **Overzicht** `components/DossierOverview.tsx`: kaarten Route (Laden/Lossen/Planning),
  Activiteiten (max 3 + "+ n andere"), Verkoop & prijs (totaal, x/y, € 0 ⚠), Goederen,
  Documenten (`documentCount`/`documentTypes`), Notities & historiek (`lastChangedAt`). Alleen
  links; nul invoervelden/knoppen.
- **Subsecties** `components/sections/*`: Activiteiten (ActivityList + legacy), Route
  (ongewijzigde `DossierRouteEditor`), Goederen (`DossierGoodsSummary` + GoodsDrawer), Prijs
  (ongewijzigde `DossierPricePanel`), Documenten (`OrderDocumentsPanel` van de doelopdracht),
  Historiek (notities/omschrijving, `AuditHistoryPanel`, `DossierMoreSection` open).
- **Aandacht → veld**: `goToSection` selecteert de eenheid, zet `pendingJump`, navigeert naar
  de tab; een effect op `[activeTab, jumpToken, targetReady, …]` roept `registry.goTo` zodra de
  sectie geregistreerd én het doel klaar is. Werkt vanuit elke tab (ook naar de al open tab).
- **Kop**: statuschips Operationeel/Prijs verwijderd; `operationalStatus`/`priceChip` blijven
  als helpers bestaan (lijst/toekomst).
- **Read-model**: `DossierDetailDto.DocumentCount/DocumentTypes/LastChangedAt` (één extra query
  op `TransportOrderDocuments`, max-UpdatedAt uit reeds geladen rijen). Geen migratie.
- **Stopteksten** gedeeld via `routeStopText.ts` (samenvatting + overzicht).
