# Problemen, verantwoordelijkheid & herlevering (Wave 6)

## Twee probleemrecords, één overzicht

- **Incident** — het beheersniveau: dossier-/ordergekoppeld, kosten, opvolging, deadline.
- **Uitvoeringsuitzondering** — het ritniveau: scan-/pakketafwijkingen uit de uitvoering.

De **verenigde problemenlijst** (`GET /api/problems`, blok op de incidentenpagina) toont beide
soorten open problemen samen; elke rij linkt naar zijn eigen detail (incident / rit).

## Verantwoordelijkheid

Elk incident draagt een **verantwoordelijke partij**: Onbekend, Klant, Eigen organisatie,
Chauffeur of Leverancier (+ toelichting). Elke wijziging is geauditeerd.

## Doorrekening (goedkeuringsplichtig)

| Stap | Wie | Wat |
|---|---|---|
| Voorstellen | `incidents.manage` | Alleen bij verantwoordelijkheid **Klant**; bedrag + omschrijving. |
| Goed-/afkeuren | `problems.approve_charge` (rollen v28: management + boekhouding) | Servicezijdig fail-closed afgedwongen. |
| Goedkeuring | automatisch | Maakt een **handmatige verkooplijn** op de gekoppelde order (bestaande lijnmechanieken, reden "Incident: …", telt mee in totaal en factuurgereedheid). Vergrendelde/gefactureerde prijs → beslissing blijft geregistreerd, lijn handmatig bij facturatie (Wave 10 toont dit). |

Interne verantwoordelijkheid (Eigen/Chauffeur/Leverancier) kan nooit worden doorgerekend —
die kosten blijven intern (EstimatedCost/ActualCost).

## Herlevering

`POST /api/incidents/{id}/redelivery` (orders.create) dupliceert de gekoppelde order als
nieuwe CONCEPT-order: zelfde klant/goederen/stops, referentie "HERLEVERING {orig}",
**in hetzelfde dossier**; originele colli gaan naar *Herlevering gepland* waar de
levenscyclus dat toelaat. Eén herlevering per incident; de herleveringskost volgt het
doorrekeningsproces op de nieuwe order.
