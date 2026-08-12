# Opslag (Wave 5)

## De bewegingsklok

Elke collo (handling unit) krijgt automatisch **opslagverblijven** (`storage_stays`):

- **Openen**: een Ontvangst- of Retour-inboekscan op het magazijnstation (Trace & voorraad).
  Het station geeft zijn magazijn mee; een gekozen locatie impliceert het magazijn.
- **Sluiten**: centraal, bij ELK vertrek-custody-event (laadscan, retour-/herleveringslading,
  retour naar afzender, annulering) — één interceptor vangt elk huidig én toekomstig
  vertrekpad. Correcties sluiten en heropenen; historiek wordt nooit herschreven.
- Maximaal één open verblijf per collo; opnieuw ontvangen ververst alleen de locatie.

Beperking (bewust): een ritgebonden depot-retour opent de klok pas wanneer de collo op het
magazijnstation wordt ontvangen/ingeboekt — het rit-event kent het magazijn niet.

## Pallet-dagen naar facturatie

`GET /api/customers/{id}/storage?from=&to=` (en het paneel "Opslag per klant" op Trace &
voorraad) berekent per klant en periode de **pallet-dagen**: overlap van elk verblijf met de
periode, per **begonnen dag** (dezelfde conventie als de handmatige pallet-dag-invoer); open
verblijven tellen tot het periode-einde. Uitsplitsing per order en per magazijn.

De uitkomst voedt de bestaande dienstsoorten **Per dag** en **Per pallet/dag** — de operator
(en later de Wave-10-voorstellenmotor) leest hier het echte aantal; handmatig ingevulde
dagen op een order winnen altijd.

## Handling IN/UIT automatisch verkopen

Geen nieuw mechanisme nodig: een dienst met **Automatisch toepassen** + **magazijnconditie**
(en bv. soort *Per eenheid*) prijst de handling zodra de order het magazijn raakt — inclusief
de Wave-2-verkoopcode voor de juiste grootboekpost. Recept: Diensten & toeslagen → nieuwe
dienst "Handling in" (PerUnit, auto, magazijn X) en idem "Handling uit".
