---
id: opslag
title: Opslag van bestanden
category: Beheer
roles: admin
keywords: storage, opslag, Storage:RootPath, verify-storage, uploads, bestanden
related: back-up, productie-configuratie
---
## WAT DOE JE HIER?

Alle bestanden, zoals foto's, documenten en PDF's, staan in één map op de server.

## WANNEER GEBRUIK JE DIT?

Bij de installatie en na een herstel.

## STAPPEN

1. Kies een map buiten de applicatie, bijvoorbeeld `/var/lib/transportationservice/storage`.
2. Stel die in met `Storage__RootPath` in `tms.env`.
3. Geef de service-gebruiker schrijfrechten.
4. Controleer met het commando `--verify-storage` van de API. Doe dat ook na elke migratie en elk herstel.

## WAT GEBEURT ER DAARNA?

De API controleert bij elke start of de map absoluut, schrijfbaar en buiten de applicatie is. Anders weigert hij te starten. `/health/ready` controleert de opslag ook.

## WAAR MOET JE OP LETTEN?

Plaats de map nooit binnen de releasemap. Een deploy zou de bestanden dan kunnen wissen. Het deployscript weigert zo'n instelling. Details: `docs/storage.md`.

## VEEL VOORKOMENDE PROBLEMEN

**`--verify-storage` meldt ontbrekende bestanden.** Bekijk welke verwijzingen het zijn en haal ze zo nodig uit de laatste back-up.
