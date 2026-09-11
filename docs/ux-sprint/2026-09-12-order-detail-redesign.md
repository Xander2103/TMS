# Transportopdracht detail — visueel herontwerp (2026-09-12)

Referentie: `screenshots/TransportOpdracht.png`. Visuele/layout-pass; domeinlogica, API-contracten,
prijs-/stop-/colli-flows en dialogen ongewijzigd. Niet gecommit, niet gedeployed.

## Oud

Eén verticale pagina (`max-width: 1000px` per sectie): PageHeader met één lange knoppenrij
(Leveringsbon, CMR, Bewerken, Verwijderen, transities, Annuleren, Status corrigeren, Sjabloon),
commerciële balk, documentstrategie, prijsbanner, portaalreview, en daaronder ALLE secties
gestapeld: Lading, Prijs, Stops, Colli, Goederenlijnen, Historiek, Berichten.

## Nieuw

```
TransportOrderDetailPage (shell: state + handlers + dialogen, ongewijzigd)
├─ OrderDetailHeader   breadcrumb · terug · "0004 — Klant" · chips (nummer, status, prijsstatus)
│                      · "Opdracht van …" · klant/entiteit-balk · actiegroepen
│                        [Bewerken] [transities] [Annuleren] [Status corrigeren] | [Verwijderen]
│                        [CMR] [Leveringsbon] [Gebruik als sjabloon]
├─ OrderAttention      compacte alertstrip (prijs te bevestigen / onvolledig / verouderd,
│                      geannuleerd, tijdelijke klant, geen stops) met "Open …"-acties
├─ OrderSubnav         Overzicht · Lading · Prijs · Stops · Colli · Historiek · Berichten
└─ sectie (URL `/transport-orders/:id/:section?`)
   ├─ OrderOverview    kaarten: Route/Stops · Verkoop & prijs · Lading/Goederen · Colli ·
   │                   Foto's & documenten · Historiek · Berichten (klantportaal); portaalreview-
   │                   paneel bovenaan wanneer van toepassing
   ├─ OrderLadingSection   feiten + goederenlijnen + Foto's & documenten (documentenpaneel +
   │                       documentstrategie)
   ├─ OrderPriceSection    totaal/statusbanner + prijsacties + dekking + prijsregels (bestaand)
   ├─ OrderStopsSection    stoptabel (bestaand) in één paneel
   ├─ OrderColliSection    OrderPackagesPanel / CustomerPackagesSummary (bestaand)
   ├─ OrderHistorySection  OrderTimelinePanel (bestaand)
   └─ OrderMessagesSection CustomerMessagesPanel (bestaand) of nette lege staat
```

- Routing: één route-element met optioneel segment (zoals het dossier); refresh/deeplink/terug
  werken via de router; onbekend segment → Overzicht. Bewerken (`editing`) toont het volledige
  formulier in plaats van de tabs, zoals vandaag.
- Subsecties lezen `OrderDetailContext` (order, permissies, prijsberekeningen, handlers); de shell
  blijft eigenaar van state en dialogen.
- Overzicht is samenvatting-eerst: geen prijs-/stopbewerking; kleine leesacties (Open …). De
  kaarten halen enkel lichte lijsten op die de secties toch al gebruiken (documenten, colli,
  tijdlijn, berichten), permissiegebonden.
- Geen backend-wijziging.

## Implementatie (2026-09-12)

- Route `<Route path="/transport-orders/:id/:section?">` (oude `/:id`-route verwijderd); `useParams().section` →
  `isOrderTab` → `activeTab`; onbekend segment → `<Navigate replace>` naar het overzicht.
- Shell `pages/TransportOrderDetailPage.tsx` (1579 → ~1050 regels): alle state, handlers en dialogen
  ongewijzigd; render = `OrderDetailHeader` + commerciële dialogen + (bewerken ? `TransportOrderForm`
  : `OrderAttention` + `OrderSubnav` + actieve sectie) + dialogen. `OrderDetailContext`
  (`detail/orderDetailContext.ts`) levert order, permissies, prijsberekeningen en handlers aan de
  secties.
- Nieuw in `features/transport-orders/detail/`: `orderSections.ts`, `orderDetailContext.ts`,
  `OrderSubnav`, `OrderDetailHeader` (breadcrumb, terug, titel + chips, klant/entiteit-balk,
  actiegroepen: workflow-rij met Verwijderen apart, documentenrij), `OrderAttention`,
  `OrderOverview` (+ `useOverviewData`: documenten, colli, tijdlijn, berichten — permissiegebonden),
  `OrderLadingSection` (feiten, goederenlijnen, Foto's & documenten = OrderDocumentStrategyPanel +
  OrderDocumentsPanel), `OrderPriceSection` (totaal/status/actiegroep + dekking + prijsregels),
  `OrderStopsSection`, `OrderColliSection`, `OrderHistorySection`, `OrderMessagesSection`,
  `order-detail.css` (15px paginaschaal, 6-koloms grid: 3+3 / 3+3 / 2+2+2 zoals de referentie).
- Verwijderd: de dubbele Bewerken/Verwijderen-rij onderaan; de kop-KPI's zijn chips geworden.
- Overzicht = samenvatting-eerst (alleen-lezen kaarten + "Open …"-links); portaalreviewpaneel
  bovenaan wanneer van toepassing. Foto's & documenten-kaart toont aantal + eerste vier documenten
  (titel, type, bestand ja/nee) en opent de Lading-sectie met het documentenpaneel.
- Vertaalsleutels `transportOrders.detail.nav.*` (nl/en/fr); de overige tekst volgt de bestaande
  Nederlandse literals van de pagina.
