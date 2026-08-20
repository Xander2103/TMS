# Prikklok (kiosk) — installatie & beheer

## Een prikklok opzetten

1. **Registreren** — Instellingen → Urenregistratie → "Prikklok toevoegen": naam
   (bv. "Prikklok magazijn") en optioneel een Locatie (punches krijgen die locatie als
   bron). Na aanmaken verschijnt éénmalig de **provisioning-sleutel** — bewaar die kort
   en veilig; ze wordt nooit opnieuw getoond.
2. **Device koppelen** — open op de tablet/touch-pc `https://<erp-host>/kiosk`.
   Het instelscherm vraagt éénmalig de provisioning-sleutel; na "Registreren" toont het
   device de klok + het numerieke keypad. De sleutel staat daarna alleen in de
   localStorage van dat device.
3. **Browser vastzetten (kioskmodus)** — de pagina is fullscreen en linkt nergens
   naartoe, maar zet de browser zelf ook vast:
   - Chrome/Edge: start met `--kiosk https://<erp-host>/kiosk` (Windows-snelkoppeling
     of beleids-/MDM-profiel), of Android "pinned app"/beheerde modus.
   - iPad: Safari-webapp op het beginscherm + Begeleide toegang.
   - De PWA-shell (standalone manifest) houdt de UI bruikbaar bij korte wifi-dips;
     punches zelf vereisen altijd serverbevestiging.
4. **PIN-codes uitdelen** — per medewerker op de fiche → tab Urenregistratie →
   "Prikklokcode" (genereren of zelf zetten; lengte volgt de instelling, default 4).

## Dagelijks gebruik

Code intikken (touch of fysiek toetsenbord/USB-keypad — scanners die toetsenbord
emuleren werken dus ook) → ✓ → welkomscherm met uitsluitend de geldige actie(s) →
bevestiging → automatische reset na ±4 s. Het welkomscherm reset ook zelf na 25 s
inactiviteit zodat er nooit persoonsgegevens blijven staan.

## Beheer

| Actie | Effect |
|---|---|
| **Sleutel vernieuwen** | Nieuwe provisioning-sleutel (éénmalig getoond); de oude is per direct ongeldig. Doe dit bij verlies/diefstal van het device. |
| **Uitschakelen** | Device weigert alle verkeer; her-inschakelen kan zonder nieuwe sleutel. |
| **Kiosk uit (instelling)** | Zet de volledige kioskflow van de tenant uit; devices tonen "De prikklok is uitgeschakeld." |
| **Laatste activiteit / laatste punch** | Zichtbaar in de devicelijst — handig om dode devices te spotten. |

Meerdere prikklokken per tenant zijn normaal (magazijn-in, magazijn-uit, kantoor, depot …)
— allemaal dezelfde engine, elk met eigen sleutel en locatie.

## Offline / netwerkfouten

De kiosk doet **nooit** alsof een punch lukte zonder serverbevestiging. Bij
netwerkproblemen toont hij: *"Geen verbinding met de server. Inpunten kan momenteel niet
veilig geregistreerd worden. Probeer opnieuw."* Ontbrekende punches herstelt HR daarna
via een manuele registratie/correctie (volledig geauditeerd).

**Toekomstige offline queue** (bewust nog niet gebouwd): het bestaande
`ActionQueue`-patroon van de chauffeursapp (localStorage-queue, `clientRequestId`-
idempotency, geordende replay) is direct herbruikbaar; aandachtspunten zijn een
device-gebonden namespace (geen user-id), servertijd-vs-devicetijd bij replay en een
zichtbare "nog niet gesynchroniseerd"-status op het device.

## Toekomstige hardware

`AttendanceCredential.Type` is extensible (Badge/NFC/QR/hardwaretoken). Omdat de meeste
USB-badge-/QR-lezers toetsenbordinvoer emuleren, werkt de bestaande keypad-invoer al;
een nieuw credentialtype betekent vooral een eigen lookup-/verificatiepad, geen
herbouw van de kioskflow.
