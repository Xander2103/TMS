---
id: geschiedenis-portaal
title: Klantportaal
category: Projectgeschiedenis
order: 5
---
## Probleem

- "Terug naar concept" stuurde een mail "geweigerd" (KP-01).
- Een POD toevoegen was onmogelijk zodra de laatste stop de rit had afgerond (KP-02).
- De dashboard-API gaf facturen terug zonder het recht daarop (KP-04).
- Uitgegeven CMR's en laadbrieven bereikten het portaal nooit (KP-06).
- 40 pixels horizontale overloop op 390 pixels (KP-07).
- Als gelezen markeren gaf een 500 (KP-10).

## Risico

Misleide klanten, ontbrekende juridische documenten en gegevens die zichtbaar zijn zonder recht.

## Oplossing

- `PortalOrderStatusRules.ProjectAsync` is de enige klantstatus aan de serverkant. Er gaat geen interne statuscode over de lijn.
- Eén zichtbaarheidsregel: interne concepten antwoorden 404, net als opdrachten van een andere klant.
- Een volledige heen-en-terugronde voor informatievragen.
- Publicatie van uitgegeven documenten: alleen actieve, met bevroren snapshot; overgenomen bij vervanging, ingetrokken bij wijziging van klant.
- POD ook na afronding van de rit.

## Belangrijke architectuurbeslissing

Het portaal heeft een eigen weergave en niet de interne status. Facturatie is een aparte vlag, `InvoiceAvailable`.

## Uitgevoerde tests

| Fase | Resultaat |
|---|---|
| Aanvraag | 13/13 |
| Beoordeling | 14/15; de mislukte controle herhaald en geslaagd, de oorzaak was het script |
| Uitvoering | 21/24; de 3 mislukkingen zochten de publicatieknop op de verkeerde pagina, herhaald 5/5 |
| Mislukte levering | 13/13 |
| **Klantscheiding A/B** | **42/42** |
| Laatste fase | 19/19 |

Technische poorten: dotnet 3255/3255, vitest 2590/2590, EF schoon.

## Resultaat

Migratie `CustomerPortalFinalRemediation` (191 in totaal). Klant A en klant B zien alleen hun eigen gegevens. Commits: `63703163`, `034b58b9`, `cd753f36`, `600f252c`.
