---
id: geschiedenis-integratie-portaal
title: Integratie van portaal en beveiliging
category: Projectgeschiedenis
order: 7
---
## Probleem

Twee takken vanaf dezelfde basis `d07335d4` raakten overlappende bestanden.

## Risico

Een inhoudelijk verkeerde samenvoeging kon bewakingen of de afscherming van velden ongedaan maken, zonder dat Git een conflict meldt.

## Oplossing

De commits zijn één voor één overgezet. Er waren 2 tekstuele conflicten (dezelfde tests rond middernacht); de portaalversie bleef behouden en 2 identieke beveiligingstestcommits vervielen. Daarna volgde een inhoudelijke controle van `AppRoutes.tsx`, `LoginPage.tsx`, `TransportOrderService`, `TransportOrdersController`, `CustomerPortalOrderDetailPage` en `TripExecutionPage`.

Extra herstel: datums van herlevering en schade volgen de lokale tijd van de tenant (`c550f80b`), getest op zomer- en wintertijd.

## Belangrijke architectuurbeslissing

Nieuwe eindpunten voor medewerkers geven geen bedragen terug en beoordelingsantwoorden gebruiken de afgeschermde weergave. De terugkeerpagina na het aanmelden komt uit de router en nooit uit een URL-parameter. `PortalDashboardService` en de `DashboardService` voor medewerkers blijven gescheiden.

## Uitgevoerde tests

- 191 migraties, zowel nieuw als upgrade; rolsjablonen v40; herstart zonder dubbele rechten.
- Probes 381/382 (de mislukte controle hing af van de testgegevens), sessies 36, offboarding 40, sessie-UX 31, browser 47/48, klantscheiding 42/42.
- Regressie: s08 30, s09 22, s10 25, s11 9, s12 42, s14 15, s15 11, s16 10.
- Technische poorten op `c550f80b`: dotnet 3272/3272, vitest 2708/2708.

Een openheid over het bewijs: de latere prestatieregressie overschreef 76 schermafbeeldingen in de map van deze ronde. De geslaagd/mislukt-logs bleven ongemoeid.

## Resultaat

Beide takken samen op `ad405ac2`. Restpunten destijds: de kwetsbaarheidsmelding op `Pkcs` (later opgelost) en UX-002 en UX-003 (opgelost in UX-1).
