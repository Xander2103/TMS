# Snelgids — Dispatcher / Planner

*Van dossier tot afgehandeld probleem, in zes stappen.*

## 1. Dossier aanmaken

1. **Dossiers → Dossiers** → **Nieuw dossier** (`/dossiers/new`).
2. Kies de **klant** (alleen dit veld is verplicht) en eventueel een sjabloontegel
   (bv. transport): die wordt meteen de eerste activiteit.
3. Op de dossierpagina toont het **Aandacht-paneel** wat er nog ontbreekt (activiteit,
   opdracht, datum, prijs); elk punt springt naar de juiste sectie.

## 2. Opdracht aanmaken

1. Klik op de transportactiviteit in het dossier en maak de **conceptopdracht** aan, of
   werk via **Dossiers → Opdrachten (klassiek)** (`/transport-orders`).
2. Vul de secties in: **Algemeen** → **Route & stops** (laad-/losstops, tijdvensters en
   tijdseisen zoals "leveren vóór 10:00") → **Goederen** (goederenlijnen met eenheid;
   vul ook afstand/laadmeters in als daarop getarifeerd wordt) → **Services & toeslagen**.
3. Portaalopdrachten komen binnen als *Ingediend*: beoordeel ze met accepteren, afwijzen
   (reden verplicht) of info vragen.

## 3. Prijs controleren

1. De prijs wordt automatisch berekend op de orderdatum. Controleer de **dekking**: bij
   "Niet alle goederen zijn geprijsd." toont de dekkinglijst per lijn de reden.
2. Corrigeer waar nodig met handmatige lijnen (reden verplicht) of **Herberekenen**.
3. Klik **Prijs bevestigen** zodra de prijs klopt; met **Prijs aanpassen** heropent u een
   bevestigde prijs (reden verplicht).

## 4. Plannen

1. Open **Planning → Ritlijst** (`/planning`) en gebruik het paneel **Ritvoorstellen**:
   kies de datum, bekijk de voorstellen per **leverzone** (met gewicht/ldm/pallet-totalen;
   achterstand staat vooraan, orders zonder zone staan onder "Ongezoneerd" met reden).
   Per order ziet u de **randvoorwaarden** (ADR, kraan, plateau, Moffett, gevraagd
   tijdvenster, openingsuren van de loslocatie) en per voorstel een
   **capaciteitssignaal** tegenover het grootste actieve voertuig.
2. Klik **Maak rit** om een voorstel om te zetten in een rit — bestaande toewijzings- en
   conflictcontroles blijven gelden. Blokkerend: een **ADR-order** vereist een chauffeur
   met een geldige ADR-kwalificatie op de rit.
3. Of plan visueel via **Planning → Planbord** (`/planning-center`).
4. Druk vanaf het ritdetail de documenten af: **CMR's (rit)** en **Leveringsbonnen (rit)**
   (één PDF in routevolgorde).

## 5. Opvolgen

- **Planning → Live opvolging** (`/operations`): voortgang per rit en stop, met ETA's.
  U kunt een ETA handmatig overschrijven; bij een ETA-verschuiving boven de ingestelde
  drempel wordt de klant automatisch gemaild.
- **Vandaag → Meldingen** en **Berichten** houden u op de hoogte; klantvragen uit het
  portaal beantwoordt u via de berichtenthread van de order.
- Gevoelige klantmails (schade, mislukte levering, vertraging) wachten in de
  **controlewachtrij** van het meldingenbeheer: met berichtenbeheer geeft u ze daar vrij
  of wijst u ze af vóór ze de klant bereiken.

## 6. Problemen afhandelen

1. **Vandaag → Problemen → Incidenten** (`/incidents`): de verenigde lijst toont open
   incidenten én uitvoeringsafwijkingen.
2. Een **mislukte stop** maakt automatisch één incident aan (nooit dubbel), gekoppeld
   aan order, rit, klant en dossier. Afhankelijk van de **herleveringsmodus**
   (bedrijfsinstelling) toont het incident **"Herlevering aanbevolen"** (Voorstellen) of
   staat de herleveringsorder er al (Automatisch).
3. Leg op het incident de **verantwoordelijke partij** vast (Onbekend / Klant / Eigen
   organisatie / Chauffeur / Leverancier) met toelichting.
4. Bij verantwoordelijkheid *Klant*: **Doorrekening voorstellen** (bedrag +
   omschrijving); management of boekhouding keurt goed — de verkooplijn komt dan
   automatisch op de order. Een **doorrekenbeleid** per klant/incidenttype kan dit ook
   automatisch boeken of juist volledig blokkeren.
5. Moet er opnieuw geleverd worden: **Herlevering aanmaken** — een nieuwe conceptorder in
   hetzelfde dossier met referentie "HERLEVERING {origineel}", gedateerd op de
   **eerstvolgende werkdag** (weekends en feestdagen worden overgeslagen).
