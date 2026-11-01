---
id: deployment
title: Deployment en databasemigraties
category: Beheer
roles: admin
keywords: deployment, deploy, migraties, forward-only, rollback, onderhoud, nieuwe versie, script
related: back-up, productie-configuratie, storingen
---
## WAT DOE JE HIER?

Je installeert een nieuwe versie.

## WANNEER GEBRUIK JE DIT?

Bij elke nieuwe versie. Plan dit buiten de werkuren: de site is tijdens het migreren niet beschikbaar.

## STAPPEN

1. Maak een back-up en controleer ze.
2. Voer op de server uit:

```
sudo /usr/local/bin/deploy-transportationservice.sh <commit-sha>
```

3. Het script doet 17 stappen: bouwen in een schone omgeving, onderhoudspagina aan, service stoppen, laatste dump en opslagsnapshot, migreren, starten, wachten op `/health/ready`, en onderhoudspagina uit.
4. Controleer daarna `/health/ready`, **Systeeminformatie** en een aanmelding.

## WAT GEBEURT ER DAARNA?

De nieuwe versie draait. Migraties zijn toegepast terwijl de applicatie stilstond. Ze lopen nooit automatisch bij het opstarten.

## WAAR MOET JE OP LETTEN?

**Alleen vooruit.** Migraties kun je niet terugdraaien. Terugrollen betekent: de back-up terugzetten.

- Mislukt iets vóór de migratiestap, dan blijft de vorige versie.
- Mislukt iets ná de migratiestap, dan blijft de service gestopt en de onderhoudspagina staan. Het script start bewust geen oude code op het nieuwe schema.

Lees `docs/delivery/operations.md`, secties 3.3 tot 3.8.

## VEEL VOORKOMENDE PROBLEMEN

**Het script weigert te starten.** Het controleert vooraf onder andere de opslagmap en de instellingen. De reden staat in de uitvoer.
