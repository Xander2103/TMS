---
id: storingen
title: Wat doe je bij een storing?
category: Beheer
roles: admin
keywords: storing, database onbereikbaar, mail onbereikbaar, 503, onderhoudsmodus, opnieuw starten, incident
related: systeem-gezondheid, back-up, mail-configuratie
---
## WAT DOE JE HIER?

Je handelt een storing rustig af.

## WANNEER GEBRUIK JE DIT?

Als gebruikers melden dat het niet werkt.

## STAPPEN

### De database is onbereikbaar

1. Gebruikers zien "De server is tijdelijk niet bereikbaar". `/health/live` blijft 200, `/health/ready` geeft 503.
2. Start de database opnieuw. Bekijk de schijfruimte en de logs.
3. Het systeem herstelt vanzelf zonder API-herstart. Er wordt tijdens de storing niets half opgeslagen.

### De mailserver is onbereikbaar

1. Het werk gaat gewoon door. Mails blijven in de wachtrij en worden opnieuw geprobeerd.
2. Controleer de SMTP-gegevens en de server.
3. Kijk daarna in `/health/details` of de wachtrij leegloopt, en beoordeel berichten die wachten op **Vrijgeven**.

### De API draait niet

1. Bekijk `systemctl status transportationservice-api` en de journal.
2. Lees de reden: de API meldt een onveilige of ontbrekende instelling letterlijk.

## WAT GEBEURT ER DAARNA?

Controleer `/health/ready` en vraag een gebruiker om een test.

## WAAR MOET JE OP LETTEN?

- Start nooit oude code op een nieuwer databaseschema. Zie [Deployment](help:deployment).
- Start geen tweede API-instantie om sneller te herstellen. Zie [Eén instantie](help:een-instantie).
- De onderhoudspagina van nginx blijft staan zolang `maintenance.on` bestaat.

## VEEL VOORKOMENDE PROBLEMEN

**Na een mislukte deploy blijft de onderhoudspagina staan.** Dat is de bedoeling. Lees de uitvoer van het deployscript, en volg `docs/delivery/operations.md` sectie 3.7.
