# Prijsmodule (tarification + orderprijzen)

Dit document beschrijft het volledige prijsmodel van de TMS: hoe tarieven worden opgebouwd en
beheerd (Prijzen-gebied), hoe een order zijn prijs krijgt (de rekenmotor) en hoe die prijs op de
order zelf verder leeft (regelbewerking, statuslevenscyclus, facturatie). Alles is
backend-afgedwongen; de React-schermen bevatten geen gezaghebbende rekenlogica — ze tonen enkel
wat de API teruggeeft. Elke bewering hieronder is nagekeken tegen de huidige implementatie
(bestanden en regelnummers zoals hieronder vermeld); dit is geen ontwerpdocument maar een
naslagwerk voor wie met deze code moet werken.

## 1. Overzicht & modulekaart

### Backend — `Modules/Tarification` (tariefconfiguratie)

- `Entities/PricingAgreement.cs` — `PricingAgreement` (tariefkaart) + `PricingAgreementSurcharge`
  (automatische toeslag) + `PricingAgreementAssignment` (klantkoppeling van een gedeelde tabel) +
  `PricingAgreementModifier` (stapeltoeslag van een afgeleide tabel).
- `Entities/PriceRule.cs` — `PriceRule` + `PriceRuleBracket` + de enums `PriceRuleBasis` en
  `BracketSelectionMode`.
- `Entities/PricingZone.cs` — `PricingZone` + `PricingZoneArea` (land + postcodereeks).
- `Entities/ServiceOption.cs` — `ServiceOption` (globale dienst/toeslag), `CustomerServiceOptionPrice`
  (klantoverride) en `CustomerPreferredUnit` (klant-eenheidconfiguratie: EDI/Excel-code, favoriet).
- `Entities/CombinedUnitDiscount.cs` — `CombinedUnitDiscount` + `CombinedUnitDiscountUnit` +
  `CombinedUnitDiscountTier` (combinatiekorting over meerdere eenheidtypes).
- `Entities/ScheduledPriceAdjustment.cs` — `ScheduledPriceAdjustment` + `ScheduledPriceAdjustmentRule`
  (geplande bulkaanpassing die toekomstige regelversies materialiseert).
- `Services/PricingAdminService.cs` — alle CRUD voor het bovenstaande, plus duplicatie
  ("nieuwe versie"), klantconfiguratie (`GetCustomerConfigAsync`/`SaveCustomerConfigAsync`) en de
  "Controle"-validatie (`ValidateAgreementConfigurationAsync`, zie §4 hieronder).
- `Services/PricingEngine.cs` — `CalculateAsync`: de ene rekenmotor, gebruikt door zowel de
  preview-endpoint als de order-opslag.
- `Services/PriceAdjustmentService.cs` — geplande prijsaanpassingen (klant- of tabelgebonden).
- `Services/PriceAdjustmentMath.cs` / `Services/CombinedUnitDiscountMath.cs` — pure, DB-vrije
  rekenkernen (percentage/vast bedrag + afronding; groepering/equivalent/apportionering).
- `Services/PricingExcelService.cs` — Excel-export/import van één tarieventabel se regels.
- `Controllers/PricingController.cs` / `Controllers/PricingImportController.cs` — de HTTP-laag.
- `Entities/RateCard.cs` + `Services/RateCardConversionService.cs` — legacy invoer, ooit
  geconverteerd naar `PricingAgreement`+`PriceRule` (`LegacyRateCardId`); niet meer actief bewerkt.

### Order-zijde — `Modules/Orders`

- `Entities/TransportOrderPricing.cs` — `TransportOrderPricingLine` (één regel van de bevroren
  prijsopbouw) + `TransportOrderPricingSnapshot` (header) + `TransportOrderServiceLine`
  (geselecteerde dienst) + de enums `OrderPriceLineKind` en `OrderPricingStatus`.
- `Entities/TransportOrder.cs` — `OrderPricingSource` (Contract/OneOff) + de `OneOff*`-velden.
- `Services/TransportOrderService.cs` — `ApplyPricingAsync` (roept de engine aan, merget de
  uitkomst in de bestaande regels), `SaveOrderPriceLinesAsync` (handmatige regelbewerking),
  `RecalculateOrderPricingAsync`, `SetOrderPricingStatusAsync`, `ConfirmOrderPriceLineAsync`.

### Frontend — Prijzen-gebied

- `features/tarification/pages/PricingTablesPage.tsx` — overzicht van alle tarieventabellen.
- `features/tarification/pages/PricingTableDetailPage.tsx` — tabbladen **Regels**
  (`RuleGridEditor.tsx`), **Klanten** (`AgreementAssignmentsPanel.tsx`), **Afleiding**
  (`AgreementDerivationPanel.tsx`), **Toeslagen** (`AgreementSurchargesPanel.tsx`), **Kortingen**
  (`CombinedDiscountsPanel.tsx`), **Prijsaanpassing** (`AgreementAdjustmentsPanel.tsx`), **Versies**
  (`AgreementVersionsPanel.tsx`) + de altijd-zichtbare **Controle**-sectie
  (`AgreementValidationPanel.tsx`, zie §4) + export/import-acties (`PricingImportDialog.tsx`).
- `features/tarification/pages/PricingSettingsPage.tsx` — zones, diensten (`ServiceOptionsEditor.tsx`),
  eenheden (`UnitTypeMasterEditor.tsx`).
- `features/customers/components/CustomerUnitPricingPanel.tsx` — klanttab "Tarieven": eigen
  regels, gekoppelde gedeelde tabellen, dienstoverrides, eenheidvoorkeuren, prijsaanpassingen.
- Order-zijde: `features/transport-orders/pages/TransportOrderDetailPage.tsx` +
  `components/TransportOrderForm.tsx` — het "Prijs"-blok (regels, status, herberekenen, bevestigen).

## 2. Entiteitsmodel

### PricingAgreement (tariefkaart)

Een `PricingAgreement` is de commerciële identiteit van een set regels: naam, geldigheidsvenster,
valuta, optioneel minimum/maximum op de subtotaal, notities. Drie samenstellingen, bepaald door
`CustomerId`/`IsShared`/`BaseAgreementId`:

| Samenstelling | `CustomerId` | `IsShared` | Betekenis |
|---|---|---|---|
| Privé (klantspecifiek) | gezet | — | Prijst automatisch voor precies die klant. |
| Bedrijfsbreed (company-default) | `null` | `false` | Prijst automatisch voor iedereen zonder specifiekere match. |
| Gedeeld (herbruikbare tabel) | `null` | `true` | Prijst NOOIT uit zichzelf — enkel via een actieve `PricingAgreementAssignment` voor die klant op de tariefdatum. |

Een gedeelde tabel kan per klant een `PricingAgreementAssignment` krijgen met een eigen
`PercentAdjustment` (bv. -5%) en/of `FixedAdjustment` (vast bedrag), plus een eigen
geldigheidsvenster los van de tabel zelf.

Een agreement kan **afgeleid** zijn (`BaseAgreementId` gezet, spec §9, "NL = BE +30%"): een
afgeleide tabel heeft **geen eigen regels** — ze hergebruikt de regels van haar basis-keten-wortel
(`BaseAgreementId` gevolgd tot het einde, max 3 hops, cykeldetectie) en stapelt haar eigen
`PricingAgreementModifier`-rijen (percentage of vast bedrag, optioneel land-/zonevoorwaarde,
oplopende `Sequence`) op de lopende subtotaal.

### PriceRule + brackets

Eén `PriceRule` = één prijsregel: optioneel klantgebonden (`CustomerId`), optioneel eenheidgebonden
(`UnitTypeId`), optioneel zonegebonden (`ZoneId`), een `Basis` (zie tabel), een geldigheidsvenster,
`Priority` (expliciete tie-breaker, -1000..1000) en optioneel gekoppeld aan een `AgreementId`.

**Basistabel — wat elke `PriceRuleBasis` als invoer nodig heeft:**

| Basis | Invoer | Berekening |
|---|---|---|
| `PerUnit` | `UnitPrice` × lijnhoeveelheid | `UnitPrice × billableQuantity` |
| `QuantityBracket` | staffels op hoeveelheid | staffel bevattend de hoeveelheid (of `PerNextUnit`, zie onder) |
| `WeightBracket` | staffels op `request.WeightKg` | staffel bevattend het ordergewicht |
| `Hourly` | `UnitPrice`, optioneel `MinimumQuantity`/`QuantityRoundingStep` | rondt eerst op naar `QuantityRoundingStep` (per begonnen interval), dan minimum, dan × `UnitPrice` |
| `Fixed` | `BaseAmount` + `UnitPrice` | vast bedrag per order, ongeacht hoeveelheid |
| `PerKm` | `request.DistanceKm` | `BaseAmount + UnitPrice × km` |
| `PerPallet` | `request.PalletCount` | `BaseAmount + UnitPrice × pallets` |
| `PerTon` | `request.WeightKg` | `BaseAmount + UnitPrice × (kg / 1000)` |
| `PerLoadingMeter` | `request.LoadingMeters` | `BaseAmount + UnitPrice × ldm` |
| `PerVolume` | `request.VolumeM3` | `BaseAmount + UnitPrice × m³` |
| `PerStop` | `request.StopCount` | lineair (`UnitPrice × stops`) of progressief via staffels |

Ontbrekende invoer (bv. geen `DistanceKm` bij `PerKm`) produceert nooit een stille €0-regel: de
regel wordt overgeslagen met een informatieve regel ("overgeslagen (geen afstand gekend)").

**Dimensiegrenzen (`PriceRuleBracket`):** `WeightToKg`/`VolumeToM3`/`LoadingMetersTo` zijn optionele
caps per staffelrij (carrier-stijl "kg tot / m³ tot / ldm tot / prijs"). Een gevulde cap matcht enkel
als de order dat kenmerk kent ÉN binnen de cap valt; tussen matchende rijen wint de strengste
(hoogste `FromQuantity`, dan kleinste gevulde caps). Zie `PricingEngine.FindMatchingBracket`.

**`BracketMode`:** `Absolute` (bestaande gedrag: de ene staffel die de hoeveelheid bevat) of
`PerNextUnit` (enkel bij `QuantityBracket`): progressieve per-stuk-prijs — som van de staffelprijs
per eenheidindex 1..hoeveelheid (bv. "1e stuk €60, 2e €55, 3e €50, 4e en verder €45"); staffels
moeten aaneensluitend zijn vanaf 1 (`FromQuantity == vorige ToQuantity + 1`).

**Klantafwijkingen per staffelrij (`PriceRuleBracketOverride`):** één klantspecifieke prijs voor
ÉÉN rij van een gedeelde/bedrijfsbrede staffelregel, zonder de hele tabel te kopiëren. Voorbeeld:
gedeelde tabel 1/2/3/4+ pallets = €50/€80/€105/€125, klant X wijkt enkel af op rij 3 = €99 → 1, 2
en 4+ blijven de gedeelde prijzen volgen. Werking:

- De afwijking richt zich op een rij via haar **waarde-identiteit** (`FromQuantity`, `ToQuantity`
  en de dimensiecaps), niet via het rij-id — regelbewerkingen en Excel-import vervangen rijen
  integraal, dus id's overleven niet. Een afwijking waarvan de rij niet meer bestaat, is
  "verweesd": ze wordt niet meer toegepast en het raster toont een waarschuwing.
- De engine lost eerst de winnende regel + rij op zoals altijd (specificiteit, zone, datum); pas
  daarna, en enkel wanneer die regel NIET klant-privé is, vervangt een op de orderdatum geldige
  afwijking van de orderklant de prijs van precies die rij. `PricePerExtraUnit` wordt enkel
  vervangen als de afwijking er zelf één opgeeft.
- Twee afwijkingen die op dezelfde datum dezelfde rij claimen zijn een **blokkerende
  configuratiefout** ("Conflicterende klantafwijkingen…"), nooit een stille keuze; opslaan van
  overlappende vensters wordt bovendien al door de validatie geweigerd.
- Beheer: in het regelraster per staffelrij de actie **"Klantafwijking…"** (badge *Klantafwijking*
  onder de rij, verwijderen herstelt de geërfde prijs); API `GET/POST
  api/pricing/rules/{ruleId}/bracket-overrides`, `PUT/DELETE api/pricing/bracket-overrides/{id}`.
- Ordersnapshots blijven onaangeroerd: de afwijking beïnvloedt enkel nieuwe berekeningen; de
  breakdownregel krijgt bron "… — klantafwijking". Tests: `BracketOverrideTests`.

**Buitenmaat-billable factor:** `OversizeLengthCm`/`OversizeWidthCm`/`OversizeBillableFactor` —
spec ch. 11: een stuk boven de drempel telt als `OversizeBillableFactor` factureerbare eenheden; de
fysieke order verandert nooit (zie `PricingEngine.BillableQuantity`).

**Uurtarief-minimum + afronding (echt voorbeeld, `PricingEngineV2Tests`):** €72/uur, minimum 3 uur,
afronding per 0,25 (kwartier): 2u10 → rondt op naar 2,25u → onder het minimum → 3 × €72 = €216;
3u40 → rondt op naar 3,75u → 3,75 × €72 = €270.

Beide velden zijn rechtstreeks in het regelraster (`RuleGridEditor`) bewerkbaar via de kolommen
**Min. aantal** en **Afrondingsstap** — dezelfde kolomnamen als in de Excel-rondgang (§9). De
kolommen tonen enkel een invoerveld voor regels met basis *Per uur*; bij andere bases staat er "—".

### PricingZone

`PricingZone` + `PricingZoneArea` (land + postcode-van/tot). Resolutie
(`PricingEngine.ResolveZoneAsync`): numerieke postcodevergelijking als beide grenzen en de code
parsen als getal, anders ordinale stringvergelijking (bv. NL "1234 AB"). Land default "BE" als niet
opgegeven.

### ServiceOption + CustomerServiceOptionPrice (diensten/toeslagen)

**Kinds-tabel — wat elke `SurchargeKind` nodig heeft en hoe de hoeveelheid wordt afgeleid:**

| Kind | Hoeveelheid | Bron als niet expliciet ingevoerd |
|---|---|---|
| `Fixed` | geen | — |
| `Percent` | geen | percentage van de subtotaal vóór diensten |
| `PerHour` / `PerStop` | verplicht ingevoerd | — (geen fallback; ontbrekend of ≤0 ⇒ informatief, geen bedrag) |
| `PerUnit` | eenheid (`ServiceOption.UnitTypeId`) | som van orderregels met dat eenheidtype |
| `PerOrderLine` | orderregels | `request.CargoLineCount` |
| `PerKg` | kg | `request.WeightKg` |
| `PerM3` | m³ | `request.VolumeM3` |
| `PerLdm` | ldm | `request.LoadingMeters` |
| `PerDay` / `PerPalletDay` | verplicht ingevoerd | — (geen fallback) |

Voor `PerOrderLine`/`PerKg`/`PerM3`/`PerLdm`/`PerHour`/`PerStop`/`PerDay`/`PerPalletDay` geldt: een
**expliciete 0** wordt exact zo behandeld als "onbekend" — nooit een stille €0-regel, altijd de
informatieve "geen ... gekend"-regel (ledger-fix, `PricingEngine.cs`, `FinalizeAsync`).

**Dag- en pallet-daghoeveelheden op de order (wave 2026-07-27 §2.3):** voor een `PerDay`-dienst
vraagt het orderformulier **"Aantal dagen"** (12 dagen × €0,25 = €3,00); voor `PerPalletDay`
**"Pallets"** en **"Dagen"**, waarbij het factureerbare aantal **pallet-dagen = pallets × dagen**
automatisch wordt afgeleid (4 × 12 = 48 → 48 × €0,20 = €9,60) maar handmatig corrigeerbaar blijft
(een expliciet ingevuld aantal wint altijd — `TransportOrderService.EffectiveServiceQuantity`). De
invoer (`PalletCount`, `DayCount`) wordt op `TransportOrderServiceLine` bewaard zodat herberekening
identiek reproduceert en de UI de invoer terugtoont; ontbrekende invoer blijft de informatieve
"geef het aantal (pallet-)dagen op"-regel. Tests: `OrderDayQuantityTests`.

**Auto-apply:** `ServiceOption.AutoApply` (of de klant-override `AutoApplyOverride`) voegt de dienst
automatisch toe zonder expliciete selectie — een contractdienst (bv. Picking, PAL UIT). Voorbeeld
(`WarehouseServiceTests`): Picking €1,25/colli auto-toegepast op 3 colli → €3,75; PAL UIT
€4,50/pallet op 5 pallets → €22,50; Administratie €1,50/orderregel (`PerOrderLine`) op 3 regels →
€4,50.

**ADR-voorwaarde:** `OnlyForAdr` — enkel auto-toegepast/geldig wanneer `request.AdrRequired == true`;
anders informatief ("alleen van toepassing bij ADR").

**Klantoverride (`CustomerServiceOptionPrice`):** eigen `Value`, `Disabled`, `MinimumAmount`,
`InvoiceDescription`, geldigheidsvenster, `AutoApplyOverride` — null-velden erven de globale
standaard; `Disabled` schakelt de dienst helemaal uit voor die klant (ook al is ze globaal auto).

### ScheduledPriceAdjustment (geplande prijsaanpassing)

Klant- of tabelgebonden (`CustomerId` XOR `AgreementId`, runtime-afgedwongen —
`PriceAdjustmentService.Validate`), percentage XOR vast bedrag (`AmountDelta`), optionele
`RoundingStep` (`null`/0,01/0,05/0,10), optionele basis-/eenheidfilter. Bevestigen materialiseert
onmiddellijk nieuwe effectief-gedateerde regelversies en sluit de huidige versies de dag ervoor —
huidige prijzen blijven ongewijzigd tot de ingangsdatum; annuleren vóór activatie herstelt alles.

### CombinedUnitDiscount (combinatiekorting, spec §29-31)

Combineert verschillende eenheidtypes tot één gewogen "equivalent" aantal (bv. "1 europallet + 1
blokpallet + 2 colli, elk gewicht 1 → 4 eenheden → -8%"), geëvalueerd per `Scope`
(`Order`/`DeliveryAddress`/`Stop`) zodat hoeveelheden van verschillende afleveradressen nooit
mengen. Meest specifieke configuratie wint (klant+tabel > klant > tabel > bedrijfsbreed); een exacte
gelijkstand op hetzelfde niveau blokkeert de berekening.

### Order-zijde: PricingSource/one-off velden

`TransportOrder.PricingSource` (`Contract`/`OneOff`, string-opgeslagen). Bij `OneOff` draagt de
order haar eigen prijsafspraak: `OneOffFixedAmount`, `OneOffIncludedLoadingMinutes` /
`OneOffIncludedUnloadingMinutes` / `OneOffIncludedCombinedMinutes` (mutueel exclusief met de
per-activiteit velden), `OneOffExtraHourlyRate`, `OneOffNotes`. De engine slaat dan **alle**
regel-/tabelresolutie over.

### TransportOrderPricingLine (Kind-levenscyclus)

Eén bevroren regel van de berekening. `OrderPriceLineKind`:

| Kind | Betekenis | Overleeft herberekening? |
|---|---|---|
| `Auto` | Motor-gegenereerd, nooit bewerkt | Nee — wordt volledig herschreven |
| `AutoAdjusted` | Startte als motorregel, handmatig gecorrigeerd; `Original*` bewaart de motor-baseline | Ja — merget via `LineKey` |
| `Manual` | Vrije regel, door een gebruiker toegevoegd (of een verweesde `AutoAdjusted` regel wiens motorbron verdween) | Ja |
| `Proposed` | Onbevestigd motorvoorstel (bv. extra tijd); telt niet mee in `LinesTotal`/`AgreedPrice` tot bevestigd | Nee — wordt herschreven tot bevestigd |

`Proposed` (bool) is een **gedupliceerd** DTO-compat-veld: `Kind` is de enige waarheid, `Proposed`
moet altijd exact `Kind == Proposed` zijn. Elk schrijfpad dat `Kind` wijzigt gebruikt
`TransportOrderService.SetKind` (of zet `Proposed` in dezelfde toewijzing) zodat de twee nooit
uiteenlopen — zie de entiteits-doccomment en de "merge path"-test in `OneOffPricingTests`
(`AdjustingAProposedLine_ClearsProposed_...`).

### TransportOrderPricingSnapshot (Status-levenscyclus)

Eén header per order: tariefdatum, zone, betrokken tabellen (namen), `LinesTotal`, override-audit,
leesbare `Explanation`. `OrderPricingStatus`: `Draft` (herberekent vrij) → `Reviewed` (herberekent
nog, front-end waarschuwt) → `Locked` (weigert elke prijsmutatie, incl. herberekenen/regelbewerking/
bevestigen én elke prijsrelevante orderwijziging) → `Invoiced` (enkel gezet door facturatie, nooit
via het status-endpoint; nooit meer wijzigbaar).

## 3. Berekenvolgorde (canonieke pipeline)

Per `PricingEngine.CalculateAsync`-aanroep, in exacte volgorde:

1. **Regelselectie per eenheidlijn** — meest specifieke regel (klant beats bedrijf, zone beats
   zoneloos, dan `Priority`; exacte gelijkstand blokkeert).
2. **Orderbrede regels** (`PricingEngine.CalculateAsync`, rond regel 220-295) — twee onderscheiden
   takken, afhankelijk van of stap 1 al een eenheidregel matchte:
   - **(a) Componentmodel** (`anyRuleMatched == true`, regel ~229-237): elke tabel die zonet via
     een gematchte eenheidregel is "engaged" (zie `engagedAgreements`), levert OOK haar overige
     orderbrede regels (bv. een basisbedrag, een km-component) — élk als eigen regel, naast de
     eenheidregel(s). Dit is de vorm van een geconverteerde tarievenkaart met zowel een
     eenheidprijs als een kilometercomponent op dezelfde tabel: beide regels tellen mee.
     Enkel tabellen die effectief engaged zijn (via een gematchte eenheidregel) doen mee — een
     tabel van een andere klant, of een niet-engaged tabel van dezelfde klant, levert nooit een
     component (zie `Agreement_ComponentModel_OrderLevelRuleFiresAlongsideMatchedUnitLine` in
     `PricingEngineV2Tests`).
   - **(b) Fallback** (`anyRuleMatched == false`, regel ~238-295) — enkel wanneer GEEN
     eenheidregel matchte: de meest specifieke toepasselijke tabel wint (haar gegroepeerde
     orderbrede regels leveren samen de prijs); bestaat er geen tabelgebonden orderbrede regel,
     dan wint één standalone regel — één primaire prijsbasis, nooit gesommeerd over meerdere
     bases (spec §10/18).
3. **Afleidingsmodifiers** (afgeleide tabel) — oplopende `Sequence`, op de lopende subtotaal.
4. **Combinatiekorting** (spec §29-31) — na de modifiers, vóór de klantkoppeling-aanpassing.
5. **Klantkoppeling-aanpassing** (gedeelde tabel) — eerst `PercentAdjustment`, dan `FixedAdjustment`.
6. **Minimum** (`PricingAgreement.MinimumAmount`).
7. **Maximum** (`PricingAgreement.MaximumAmount`).
8. **Automatische toeslagen** (`PricingAgreementSurcharge`, alfabetisch).
9. **Diensten** (expliciet geselecteerd ∪ auto-toegepast).
10. **Voorgestelde tijdstoeslagen** (`Proposed`, uitgesloten van `Total`/`TotalWithProposed` apart).
11. **Handmatige aanpassingen** (order-zijde, ná de snapshot — zie §8).
12. **Snapshot** (bevroren bij opslag).

**Uitgewerkt getallenvoorbeeld per fase** (2 pallets × €50 basisregel, spec §21):

| Fase | Bedrag | Lopende subtotaal |
|---|---|---|
| Basisregel | 2 × €50 | €100,00 |
| Afleidingsmodifier "Nederland +30%" | +€30,00 (100 × 30%) | €130,00 |
| Klantkoppeling-aanpassing "-5%" | -€6,50 (130 × -5%) | €123,50 |

(Percentages stapelen multiplicatief in deze volgorde — niet optellen tot 25% en dan toepassen.
Zie `DerivedAgreementTests.Derived_PlusAssignmentAdjustment_AppliesModifiersBeforeAssignment_S21`,
exacte uitkomst €123,50.)

## 4. Precedentie & niveaus

Score = `tier × 4 + (zone gebonden ? 2 : 0)`, dan `Priority` als expliciete tie-breaker. `tier`:
privé (klantgebonden) = 2, gedeeld/toegewezen = 1, bedrijfsbreed (company-default) = 0. Een exacte
gelijkstand (zelfde score ÉN zelfde `Priority`) is een blokkerende `ConfigurationError` — nooit een
willekeurige keuze (`PricingEngine.SelectRule`). Hetzelfde model geldt voor:

- prijsafspraken (`PricingEngine.AgreementTier` — welke tabel de orderbrede regel/inbegrepen tijd
  levert wanneer meerdere tabellen zijn betrokken),
- combinatiekortingen (klant+tabel > klant > tabel > bedrijfsbreed),
- de bron van inbegrepen tijd (bij meerdere betrokken tabellen wint de specifiekste; een exacte
  gelijkstand blokkeert, spec: "Meerdere prijsafspraken met inbegrepen tijd op hetzelfde niveau").

### "Controle" (configuratievalidatie, `GET /api/pricing/agreements/{id}/validate`)

`PricingAdminService.ValidateAgreementConfigurationAsync` rapporteert configuratieproblemen van één
tabel zonder ooit te gooien — elke bevinding is een regel `{ severity: "error"|"warning", message }`.
Gebruikt door de **Controle**-sectie op `PricingTableDetailPage` (laadt automatisch, plus knop
"Controleer configuratie"). Controles:

| # | Controle | Ernst |
|---|---|---|
| 1 | Overlappende geldigheid van twee regels met identieke specificiteit (eenheid/zone/basis/klant/prioriteit) | error |
| 2 | Gat in een staffel (bv. 1-2 dan 4-5); een niet-laatste rij met een open einde (`ToQuantity == null`) vóór de laatste rij krijgt ook een eigen waarschuwing (zou anders de gat-check voor de volgende rij stilzwijgend overslaan — data-drift, normaal onmogelijk via opslagvalidatie) | warning |
| 3 | Staffel start niet bij 0 of 1 | warning |
| 4a | Afgeleide tabel: basistabel inactief | warning |
| 4b | Afgeleide tabel: basisvenster dekt het eigen venster niet volledig | warning |
| 4c | Basisketen-cyclus/te diep (zou onmogelijk moeten zijn via opslagvalidatie — data-drift) | error |
| 5 | Klantkoppeling buiten de geldigheidsperiode van de tabel zelf | warning |
| 6 | Gedeelde tabel zonder enige koppeling | warning |
| 7 | Regel verwijst naar een inactieve eenheid/zone | warning |
| 8 | `MinimumAmount` > `MaximumAmount` (zou onmogelijk moeten zijn via opslagvalidatie — data-drift) | error |

Elke controle is tenant-gefilterd. Zie `PricingValidationEndpointTests.cs` voor elk scenario.

## 5. Geldigheidsdata

Tariefdatum = `order.OrderDate` (nooit "vandaag"). Elke `PriceRule`/`PricingAgreement` heeft een
effectief venster (`EffectiveFrom`/`EffectiveUntil`, `EffectiveUntil == null` = onbeperkt); de motor
laadt enkel wat op de tariefdatum actief én binnen venster is. Een prijswijziging is dus altijd een
**nieuwe versie** (nieuw venster), nooit een overschrijving — geschiedenis blijft compleet:

- **Geplande aanpassing** (`ScheduledPriceAdjustment`) materialiseert toekomstige regelversies en
  sluit de huidige versie de dag vóór de ingangsdatum.
- **Dupliceren als nieuwe versie** (`PricingAdminService.DuplicateAgreementAsync`) kopieert regels
  (incl. staffels), toeslagen, modifiers en `BaseAgreementId` naar een nieuw venster, optioneel met
  percentage/vast-bedrag-aanpassing; koppelingen worden bewust NOOIT gekopieerd (moeten expliciet
  opnieuw via de klantkoppelingen-endpoint, zodat een nieuwe versie nooit stilzwijgend voor
  bestaande klanten gaat gelden).

## 6. Klantprijzen

Een klant "contract" = de som van: haar eigen (`CustomerId`-gebonden) regels/tabellen + de gedeelde
tabellen waaraan ze gekoppeld is (met hun eigen percentage/vast-bedrag-aanpassing) +
dienstoverrides (`CustomerServiceOptionPrice`) + geplande aanpassingen. Rij-niveau-override = een
klantgebonden kopie van een regel (wint gewoon via de gebruikelijke precedentie — geen apart
mechanisme). Eenmalige orders (`PricingSource = OneOff`) slaan dit hele contract over: de order
draagt haar eigen éénmalige prijsafspraak (zie §2).

## 7. Magazijndiensten (warehouse services)

Zie de kinds-tabel in §2: naast de klassieke eenheidprijs zijn er `PerOrderLine`/`PerKg`/`PerM3`/
`PerLdm` (afgeleid van ordermeetwaarden), `PerDay`/`PerPalletDay` (verplicht ingevoerde hoeveelheid)
en `PerHour`/`PerStop`. Auto-toepassing (`AutoApply`) maakt een dienst een stilzwijgende
contractdienst zonder handmatige selectie, met per-klant override (`AutoApplyOverride`,
`Disabled`). ADR-voorwaarde via `OnlyForAdr`. **Expliciete opmerking**: er bestaat **geen**
productcatalogus-voorwaarde — er is geen klant-goederenvoorraad-subsysteem gekoppeld aan
diensten; dat zou een apart integratiepunt zijn (bv. "enkel toepassen voor SKU X") mocht dat ooit
nodig zijn.

## 8. Order-prijslevenscyclus

**Statussen/gates** — zie §2 (`OrderPricingStatus`) en `TransportOrderService.PricingStatusTransitions`:
`Draft` ⇄ `Reviewed` ⇄ `Locked`; `Invoiced` enkel bereikbaar via facturatie. `Locked`/`Invoiced`
weigeren: regelbewerking (`SaveOrderPriceLinesAsync`), herberekenen (`RecalculateOrderPricingAsync`),
bevestigen (`ConfirmOrderPriceLineAsync`) en, bij een gewone save, een wijziging aan de
handmatige-override-velden (`PriceIsManual`/`AgreedPrice`/reden), de one-off-velden
(`PricingSource`, `OneOffFixedAmount`, `OneOffIncludedLoadingMinutes`,
`OneOffIncludedUnloadingMinutes`, `OneOffIncludedCombinedMinutes`, `OneOffExtraHourlyRate`,
`OneOffNotes`) of de expliciete dienstselectie (`PricingInputsChangedAsync`). Andere orderwijzigingen
— aantal, eenheid, stops, gewicht, notities — blijven bij een `Locked`/`Invoiced` prijs gewoon
toegestaan en gaan gewoon door; de prijs zelf blijft dan bevroren (geen herberekening).

**Merge-op-herberekening (`LineKey`):** elke motorregel draagt een stabiele `LineKey`
(`rule:{id}`, `agreement:{id}:{discriminator}`, `service:{id}`, `extratime:{loading|unloading|combined}`,
`combineddiscount:{id}:{groupKey}`, of `manual:{guid}` voor vrije regels). Bij een nieuwe berekening:
een `AutoAdjusted`-regel met overeenkomende `LineKey` behoudt de eigen `Label`/`Quantity`/
`UnitPrice`/`Amount`/`AdjustReason`, enkel de motor-baseline (`Original*`) ververst; een regel
zonder motor-overeenkomst (bron verdween, bv. verwijderde regel) wordt een verweesde `Manual`-regel
in plaats van stilzwijgend te verdwijnen; `Auto`/`Proposed`-regels worden altijd volledig herschreven.

**Handmatige bewerking + bewaarde originelen + audit:** `SaveOrderPriceLinesAsync` — een bestaande
regel aanpassen zet `Kind = AutoAdjusted` en vereist een reden (behalve op een reeds-`Manual`
regel); de eerste aanpassing bevriest `Original{Quantity,UnitPrice,Amount}`
(`CaptureOriginalIfFirstAdjustment`) zodat een latere aanpassing de motor-baseline nooit
overschrijft. "Verwijderen" van een `Manual`-regel is een echte delete; van elke andere regel zet
het bedrag op 0 (met reden) — de rij blijft, voor het spoor. Elke wijziging wordt geauditeerd
(`OrderPricing`/`lines_adjusted`).

**Voorgestelde toeslagen & bevestiging:** een `Proposed`-regel (bv. extra laad-/lostijd boven de
inbegrepen tijd) telt niet mee in `LinesTotal`/`AgreedPrice` tot `ConfirmOrderPriceLineAsync` ze
omzet naar `Kind = Auto` — dan tellen `LinesTotal` en `AgreedPrice` in één stap met exact het
regelbedrag op.

**Facturatie:** `AgreedPrice` (uit `LinesTotal`, of de handmatige override, of — als er niets te
berekenen viel én niets handmatig aangepast is — de legacy handmatige invoer, zie de bekende
beperking in §11) + de `TransportOrderServiceLine`-rijen worden apart gefactureerd. Bij
factuurgeneratie zet `InvoiceService` de snapshotstatus definitief op `Invoiced`.

## 9. Excel-rondgang (export/import)

`PricingExcelService` — één werkblad "Tarieven" per tarieventabel; export dient meteen als
sjabloon. Kolommen: RegelId, Naam, Basis, Eenheid, Zone, Prioriteit, Staffel van/tot, Gewicht tot
(kg), Volume tot (m³), Laadmeter tot, Staffelprijs, Prijs per extra, Eenheidsprijs, Basisbedrag,
Minimum, Maximum, Min. aantal, Afrondingsstap, Staffelmodus, Geldig van/tot.

**RegelId-identiteit:** de stabiele koppeling is altijd `RegelId` (de regel-GUID), nooit rijpositie.
Eén rij per staffel; een staffelloze regel krijgt één rij met lege staffelkolommen. Nieuwe regel =
lege RegelId (Naam+Basis identificeert de nieuwe regel binnen het bestand); regel verwijderen =
alle rijen van die regel weglaten (enkel toegepast als "Verwijderingen toepassen" is aangevinkt bij
commit).

**Preview-semantiek:** `PreviewAsync` schrijft nooit — dezelfde parse-/classificatiecode als commit,
zodat er maar één pad is dat "correct" moet zijn. Classificatie: Toegevoegd (geen matchende
RegelId) / Gewijzigd (matchende RegelId met een verschil in een veld) / Verwijderd (bestaande regel
niet meer gerefereerd). Rijfouten blokkeren commit volledig; waarschuwingen nooit (bv. een dubbele
staffelrij, of — ledger-fix — een lege Prioriteit-cel op een regel die eerder een niet-nul
prioriteit had: "Prioriteit leeg — 0 gebruikt voor '{regel}'", want dat kan stilzwijgend de
precedentie omgooien bij een volgende import).

**Commit-modi:** `UpdateAgreement` (wijzigt de bestaande tabel in-place) of
`DuplicateAsNewVersion` (dupliceert eerst — zelfde logica als de "Versies"-tab — past dan het
bestand toe op de kopie, alles in dezelfde transactie zodat een ongeldige import nooit een kale
kopie achterlaat).

## 10. Afronding

Decimalen overal (`decimal`, nooit `double`/`float` voor bedragen). `decimal.Round(x, 2)` op elk
regelniveau (elke `PriceBreakdownLine.Amount`, elke Excel-monetaire kolom bij het parsen). Geplande
aanpassingen ondersteunen een optionele extra afrondingsstap ná percentage/vast-bedrag maar vóór de
uiteindelijke 2-decimalen-afronding: `null` (geen), 0,01, 0,05 of 0,10 —
round-half-away-from-zero naar die stap (`PriceAdjustmentMath.Adjust`). Een aanpassing die een
negatief tarief zou opleveren wordt geweigerd (nooit stilzwijgend geclampt naar 0).

## 11. Bekende beperkingen

Eerlijk overgenomen uit de code (geen van deze is "verborgen" — elk is hieronder aangetoond):

1. **Herberekenen herbouwt dienstselecties uit de bewaarde regels, niet uit een bewerkbare
   vrachtselectie.** `RecalculateOrderPricingAsync` leest `TransportOrderServiceLines` (reeds
   opgeslagen) en de bewaarde `CargoItems` om de motor opnieuw te voeden — er bestaat nog geen
   losstaande "vracht bewerken" UI die een andere selectie zou kunnen voorstellen. Veilig zolang
   dat niet bestaat; zou herzien moeten worden zodra losstaande vrachtbewerking wordt toegevoegd.
2. **Een zuiver-`Auto` order dat `RequiresManualPrice` teruggeeft, valt terug op de legacy
   handmatige `AgreedPrice`-invoer.** Wanneer er niets te berekenen viel (geen geldig tarief) ÉN
   geen enkele regel handmatig is aangepast, blijft de vóór-de-motor manuele invoer (het
   `AgreedPrice`-veld van het request) gewoon werken ongewijzigd — zie de `else`-tak in
   `ApplyPricingAsync` (rond regel 1305 in `TransportOrderService.cs`).
3. **Stopvervanging bij een normale order-opslag verweest `StopExecution`-rijen.** `UpdateAsync`
   vervangt `order.Stops` altijd volledig (nieuwe GUID's) bij elke save — een `StopExecution` die
   naar een oude stop-id verwijst, verliest daarmee zijn koppeling. De mitigatie is het aparte
   `RecalculateOrderPricingAsync`-endpoint, dat stops nooit vervangt (enkel de prijs herberekent) —
   dat is precies waarom testcode die stop-executies simuleert een reflectie-oproep naar
   `ApplyPricingAsync` gebruikt in plaats van een gewone save (zie `OneOffPricingTests.RepriceAsync`).
4. **De front-end klantpaneel haalt per gedeelde tabel apart de koppelingen op (N+1-achtig).**
   `CustomerUnitPricingPanel.tsx` doet `sharedTables.map(a => getAgreementAssignments(a.id))` in
   een `Promise.all` — parallel, maar wel N aparte requests in plaats van één samengestelde
   endpoint. Merkbaar pas bij veel gedeelde tabellen; geen correctheidsprobleem.
5. **Combinatiekorting-groepen en de geprijsde eenheidsregel tellen vanuit twee verschillende
   bronnen.** `BuildPricingGroupsAsync` bouwt de combinatiekorting-groepen door per
   lossing-stop de `CargoItem.ExpectedQuantity` van de gekoppelde vrachtregels op te tellen
   (`byStop.GroupBy(...).Sum(x => x.Item.ExpectedQuantity)`), terwijl de geprijsde
   order-eenheidsregel gewoon `order.Quantity` gebruikt (`ApplyPricingAsync`). Lopen deze twee
   uiteen (vrachtregels die niet optellen tot het order-aantal), dan verdeelt de motor de
   combinatiekorting over fracties van een ander totaal dan wat effectief gefactureerd wordt.

## 12. Uitgewerkte voorbeelden

1. **Eenvoudig uurtarief (minimum 3 uur):** zie §2 — €72/uur, min. 3u, afronding per kwartier:
   2u10 → €216; 3u40 → €270.
2. **Gedeelde pallettabel met 2 klanten + 1 override:** basisregel €50/pallet in een gedeelde tabel;
   klant A gekoppeld zonder aanpassing (prijst €50/pallet via de tabel); klant B gekoppeld met
   `PercentAdjustment = -5` (prijst €47,50/pallet netto ná de klantkoppeling-stap in §3).
3. **NL = BE +30% + Wadden +€75** (`DerivedAgreementTests`): basisregel BE €50/pallet; afgeleide
   tabel NL met modifier "Nederland +30%" (landvoorwaarde NL) en "Waddeneilanden +€75"
   (zonevoorwaarde). Levering NL, postcode 9010 (Wadden-zone): 1 pallet → 50 + 15 (30%) + 75 = €140.
   Levering NL, postcode 1000 (niet-Wadden): 1 pallet → 50 + 15 = €65.
4. **Picking/PAL UIT/administratie auto-toeslagen** (§2/§7): Picking €1,25/colli op 3 colli → €3,75;
   PAL UIT €4,50/pallet op 5 pallets → €22,50; Administratie €1,50/orderregel op 3 regels → €4,50 —
   alle drie automatisch toegevoegd zonder expliciete selectie.
5. **Eenmalige order €850 met inbegrepen tijd** (`OneOffPricingTests`): `OneOffFixedAmount = 450`,
   `IncludedCombinedMinutes = 60`, `ExtraHourlyRate = 75`; werkelijke laad+lostijd 45+45=90 min →
   30 min boven de 60 inbegrepen → voorstel 30/60 × €75 = €37,50 (`Proposed`, telt niet mee tot
   bevestigd); `TotalWithProposed = 487,50`.
6. **Combinatiekorting per afleveradres** (`CombinedUnitDiscountTests.S15`): 1 eenheidtype (europallet,
   €50/stuk), korting 5% bij 2-3 equivalente eenheden. 5 pallets verdeeld over 2 adressen (3 in
   Antwerpen, 2 in Mechelen, `Scope = DeliveryAddress`): elk adres krijgt zijn EIGEN staffel (nooit
   de gecombineerde 5-eenheden-staffel) — Antwerpen: aandeel 3/5 × €250 = €150 → -5% = -€7,50;
   Mechelen: aandeel 2/5 × €250 = €100 → -5% = -€5,00; totaal €250 - €7,50 - €5,00 = €237,50.
7. **Componentmodel: eenheidregel + orderbrede km-component op dezelfde tabel**
   (`Agreement_ComponentModel_OrderLevelRuleFiresAlongsideMatchedUnitLine`, §3 stap 2a): gedeelde
   tabel met een `PerUnit`-regel €22/pallet (matcht de eenheidlijn) ÉN een `PerKm`-regel
   (`BaseAmount` €25, €1,20/km). Klant A gekoppeld, klant B niet. Order 3 pallets, 100 km:
   klant A → eenheidlijn 3 × €22 = €66 + km-component 25 + 1,20×100 = €145 → totaal €211 (beide
   regels vuren, want de tabel is "engaged" via de gematchte eenheidregel). Klant B (geen koppeling)
   → geen enkele regel van deze tabel vuurt — geen km-lijn, `RequiresManualPrice`.
