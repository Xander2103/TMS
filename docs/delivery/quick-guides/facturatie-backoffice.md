# Snelgids — Facturatie-backoffice

*Van factuurgereedheid tot Peppol-verzending en creditnota's.*

## Factuurgereedheid

Een order is factureerbaar zodra er geen belemmering meer is. Het systeem herkent deze
redenen en toont ze als badge:

- *nog geen prijs* / *niet alle onderdelen geprijsd* / *geen onderdeel volledig geprijsd*
- *prijs verouderd — herbereken*
- *afleverbewijs ontbreekt*

Prijzen worden bevestigd op de order zelf (**Prijs bevestigen**); bevestigen met
niet-geprijsde goederen vereist een apart recht én een reden.

## De facturatiecontrole-werkruimte

**Klanten → Facturatie → Facturatiecontrole** (`/invoice-control`) — uw dagelijkse start:

1. **Goedgekeurde doorrekeningen — handmatig toe te voegen** (bovenaan, indien aanwezig):
   incident-toeslagen die zijn goedgekeurd nadat de orderprijs al vergrendeld of
   gefactureerd was. Voeg deze zelf als factuurlijn toe — het systeem herinnert u eraan
   tot het gebeurd is.
2. **Factuurvoorstellen** — orders die klaar zijn, gegroepeerd per klant volgens diens
   groeperingsvoorkeur. Met de **selectievakjes** factureert u desgewenst maar een deel
   van een voorstel. Klik **Maak factuur**: de factuur wordt aangemaakt met de gekozen
   orders en u komt direct op het factuurdetail.
3. **Nakijken vóór facturatie** — per order de reden(en) waarom die nog niet klaar is;
   werk deze uitzonderingen weg (herberekenen, POD opvragen, prijs bevestigen).
4. **Uitstellen (snooze)** — een order die nog niet mee mag, stelt u uit tot een datum,
   met reden. Uitgestelde orders verdwijnen uit de voorstellen en de nakijklijst en
   blijven zichtbaar in de eigen sectie **"Uitgesteld"** tot de datum verstrijkt of u
   het uitstel opheft. Elke uitstel-actie wordt geauditeerd.

## Groeperingsvoorkeuren per klant

Op de klantfiche (sectie **Facturatie**) stelt u per klant **Factuurgroepering** in:

- *Handmatig (standaard)* — geen automatische voorstellen;
- *Eén factuur per dossier*;
- *Wekelijks verzamelen* / *Maandelijks verzamelen*;
- *Per klantreferentie*.

Daar staan ook de **factuurtaal**, de betaaltermijn, de **standaard facturerende
entiteit** en de **toegestane facturerende entiteiten** (dossiers/orders/facturen buiten
die lijst worden geweigerd).

## Incident-toeslagen (doorrekening)

Alleen incidenten met verantwoordelijkheid **Klant** kunnen worden doorgerekend. Een
voorstel (bedrag + omschrijving) wordt door management of boekhouding **goedgekeurd of
afgekeurd**; goedkeuring maakt automatisch een verkooplijn op de order (reden
"Incident: …"). Was de prijs al vergrendeld/gefactureerd, dan verschijnt de toeslag in de
facturatiecontrole als handmatig toe te voegen lijn.

Onder **Parameters → Beheer → Doorrekenbeleid** (`/settings/charge-policies`) kan per
klant en/of incidenttype een beleid staan: **Nooit**, **Voorstellen** of **Automatisch**
(met standaardbedrag). Het meest specifieke beleid wint en vuurt één keer, zodra de
verantwoordelijkheid op *Klant* landt. *Automatisch* boekt de lijn via hetzelfde
mechanisme als een handmatige goedkeuring — geauditeerd en omkeerbaar zolang de prijs
niet vergrendeld is; *Nooit* blokkeert ook handmatig voorstellen. Eigen fouten kunnen
nooit worden doorgerekend.

## Vastgehouden klantmail (controlewachtrij)

Gevoelige klantmails (standaard: schade, mislukte levering en vertraging) worden niet
meteen verstuurd maar wachten in de **controlewachtrij** van het meldingenbeheer. Iemand
met berichtenbeheer geeft ze vrij of wijst ze af; alleen klantmail wordt vastgehouden.

## Peppol verzenden

- **Klanten → Facturatie → Peppol** (`/peppol`): tab **Overzicht** (checklist per eigen
  bedrijf), **Uitgaand** (transmissies met opnieuw proberen/annuleren), **Inkomend**
  (ontvangen documenten beoordelen), **Configuratie** en **Validatieproblemen**.
- Op het **factuurdetail**: paneel Peppol met **Valideren**, **Voorbeeld**, **XML
  downloaden**, **Versturen via Peppol** en de transmissietijdlijn. Verzenden kan alleen
  op verzonden/betaalde facturen met een geldige btw-configuratie; validatiefouten worden
  in het Nederlands uitgelegd.
- De verzendwachtrij handelt de status automatisch af (Wachtrij → Aangeboden →
  Afgeleverd / Geweigerd / Mislukt); mislukte verzendingen kunt u met een klik opnieuw
  aanbieden.

## Creditnota's

- Van een verzonden of betaalde factuur maakt u een creditnota
  (factuurdetail; maximaal één levende creditnota per factuur, nooit een creditnota van
  een creditnota).
- Bedragen blijven positief; het credit-karakter zit in het documenttype en wordt
  doorgevoerd in de boekhoudexport, de PDF (titel CREDITNOTA) en het klantportaal
  (creditnota-badge).
- De nummering deelt de maandreeks van de facturen met het creditnota-voorvoegsel van de
  eigen entiteit.
