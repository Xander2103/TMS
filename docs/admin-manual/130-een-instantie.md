---
id: een-instantie
title: Beperking: één instantie
category: Beheer
roles: admin
keywords: single instance, schalen, pilot, één instantie, load balancer, rate limit
related: deployment, storingen
---
## WAT DOE JE HIER?

Je leest waarom de pilot op precies één API-proces draait.

## WANNEER GEBRUIK JE DIT?

Voordat je denkt aan opschalen of een load balancer.

## STAPPEN

1. Draai exact één API-instantie.
2. Gebruik geen blue/green-overlap: de oude stopt voordat de nieuwe start.

## WAT GEBEURT ER DAARNA?

Achtergrondtaken lopen in dat ene proces. Ze hebben een vergrendeling, zodat een tweede instantie ze zou overslaan.

## WAAR MOET JE OP LETTEN?

- Rate limits, kiosktokens en caches staan in het geheugen van het proces. Met twee processen kloppen ze niet meer.
- De aanmeldlimiet is 10 per minuut per IP-adres. Een kantoor achter één internetadres deelt die limiet.
- Details: `docs/operations/single-instance.md`.

## VEEL VOORKOMENDE PROBLEMEN

**"Te veel pogingen" bij het begin van de werkdag.** Veel collega's melden tegelijk aan vanaf één adres. Wacht een minuut.
