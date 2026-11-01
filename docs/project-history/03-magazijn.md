---
id: geschiedenis-magazijn
title: Magazijn en voorraad
category: Projectgeschiedenis
order: 3
---
## Probleem

Een artikel met voorraad en bewegingen verwijderen wiste de historiek (antwoord 204). De andere problemen zijn bekend via de herstelcommits: overlopende hoeveelheden, geen eenduidige identiteit voor serienummers, geen uitgifte- en retourbalie en verouderde correcties. Voor deze tak bestaat geen eigen bevindingenrapport, dus de oorspronkelijke metingen zijn niet bewaard.

## Risico

Verlies van voorraadhistoriek, twee houders van hetzelfde serienummer, en geen veilige manier voor magazijnmedewerkers om te werken zonder HR-rechten.

## Oplossing

- Bewaking van de historiek bij verwijderen.
- Alleen hele eenheden: 1 tot 100.000 per beweging, voorraad maximaal ±1.000.000, met gecontroleerde rekenkunde.
- `NormalizedSerialNumber` met een gefilterde unieke index `UX_employee_issued_items_open_serial`.
- Verbruiksartikelen krijgen de status Consumed.
- `ExpectedCurrentStock`: wie op verouderde cijfers corrigeert, krijgt een 409.
- Gearchiveerde artikelen weigeren nieuwe uitgiftes maar aanvaarden nog retours.
- Een eigen balie op `/inventory/issues`, met de rechten `inventory.issue` en `inventory.return` (rolsjabloon v39).
- Een veilige medewerkerskiezer die alleen nummer, naam en actief-status teruggeeft.

## Belangrijke architectuurbeslissing

Hoeveelheden zijn gehele getallen. De identiteit van een serienummer ontstaat bij de uitgifte: er is geen apart activaregister. De catalogus blijft bij HR en Beheer, de bewerkingen bij Magazijn. Bij uitdiensttreding is uitstaand materiaal een waarschuwing: het blokkeert niet en er wordt niets automatisch teruggeboekt.

## Uitgevoerde tests

Magazijnrechten 22/22, smokes 11/11, offboarding met materiaal 30/30. De racematrix toonde nooit negatieve voorraad en altijd één houder.

## Resultaat

Magazijn werkt zelfstandig zonder HR-rechten. Commits: `d62da97f`, `681e2c0f`, `ec947d82`, `6c9b0f88`.
