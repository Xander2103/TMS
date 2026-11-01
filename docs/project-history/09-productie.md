---
id: geschiedenis-productie
title: Productieverharding
category: Projectgeschiedenis
order: 9
---
## Probleem

- Een deploy kon alle opgeslagen bestanden wissen (B1).
- De automatische terugrol startte oude code op het nieuwe schema (B2).
- In productie was er geen manier om een tenant aan te maken (B3).
- Peppol "Live" deed alsof er verzonden was (B4).
- Daarnaast 12 punten met hoge prioriteit.

## Risico

Definitief verlies van documenten, beschadigde gegevens na een mislukte deploy, en facturen die als verzonden staan zonder ooit verzonden te zijn.

## Oplossing

### Storage:RootPath (`5ce8787d`, `dd66fdaf`, `2af1e9bf`)

Eén bepaling voor uploads, back-ups en de testmailmap. Buiten Development weigert de API te starten tenzij het pad absoluut, schrijfbaar en buiten de applicatiemap is. Controle: `--verify-storage`.

### Back-up en herstel (`ba8d45a1`, `216b8ccb`)

Een set is een dump, `files.tar.gz` en een sha256-manifest, en telt pas als voltooid na verificatie. Herstellen via de app geeft buiten Development een 409: herstel is een offline procedure volgens `docs/operations/backup-restore.md`. Encryptiesleutels zitten bewust niet in de back-up. Een maandelijkse hersteloefening is voorgeschreven.

### Gezondheidscontroles (`29f0c94c`)

- `/health/live`: alleen het proces.
- `/health/ready`: database, migraties, sleutelcontrole en opslag.
- `/health/details`: uitgebreid, met het recht `system_info.view`.

Een databasestoring geeft 503 met `Retry-After`.

### Deploys die alleen vooruit gaan (`704d5a5a`, `3b753482`, `169c7208`, `d41ede5a`, `93fca238`, `5085a632`)

Volgorde: bouwen in een schone omgeving → onderhoudsmodus en stoppen → laatste dump en opslagsnapshot, geverifieerd → migreren → starten → wachten op ready. Na de migratiestap worden nooit oude binaire bestanden gestart: de dienst blijft gestopt en de onderhoudspagina blijft staan.

### Peppol blokkeert bij twijfel (`0e1ff55b`, `316a402d`)

Live vereist een provider die `SupportsLiveNetwork` opgeeft, en de sandbox doet dat nooit. Rijen op Live worden bij het opstarten kritiek gelogd en nooit verzonden of als geleverd gemarkeerd. De UI toont "Live-provider niet geconfigureerd".

### Overig

- Tenant aanmaken met `--provision-tenant` (`ec32ac12`): idempotent, beheerder zonder wachtwoord met activatiemail, geen demogegevens.
- Mailwachtrij met at-most-once verzending en quarantaine (`6dd58923`).
- Sleutelcontrole, vergrendelingen op achtergrondtaken, reset-mail via de wachtrij (`0341f0f2`).
- `Pkcs` vastgezet op 10.0.13 (`eedd91a5`).

## Belangrijke architectuurbeslissing

Alleen vooruit, en terugrollen via een herstel. Herstellen is een offline procedure van de beheerder. De pilot draait op exact één instantie. Eerlijk uitgeschakeld is beter dan schijnsucces: dat geldt voor agenda, sms en Peppol.

## Uitgevoerde tests

- Deploysimulatie: 19 scenario's, 214 controles, alle geslaagd.
- Nieuwe installatie: 193 migraties in 17 seconden.
- Upgrade: 35 bestanden met gelijke checksums.
- Herstel naar een nieuwe cluster: 7/7 en 6/6; een verkeerde sleutel wordt geweigerd en downloads zijn byte-identiek.
- Technische poorten op `1af4de7c`: dotnet 3408/3408, vitest 2714/2714, geen kwetsbare pakketten.

## Resultaat

Restpunten: geen schaalvergroting, geen echte Peppol Access Point, agenda- of sms-provider, geen malwarescan op uploads, IBAN van medewerkers leesbaar in de database, medische bestanden onversleuteld op schijf, en activatielinks leesbaar in de wachtrij tot ze worden gewist. `docs/security/operational-checklist.md` bevat de organisatie- en infrastructuurpunten die uitdrukkelijk nog niet als opgelost staan.
