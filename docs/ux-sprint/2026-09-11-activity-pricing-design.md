# Stap 13 — Prijs voor zelfstandige dossieractiviteiten (Opslag / Kraan) + intentionele € 0-waarschuwing

Status: ONTWERP + IMPLEMENTATIE 2026-09-11, niet gecommit, niet gedeployed.
Vervangt de productbeslissing "bewust uitgesteld" in `2026-09-10-hardening.md` §1.

## 1. De beperking tot nu toe

Een dossier kan zelfstandige activiteiten bevatten (Opslag, Kraanwerk, Plateau, …) zonder
transportopdracht. De ENIGE prijsdrager in het domein was `TransportOrder`
(`AgreedPrice`, `PriceIsManual`, `PricingSource`, `OneOffFixedAmount`, snapshot + regels), en elke
commerciële leespad — dossierlijst, dossierfinancials, per-opdracht `isPriced`, readiness
`pricing.*`, aandachtsteller, activiteit-KPI — bereikte prijzen uitsluitend via
`DossierOrders`/`DossierActivity.LinkedTransportOrderId → TransportOrders`. `DossierActivity` had
geen geld, geen versie en geen status; `ActivityType` had geen factureerbaarheidsvlag. Een dossier
met enkel Opslag toonde "Nog geen prijs — prijzen worden bijgehouden op een transportopdracht"
(+ eerder "Transportopdracht aanmaken"). Een opdracht verzinnen om ergens een prijs aan te hangen
is geen aanvaardbaar productgedrag.

Inventaris (drie parallelle audits, 2026-09-11): activiteitendomein, prijsmodel, downstream.
Kernfeiten:

- `DossierActivity` (`Modules/Dossiers/Entities/DossierActivity.cs`): dun uitvoeringsrecord;
  documentatie-invariant "een veld dat op TransportOrder bestaat komt hier niet".
- `OrderPricingState.IsPricedExpression` = provenance (`PriceIsManual || (OneOff && FixedAmount != null)
  || AgreedPrice > 0`); € 0 is een geldige prijs met provenance.
- `SetOneOffPriceAsync` + `POST /api/transport-orders/{id}/pricing/one-off`: versie-gate op de
  order, Locked/Invoiced geweigerd, audit `OrderPricing/oneOffPriceSet`.
- Downstream: facturatie (`InvoiceService.CreateAsync`, `InvoiceControlService`) start UITSLUITEND
  vanuit `TransportOrders`; `InvoiceLine.TransportOrderId` is nullable (vrije regel) en dat is
  de enige order-loze geldrij; Peppol/UBL, pdf en dashboard lezen enkel factuurregels
  (order-agnostisch); `ActivityKpiService` telt omzet enkel via gekoppelde opdrachten (zou stil
  € 0 rapporteren); winstgevendheid is rit-verankerd; boekhoudexport eist een verkoopcategorie
  per factuurregel.

## 2. Gekozen model: activiteit-eigen commercieel record `DossierActivityPricing`

**Beslissing.** Elke factureerbare zelfstandige activiteit (activiteitstype `IsBillable` en
`!HasStops`) krijgt een eigen 1:1 commercieel aggregaat `DossierActivityPricing`. Transport-
activiteiten blijven via hun `TransportOrder` geprijsd — daar verandert niets. Het dossier
rekent over "factureerbare eenheden": per factureerbare activiteit één eenheid, waarvan de prijs
óf van de gekoppelde opdracht komt (HasStops) óf van het activiteitsrecord (zelfstandig).

Waarom dit en niet:

| alternatief | verworpen omdat |
|---|---|
| kolommen op `DossierActivity` | schendt de gedocumenteerde invariant (geld hoort niet op het dunne uitvoeringsrecord), geen eigen versie/status, elke activiteitmutatie zou de prijsversie invalideren |
| `TransportDossier.Price` | één getal is dubbelzinnig zodra een dossier meerdere geprijsde activiteiten heeft |
| polymorfe "PricingOwner" onder `TransportOrderPricingLine`/snapshot | herschrijft de werkende transport-prijsstack (engine, coverage, snapshotstatus, servicelijnen) voor een gebruikscase die enkel een afgesproken bedrag nodig heeft |
| aparte `StoragePricing`/`CranePricing` | de regels zijn niet verschillend; één record met een provenance-veld dekt elk zelfstandig type, ook toekomstige |

Het record is bewust een klein spiegelbeeld van de "eenmalige prijsafspraak" op de order:
dezelfde provenance-semantiek, dezelfde € 0-betekenis, dezelfde vergrendelingsstatussen, eigen
concurrency-token. Verkooplijnen: zie §7.

### 2.1 Entiteit / tabel

`Modules/Dossiers/Entities/DossierActivityPricing.cs` → tabel `dossier_activity_pricings`

| kolom | type | betekenis |
|---|---|---|
| `Id`, `TenantId`, audit-/soft-delete-velden | `AuditableTenantEntity` | tenant-isolatie via globale queryfilter + expliciete `TenantId`-check in de service |
| `DossierActivityId` | uuid, FK → `dossier_activities`, **Cascade** | eigenaar; het record leeft en sterft met de activiteit |
| `PricingSource` | string(20) enum `ActivityPricingSource { None, OneOff }` | provenance; `None` = geen prijs, `OneOff` = expliciete afspraak |
| `FixedAmount` | numeric(12,2) nullable | het afgesproken bedrag; `0` is een bewuste prijs |
| `AgreedPrice` | numeric(12,2) nullable | effectieve verkoopprijs (= `FixedAmount` zolang er geen lijnen zijn; blijft de plek waar een lijnentotaal later landt) |
| `Status` | string(20) `OrderPricingStatus` (Draft/Reviewed/Locked/Invoiced) | hergebruikte statusvocabulaire; `Invoiced` wordt enkel door de toekomstige facturatiegolf gezet |
| `Notes` | string(1000) nullable | |
| `Version` | uuid (`IVersionedEntity`) | optimistische concurrency, eigen token zoals de order |

Indexen/constraints: unieke gefilterde index `UX_dossier_activity_pricings_activity`
(`DossierActivityId` WHERE `IsDeleted = false`) — één actief prijsrecord per activiteit én de
race-guard voor twee gelijktijdige eerste opslagen; index `(TenantId, DossierActivityId)`.

`ActivityType.IsBillable` (bool, default **true**, migratie zet bestaande types op true): de
domeinvlag "deze activiteit is een commerciële eenheid". Niet-factureerbare types tellen niet mee
in volledigheid en krijgen geen prijsaandachtspunt. Zichtbaar/bewerkbaar in Instellingen ›
Activiteitstypes (badge "Factureerbaar" + vinkje), DTO `ActivityTypeDto.IsBillable`.

Migratie: `StandaloneActivityPricing` (additief: nieuwe tabel + kolom). Bestaande data: geen
records nodig — een activiteit zonder record is "geen prijs" (unpriced). Verwijderen van een
activiteit cascadeert het record; verwijderen van een dossier cascadeert via de activiteit.

### 2.2 Provenance-semantiek (ongewijzigd principe, nu voor beide dragers)

`ActivityPricingState.IsPricedExpression = p => p.PricingSource == OneOff && p.FixedAmount != null`
(EF-vertaalbaar + gecompileerd, zoals `OrderPricingState`). Geen engine → geen "positief bedrag
zonder provenance"-pad.

| toestand | opdracht | zelfstandige activiteit | telt als | waarschuwing |
|---|---|---|---|---|
| geen prijs | `OrderPricingState` false | geen record of `PricingSource = None` | niet geprijsd | `pricing.missing` |
| intentioneel € 0 | geprijsd én `AgreedPrice == 0` | `FixedAmount == 0` | geprijsd | `pricing.zero` (Warning, niet blokkerend) |
| positief | geprijsd | `FixedAmount > 0` | geprijsd | — |

Nooit meer "bedrag > 0 ⇒ geprijsd" als enige regel.

### 2.3 API-contract

`PUT /api/dossiers/{id}/activities/{activityId}/price`
body `{ "fixedAmount": decimal | null, "version": uuid | null }` → `200 DossierDetailDto`
(zelfde payload als elke andere dossiermutatie; het paneel herlaadt het dossier).

Regels (service `DossierActivityPricingService.SetAgreedPriceAsync`):
- dossier bestaat in de tenant en is Open (anders 400 "gesloten dossier");
- activiteit bestaat in dat dossier; type `IsBillable` (anders 400 "niet factureerbaar");
  type `!HasStops` (anders 400 "Transportactiviteiten worden via hun transportopdracht geprijsd.");
- `fixedAmount` null → `PricingSource = None`, `FixedAmount = AgreedPrice = null` (prijs gewist);
  anders ≥ 0 (anders 400), `PricingSource = OneOff`, `FixedAmount = AgreedPrice = bedrag`;
- `version`: null wordt aanvaard bij het EERSTE record (nog geen token) en door legacy-callers;
  bestaat een record en wijkt `version` af → 409 met de huidige `DossierDetailDto`
  (`DossierVersionConflictException`, dezelfde banner/Herladen-flow als de rest van de pagina);
  twee gelijktijdige eerste opslagen: de unieke index laat er één door, de andere krijgt 409;
- `Status` Locked/Invoiced → 400 "De prijs van deze activiteit is vergrendeld.";
- `Version` wordt gebumpt; dossier-`Version` NIET (een prijswijziging is geen structurele
  dossiermutatie — anders zou elke prijsopslag een open activiteitendialoog van een collega
  409'en); audit `DossierActivityPricing/{activityId}` actie `agreedPriceSet` met oud → nieuw
  `{ PricingSource, FixedAmount, AgreedPrice }`.

Permissie: **nieuw** `dossiers.price` — "Verkoopprijs van dossieractiviteiten beheren".
Motivatie: `orders.edit` is de bewerkrecht op transportopdrachten (waar de eenmalige afspraak
onderdeel van is), `orders.override_price` is het overschrijven van een berekende prijs,
`dossiers.manage` is de dossierstructuur. Een activiteitprijs is een commerciële handeling op een
niet-opdracht; ze stil onder `orders.edit` hangen zou prijsrechten geven aan wie opdrachten mag
bewerken en omgekeerd niemand toelaten enkel Opslag te prijzen. Rolsjablonen: `dossiers.price`
wordt toegekend aan elk sjabloon dat vandaag `orders.edit` heeft (upgradestap **32**,
`DefaultRoleUpgrades.CurrentVersion = 32`). Server-side afgedwongen via
`[RequirePermission(PermissionCodes.DossiersPrice)]`; de reflectietest van de werkvlakmatrix
neemt het endpoint op.

### 2.4 Dossier-DTO's (additief)

`DossierActivityDto` krijgt:
- `IsBillable` (bool),
- `PricingSource` ("None" | "OneOff" | "Order"): "Order" voor HasStops-activiteiten mét
  opdracht (de order is de drager), "None"/"OneOff" voor zelfstandige,
- `AgreedPrice` (decimal?): orderprijs resp. activiteitsprijs,
- `IsPriced` (bool): `OrderPricingState` resp. `ActivityPricingState`,
- `PricingStatus` (string?): snapshotstatus van de order resp. status van het record,
- `PricingVersion` (uuid?): concurrency-token van het activiteitsrecord (null zonder record of
  voor opdrachten — die gebruiken hun eigen orderversie).

`DossierFinancialSummaryDto` krijgt `BillableActivityCount`, `PricedActivityCount`,
`ZeroPricedActivityCount`; `AgreedOrderTotal` blijft de naam (compat) maar is het
**dossiertotaal over alle geprijsde factureerbare eenheden** (opdrachten + activiteiten).
`PricedOrderCount` blijft (compat) = aantal geprijsde opdrachten.

`DossierListItemDto` krijgt `BillableActivityCount`, `PricedActivityCount`,
`ZeroPricedActivityCount`; `AgreedPriceTotal` = som over alle geprijsde eenheden (null zodra
niets geprijsd is). Berekend in DEZELFDE enkele EF-projectie als vandaag (gecorreleerde
subquery's, geen N+1).

Factureerbare eenheden per dossier (één definitie, `DossierBillables`):
1. elke activiteit van een `IsBillable`-type: HasStops → geprijsd/bedrag via
   `LinkedTransportOrderId → TransportOrders` (zonder opdracht: niet geprijsd, bedrag null);
   zelfstandig → via `DossierActivityPricings`;
2. compat: gekoppelde opdrachten (`DossierOrders`) die door GEEN activiteit vertegenwoordigd
   worden (bestaat na `LinkOrder` op een oud dossier) tellen als eenheid via `OrderPricingState`.

### 2.5 Readiness / aandacht

- `pricing.missing` (Warning, sectie `prijs`, veld `price`, stage Commercial): bestaand voor
  opdrachten (`TransportOrderId` + nu ook `ActivityId` van de activiteit die de opdracht draagt);
  NIEUW voor een zelfstandige factureerbare activiteit zonder prijs: "Nog geen verkoopprijs voor
  {Typenaam}{ · label}." met `ActivityId`.
- `pricing.zero` (Warning, niet blokkerend, sectie `prijs`, veld `price`): voor elke geprijsde
  eenheid met bedrag exact 0: "Opdracht {nummer} heeft een verkoopprijs van € 0,00. Controleer of
  dit bewust is." (`TransportOrderId` + `ActivityId`) resp. "{Typenaam} heeft een verkoopprijs van
  € 0,00. Controleer of dit bewust is." (`ActivityId`). Eén item per eenheid. Niet voor een
  niet-geprijsde opdracht met engine-nul (provenance-regel). Opdrachten in status Cancelled of
  Invoiced krijgen geen zero-waarschuwing meer (afgehandeld).
- `pricing.none` (Info) vervalt in de praktijk: zodra er factureerbare eenheden zijn, produceert
  elke ongeprijsde eenheid zijn eigen `pricing.missing`; een dossier zonder factureerbare eenheden
  krijgt geen prijsitem.
- Aandachtsteller (`CountDossiersWithAttentionAsync`, één SQL-statement): + open dossier met een
  zelfstandige factureerbare activiteit zonder prijs; + een intentionele € 0 (opdracht in
  commerciële fase of activiteit).

### 2.6 Frontend (Verkoop & prijs, lijst, aandacht)

- Verkoop & prijs werkt op factureerbare activiteiten. Eén eenheid → geen wisselaar. Meerdere
  → dezelfde `DossierOrderSwitcher` (label = opdrachtnummer voor transport, typenaam voor
  zelfstandig; sublabel = label/typenaam). Route-wisselaar blijft transport-only; één gedeelde
  `selectedActivityId` — een transportactiviteit selecteren wisselt beide secties, een
  zelfstandige enkel het prijsdoel. Scrollgedrag: ongewijzigd (`RetainedHeight`), een wissel
  naar een zelfstandige activiteit laadt niets en klapt niets in.
- Zelfstandige eenheid: nieuw `DossierActivityPricePanel` — "Afgesproken prijs (€)" +
  Opslaan (`PUT …/price`, `pricingVersion`), 409 → bestaande dossierconflict-banner, huidige
  prijs "Huidige prijsafspraak: € 350,00", wissen door leeg te laten, vergrendeld → alleen-lezen
  met melding. Recht: `dossiers.price`.
- Totaalkop: `agreedOrderTotal` als ≥ 1 eenheid geprijsd (ook € 0,00), anders "Nog geen prijs";
  deelmarkering "{priced} van {total} activiteiten geprijsd" (`pricedActivityCount`/
  `billableActivityCount`); per-eenheidlijst bij > 1 eenheid: "0004 · Direct transport € 450,00",
  "Opslag € 200,00", "Kraanwerk —".
- Intentionele € 0 lokaal: bij de geselecteerde eenheid, geprijsd én bedrag 0 → amber, niet
  blokkerend: "⚠ Deze opdracht heeft een verkoopprijs van € 0,00. Controleer of dit bewust is."
  / "⚠ Deze activiteit heeft …". Verschijnt/verdwijnt met de herlaadde dossier-/orderdata.
- Dossierlijst, kolom Prijs: bedrag (som geprijsde eenheden) + `⚠` met title "Verkoopprijs is
  € 0,00. Controleer of dit bewust is." zodra `zeroPricedActivityCount > 0` (meervoud: "… voor
  {n} activiteiten."); deelmarkering "{priced}/{total}" op activiteiten. Status-kolom ongewijzigd.
- Aandacht: `goToSection('prijs', 'price', issue)` selecteert `issue.activityId` (ook
  zelfstandig) en focust het prijsveld; voor opdrachten ongewijzigd.
- "Transportopdracht aanmaken" blijft ENKEL bestaan voor een transportactiviteit zonder opdracht
  (dat is dan geen kunstgreep maar de echte uitvoering), nooit meer als prijsdrager-suggestie.

### 2.7 Downstream — expliciet

| consument | nu | later |
|---|---|---|
| Facturatie (`InvoiceService`, invoice control) | **NIET aangesloten** — bewust. Een activiteitsprijs kan vandaag enkel als handmatige factuurregel gefactureerd worden. Het record heeft daarom `Status` (Invoiced/Locked) en een audittrail zodat de facturatiegolf enkel hoeft toe te voegen: `InvoiceLine.DossierActivityId` (nullable FK), opname in `ListUninvoiced…`/control per klant, status → Invoiced + release-paden, verkoopcategorie per activiteitstype (boekhoudexport eist die). | facturatiegolf |
| Dossierfinancials `InvoicedTotal` | blijft orderregels (geen join-pad voor vrije regels) | zelfde golf |
| `ActivityKpiService` omzet per activiteitsfamilie | **WEL aangesloten**: omzet = orderprijs (bestaand) + `AgreedPrice` van zelfstandige activiteitsrecords; anders rapporteert dit rapport stil € 0 voor kraan/opslag | — |
| Winstgevendheid (rit-verankerd), diesel-basis, incidentkosten-doorrekening | ongewijzigd; buiten scope (geen rit, geen opdracht) | apart |
| Peppol/UBL, pdf, dashboard | order-agnostisch; werken zodra een factuurregel bestaat | — |

### 2.8 Backwards compatibility transportprijs

Contract-/OneOff-prijs, regels, overrides, € 0-semantiek, prijspermissies, Locked/Invoiced,
`POST /pricing/one-off`, multi-opdracht totalen en aandachtnavigatie: geen enkele regel in
`TransportOrderService`/`OrderPricingState`/`PricingEngine` wordt aangeraakt. De enige
orderzijdige toevoeging is de leesregel `pricing.zero`.

## 3. Tests

Backend (`Dossiers/DossierActivityPricingTests.cs` + uitbreidingen `DossierReadinessTests`,
`DossierServiceTests`, `DossierWorkSurfacePermissionTests`, `ActivityKpi`):
Opslag-only en Kraan-only prijs zonder opdracht (persist + herlezen), € 0 = geprijsd +
`pricing.zero`, geen record = niet geprijsd + `pricing.missing` met `ActivityId`, gemengd totaal,
volledigheid 2/3 → 3/3, transportactiviteit geweigerd, niet-factureerbaar type geweigerd,
autorisatie (endpoint-attribuut), stale versie → 409 zonder wijziging, andere tenant → niet
gevonden, Locked/Invoiced geweigerd, lijstprojectie-pariteit, OneOff € 0 en override € 0 op een
opdracht → `pricing.zero`, engine-nul zonder provenance → geen zero-waarschuwing, KPI-omzet.

Frontend (`dossierWorkSurface.test.tsx`, `dossiersPage.test.tsx`, `dossierDetailPage.test.tsx`):
storage-only en crane-only tonen de prijseditor (nooit "Transportopdracht aanmaken"), opslaan +
herlezen, meerdere eenheden onafhankelijk selecteerbaar zonder elkaars prijs te raken, deelstatus,
€ 0 zichtbaar als € 0,00 + lokale waarschuwing, dossiertotaal, aandacht → juiste activiteit,
lijst € 0,00 ⚠ en 2/2 bij € 100 + € 0, zero-waarschuwing verschijnt/verdwijnt bij wijziging.

## 4. Bewust uitgesteld

- Verkooplijnen voor zelfstandige activiteiten (§2.1: `AgreedPrice` is de landingsplaats van een
  toekomstig lijnentotaal; het lijnenmodel wordt dan een `ActivityPricingLine` onder dit
  aggregaat óf een generalisatie van `TransportOrderPricingLine` — beslissing bij die golf, niet
  nu "blind koppelen aan een activiteits-id").
- Facturatie van activiteitsprijzen (§2.7).
- Vergrendel-/ontgrendel-UI voor activiteitsprijzen (status bestaat, wordt door facturatie gezet).

## 5. Implementatienotities (2026-09-11, na de bouw)

- **Effectieve prijs van een opdracht** (`OrderPricingState.EffectiveAgreedPrice`): `AgreedPrice`
  zodra de engine die afleidde, anders — voor een eenmalige afspraak waarvoor de engine (nog)
  niet liep (geen goederenregels/tariefconfiguratie) — het afgesproken bedrag zelf. Dossierlijst,
  financials, activiteits-DTO en activiteit-KPI gebruiken die definitie; de pariteitstest
  verwacht daarom 620 i.p.v. 500 voor "500 + 0 + 0 + eenmalig 120 (nog niet afgeleid)".
- **Intentionele € 0** (`OrderPricingState.IsIntentionalZero`): de provenance zelf is nul —
  eenmalige afspraak op 0 óf handmatige override op 0. Nooit de engine-nul (die is ongeprijsd),
  nooit een nog niet afgeleide eenmalige afspraak. Zelfde boom inline in SQL (lijst, teller).
- **Aandachtsteller** telt een intentionele € 0 (opdracht in commerciële fase of activiteit) mee:
  het is een Warning en verdient de tegel. Bestaande asserts "override op 0 → teller 0" zijn
  bewust naar 1 gezet.
- `pricing.none` (Info) blijft bestaan voor een transportactiviteit zonder opdracht (bestaande
  test); een dossier met enkel zelfstandige activiteiten krijgt géén `pricing.none` meer maar per
  eenheid `pricing.missing`/`pricing.zero`.
- Seeder: `POSITIONERING` (lege rit) is standaard niet factureerbaar; alle andere types wel.
  Bestaande tenants: migratiedefault TRUE (`HasDefaultValue(true)`), de seeder wijzigt bestaande
  rijen niet.
- Frontend: naast `selectedActivityId` (prijsdoel) houdt de pagina `routeSelectionId` voor de
  route, zodat een zelfstandige eenheid kiezen in Verkoop & prijs de route (en de orderfetch) niet
  raakt; een transporteenheid kiezen verplaatst beide. Wisselaarlabel in de prijssectie: "Eenheid".
- Migratie `20260911182737_StandaloneActivityPricing` is op de LOKALE Docker-database toegepast
  voor de browser-smoke; productie: niet.
