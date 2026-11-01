---
id: geschiedenis-bedrijfsdag
title: Real Company Day
category: Projectgeschiedenis
order: 12
---
## Probleem

Werkt een volledige bedrijfsdag van begin tot einde, in productiemodus, in een echte browser en met nieuw aangemaakte tenants?

## Risico

Elke module afzonderlijk groen, terwijl de overgangen tussen rollen toch breken.

## Oplossing

Een geïsoleerde stack: productiemodus, productiebuild en een nieuwe database met 195 migraties en 2 tenants via `--provision-tenant`, zonder ontwikkelseeders. Een tweede database met 33.000 dossiers diende voor de upgrade naar v41 en de prestatiecontrole.

## Belangrijke architectuurbeslissing

Alles liep met testgegevens, en de instelling die via API of SQL gebeurde, is apart vastgelegd zodat duidelijk blijft wat een echte bedrijfsflow was.

## Uitgevoerde tests

- **v41-upgrade op 3 tenants:** precies 6 rijen verwijderd; identiek na herstart; een hertoekenning door de beheerder bleef behouden.
- **Landingen:** alle tien rollen correct in de browser.
- **Portaal:** een echte dubbelklik gaf één opdracht.
- **Planning:** geblokkeerd voertuig geweigerd, ook de override door de planner (403).
- **Gelijktijdigheid:** dezelfde dossierversie gaf één 200 en één 409.
- **Chauffeur:** 21/21 op 390 pixels, alleen de eigen rit.
- **Mislukte levering:** 21/21; blijft staan tot het incident is opgelost; chauffeurstekst nooit in het portaal.
- **Wagenpark:** "Niet inzetbaar" stemt overeen op inzetbaarheid, voertuigpagina, lijst, dashboardtelling en kiezer.
- **Offboarding:** oud token 401, open sessie terug naar aanmelden, nieuwe login 401, voertuig vrij, rit houdt zijn chauffeur met een blokkerend conflict, PDA blijft uitgegeven.
- **Boekhouding:** FAC-2026100001/2/3 aansluitend, €480 + 21% = €580,80, PDF byte-identiek na wijziging van de klant.
- **CMR:** 3 rondes van 12: 1×200 en 11×409.
- **Beveiliging:** 128 controles geslaagd, 1 verkeerde verwachting.
- **Storing:** database gestopt: live 200; ready, lezen, schrijven en aanmelden 503; 0 rijen geschreven; herstel zonder herstart.

| Prestaties (33.000 dossiers) | Mediaan | SQL-opdrachten |
|---|---|---|
| Dossiers zoeken | 38 ms | 17 |
| Vrije tekst, 9.251 resultaten | 136 ms | 17 |
| Planbord 3 dagen | 20 ms | 26 |
| Dashboard | 34 ms | 59 |

Technische poorten: dotnet 3492/3492, vitest 2778/2778, 195 migraties, v41. Logs: 327 geslaagd, 12 mislukt (9 scriptfouten, 3 echte bevindingen R-N1).

Niet geverifieerd: echte mailaflevering (27 berichten in de wachtrij, geen TLS-SMTP op de testomgeving) en het verschil tussen lokale datum en UTC (de run liep om 10:30; dit rust op de automatische tests).

## Resultaat

Oordeel "SYSTEM READY FOR PILOT / MAIN MERGE", met twee nieuwe bevindingen van middelmatig belang, R-N1 en R-N2, die eerst opgelost moesten worden.
