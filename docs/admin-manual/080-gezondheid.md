---
id: systeem-gezondheid
title: De gezondheid van het systeem bekijken
category: Beheer
roles: admin
keywords: health, health live, health ready, details, gezondheidscontrole, systeeminformatie, versie, build
related: storingen, back-up
---
## WAT DOE JE HIER?

Je controleert of het systeem goed draait.

## WANNEER GEBRUIK JE DIT?

Dagelijks, na elke deploy en bij een storing.

## STAPPEN

1. Open **Parameters**, **Systeeminformatie**. Je ziet de versie, de databaseversie, de laatste migratie en de status.
2. Bewaking kan de volgende adressen opvragen:

| Adres | Wat controleert het? | Wie mag het? |
|---|---|---|
| `/health/live` | Draait het proces? | Publiek |
| `/health/ready` | Database, migraties, encryptiesleutel, opslag | Alleen intern op de server, geeft 503 bij een probleem |
| `/health/details` | Ook mailwachtrij, taken, Peppol, back-up | Aangemeld, met het recht system_info.view |

## WAT GEBEURT ER DAARNA?

Geeft `/health/ready` 503? Dan is er iets mis. Zie [Storingen](help:storingen).

## WAAR MOET JE OP LETTEN?

`/health/ready` kijkt bewust niet naar de back-up, zodat een oude back-up de site niet offline haalt. Bewaak de back-up via `/health/details`.

## VEEL VOORKOMENDE PROBLEMEN

**`/health/ready` geeft 404 van buitenaf.** Dat is juist: nginx laat alleen `/health/live` door.
