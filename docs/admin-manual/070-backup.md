---
id: back-up
title: Back-up en herstel
category: Beheer
roles: admin
keywords: back-up, backup, restore, herstel, terugzetten, hersteloefening, pg_dump, off-host
related: opslag, storingen, deployment
---
## WAT DOE JE HIER?

Je zorgt dat je terug kunt als er iets misgaat.

## WANNEER GEBRUIK JE DIT?

Elke dag controleren, elke maand oefenen.

## STAPPEN

![De systeeminformatie met het tabblad Back-ups](shot:beheer-systeem "Systeeminformatie en back-ups")

1. Open **Parameters**, **Systeeminformatie**, **Back-ups**.
2. Kijk of de laatste back-up **Voltooid** is en recent. Het systeem maakt elke dag om 02:00 UTC een back-up.
3. Klik op **Nieuwe back-up maken** vóór een riskante ingreep.
4. Kopieer de sets naar een andere locatie, versleuteld. **Dat is verplicht.**

### Herstellen

Herstellen kan niet vanuit de applicatie. Het is een offline procedure op de server volgens `docs/operations/backup-restore.md`. Je verliest alle gegevens van na het tijdstip van de back-up.

### Maandelijkse oefening

Zet een back-up terug op een aparte server en controleer de cijfers en documenten. Zie sectie 4 van het runbook.

## WAT GEBEURT ER DAARNA?

Een set is een `.dump`, een `.files.tar.gz` en een `.sha256`. Ze telt pas als voltooid na verificatie van de inhoud. Is ze ouder dan 26 uur, dan krijg je een waarschuwing.

## WAAR MOET JE OP LETTEN?

- De **encryptiesleutels zitten niet in de back-up**. Bewaar ze in een kluis. Zonder de kolomsleutel is een back-up deels onbruikbaar.
- Downloaden kan alleen als systeembeheerder.

## VEEL VOORKOMENDE PROBLEMEN

**De back-up mislukt.** De status en de reden staan in de lijst. Meestal is het schijfruimte of de rechten op de opslagmap.
