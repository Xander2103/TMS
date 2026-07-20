# Rendement / Profitability (`/profitability`)

Operationele en commerciële marge-analyse als READ MODELS over bestaande financiële data
(TripCosting-snapshots, afgesproken orderprijzen, factuurlijnen). Er is bewust géén tweede
boekhouding of loonadministratie; facturen worden nooit aangeraakt.

## Omzet — natures blijven gescheiden

Per rit: `AgreedRevenue` (Σ AgreedPrice van de orders), `InvoicedRevenue` (factuurlijnen
excl. btw van niet-geannuleerde facturen, per order), `PaidRevenue` (subset met status
Paid). `RevenueUsed` + `RevenueSourceUsed`: gefactureerd wint zodra er lijnen bestaan,
anders afgesproken, anders None — nooit één ambigu bedrag. Toeslagen/wachttijd/incident-
doorrekening lopen via factuurlijnen of handmatige kostlijnen en blijven dus zichtbaar.

## Kosten — zekerheid blijft gescheiden

Uit de bestaande `TripCostLine`-snapshots (fasen Estimated/Actual, bronnen Berekend/
Handmatig/Tankbeurten): `ActualCost`, `EstimatedCost` en `ProjectedCost` (de per-
kostensoort-merge uit `TripCostSummary`: werkelijk waar beschikbaar, anders raming;
afgeronde ritten gebruiken hun bevroren `FinalCost`). Kernkostensoorten zonder enige lijn
(Brandstof, Chauffeur, Voertuig-km, Tol) worden als expliciet ONTBREKEND gerapporteerd —
`MissingCostTypes` per rit plus een teller in de samenvatting. De brandstofprioriteit
(tankbeurten → verbruik → configuratie → afstand×prijs) en de chauffeurskost (uurloon ×
werkgeversmultiplicator, overuren, wachttijd) zitten in de bestaande
kostencalculatiestrategieën van TripCosting; dit scherm rekent niets zelf uit.

## Metrics en groeperingen

Marge = RevenueUsed − ProjectedCost; verder marge %, €/km (werkelijke afstand indien
geregistreerd, anders raming met *-markering), kost/stop, kost/collo. Groeperingen:
klant (kosten van gedeelde ritten worden gelijk verdeeld over de orders en als ALLOCATIE
gemarkeerd, nooit als boeking), chauffeur, voertuig en ISO-week. Periode begrensd op 366
dagen; alles server-side berekend.

## Uitleg en correcties

`GET /api/profitability/trips/{id}/explanation` toont elke euro: omzetlijnen (bron
Opdracht/Factuur) en kostlijnen (fase, bron, override-vlag + reden) plus de ontbrekende
kostensoorten en de rekennotitie. Handmatige correcties lopen bewust via de BESTAANDE
trip-costing-flow (`trip_costs.manage`/`trip_costs.override`): daar zijn reden, oude/
nieuwe waarde, actor en audit al afgedwongen. Export (XLSX, zelfde cijfers als het
scherm) vereist `profitability.export`.

## Permissies

`profitability.view` voor alle leesschermen; kostendetail-links alleen met
`trip_costs.view`; export met `profitability.export`. Het plannerprofiel krijgt standaard
géén kostenzichtbaarheid (bewuste keuze, zie permissiematrix).
