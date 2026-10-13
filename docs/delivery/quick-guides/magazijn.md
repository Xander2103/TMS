# Snelgids — Magazijn

*Scannen, locaties, trace, voorraad en opslagverblijven.*

## Scannen met een rit (laden & lossen)

1. Open **Magazijn → Laden & scannen** (`/warehouse`): u ziet de laadlijsten per rit en
   per laadstop, plus een zoekveld voor colli.
2. Klik **Scannen** bij de stop: het scanpaneel opent in de juiste modus (laden bij een
   laadstop, lossen bij een losstop). Colli in retourfase krijgen extra retour-scanmodi.
3. Scan de barcodes; het paneel toont verwacht versus gescand. Dubbel scannen is
   onschadelijk — elke scan is idempotent.

## Scannen zonder rit (magazijnstation)

Open **Magazijn → Trace & voorraad** (`/warehouse/trace`), scan of typ een barcode en
kies de scansoort:

| Scansoort | Wanneer |
|---|---|
| **Ontvangst** | Collo komt het magazijn binnen. Kies het magazijn en eventueel meteen de locatie. |
| **Verplaatsen** | Collo gaat naar een andere locatie (locatie verplicht). Wijzigt nooit de status. |
| **Klaarzetten** | Collo staat klaar voor vertrek (operationele markering). |
| **Retour inboeken** | Geweigerde of mislukte levering komt terug in depot. |

Onbekende barcodes of onverwachte statussen worden als waarschuwing geregistreerd — meld
ze aan de dispatch, ze verdwijnen nooit stilletjes.

## Locaties toewijzen en verplaatsen

- Locaties bestaan uit **zones** (bv. `A — Bulkzone`) met **posities** (bv. `A-01`);
  codes zijn uniek per magazijn.
- Beheer: **Magazijn → Magazijnen (beheer)** (`/warehouses`) → **Locaties beheren**.
- Een locatie met posities of met colli erop kan niet worden verwijderd.
- De locatie van een collo werkt u bij met een **Ontvangst**- of **Verplaatsen**-scan.

## Waar is een collo? (trace)

Scan of zoek een barcode op **Trace & voorraad**: u ziet het collonummer, de huidige
locatie, de gekoppelde order en klant, en de laatste tien bewegingen.

Zodra een collo op een rit geladen wordt (of definitief vertrekt), wordt zijn
magazijnlocatie automatisch **gewist**: goederen die op de vrachtwagen staan, tonen dus
nooit meer een magazijnlocatie. Alleen een echte magazijnscan (Ontvangst, Verplaatsen,
Retour inboeken) zet opnieuw een locatie.

## Voorraadoverzicht

Hetzelfde scherm toont per magazijn de colli per locatie, met twee signalen:

- **"had vandaag buiten gemoeten"** — het collo staat er nog terwijl zijn order op een
  rit van vandaag zit: eerst afhandelen.
- **"wacht op morgen"** — collo staat correct te wachten.

## Opslagverblijven (pallet-dagen)

- Een **Ontvangst**- of **Retour inboeken**-scan opent automatisch het opslagverblijf van
  het collo; elk vertrek (laadscan, herlevering, retour naar afzender, annulering) sluit
  het. U hoeft hiervoor niets extra te doen — correct scannen volstaat.
- Het paneel **"Opslag per klant (pallet-dagen)"** op Trace & voorraad toont per klant en
  periode de pallet-dagen (per begonnen dag), uitgesplitst per order en magazijn — dit is
  de basis voor de opslagfacturatie.
- Let op: een ritgebonden depot-retour opent de klok pas wanneer u het collo op het
  magazijnstation inboekt.

## Uw scans sturen de facturatie

Diensten zoals inslag, uitslag en picking kunnen hun aantallen rechtstreeks uit de
**werkelijke scan-events** halen, en pallet-dagen volgen de opslagklok. Correct en
volledig scannen is dus ook commercieel belangrijk: te weinig scannen betekent te weinig
aanrekenen. Handmatig ingevulde aantallen op de order winnen altijd van de scan-telling.
