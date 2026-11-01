---
id: geschiedenis-consolidatie
title: Eindconsolidatie op main
category: Projectgeschiedenis
order: 14
---
## Probleem

Na alle rondes stond het geaccepteerde systeem op de tak `integration/final-system-acceptance`. Daarnaast bestonden meer dan 35 tijdelijke worktrees, tientallen takken, archieven en een aparte bewijsmap.

## Risico

Niemand weet nog welke map de juiste is, en bij het opruimen kan unieke code verdwijnen.

## Oplossing

- `main` is met een fast-forward op de geaccepteerde commit `be4e7cc9` gezet: de boom is identiek, plus één commit die `.local/` negeert.
- Alle referenties zijn eerst in een Git-bundle bewaard en er is een veiligheidstag `pre-cleanup-final-20261031` gepusht.
- Elke tak is per commit onderzocht: per patch en daarna regel voor regel tegen `main`.
- Documentatie, schermafbeeldingen en een helpcentrum zijn toegevoegd.

## Belangrijke architectuurbeslissing

Eén bron voor de handleidingen: Markdown in `docs/` voedt zowel de PDF's als het helpcentrum in de applicatie. Geen database, geen CMS en geen migratie. Lokaal bewijs staat in `.local/` en wordt nooit gecommit.

## Uitgevoerde tests

Op `main` vóór het pushen: dotnet 3524/3524, vitest 2785/2785, TypeScript en ESLint schoon, build groen, EF zonder openstaande wijzigingen, 195 migraties met Designer, rolsjabloon v41, geen NU1902 of NU1903. Daarbovenop een rooktest op een productiebuild: factuurveiligheid 49/0, 5/0 en 2/0; isolatie 114 geslaagd plus één gedocumenteerde verkeerde verwachting; landingen, zoeken, planbord, CMR, magazijn, offboarding, portaal en gezondheid.

## Resultaat

Eén project in `~/Documents/TMS`, met `main` als canonieke tak. Het eindresultaat en de restpunten staan in `docs/technical/known-issues.md`.
