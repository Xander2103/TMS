---
id: geschiedenis-uitzonderingen
title: Afwijkingen en incidenten
category: Projectgeschiedenis
order: 4
---
## Probleem

Bewerkingen konden gegevens wissen en berichten bevatten onopgeloste `{{veld}}`-plaatshouders. Er was geen koppeling met het wagenpark en er bestonden meerdere manieren om te bepalen welke problemen bij een dossier horen. Het portaal toonde "Afgerond" bij niet-geleverde opdrachten. Incidenten die alleen aan een opdracht hingen, ontbraken in het dossier. De map met bewijs bevat alleen scripts en schermafbeeldingen; de inhoud hieronder komt uit `docs/problems.md`, de commits en de integratieruns.

## Risico

Gegevensverlies, chauffeurstekst die bij klanten terechtkomt, en dossiers die worden afgesloten terwijl er nog open problemen zijn.

## Oplossing

- Bewerken per sectie (`report`, `links`, `impact`, `responsibility`, `communication`).
- `ProblemScope` als enige bepaling van welke problemen bij een dossier horen.
- `ExceptionConfirmationPolicy`: Laag is informatie, Middel een waarschuwing, Hoog en Kritiek houden de automatische bevestiging tegen.
- Eén gekoppeld schaderapport per probleem. Een incident verandert de inzetbaarheid van een voertuig nooit rechtstreeks.
- `DeliveryOutcomeRules` voor het portaal en een apart "bericht voor de klant".
- Een aanrekening kan alleen beslist worden met `problems.approve_charge`; een herlevering alleen via het gecontroleerde `problems.create_redelivery`.
- Migratie `IncidentDossierScopeBackfill` (`13c680ff`) voor bestaande gegevens.

## Belangrijke architectuurbeslissing

Een probleem na afsluiting opent het dossier nooit opnieuw (`ProblemAfterConfirmation`). Een herlevering start zonder prijs of expliciet gratis, nooit als stille kopie van de facturatie. Foto's van gesloten afwijkingen blijven bewaard als bewijs.

## Uitgevoerde tests

| Test | Resultaat |
|---|---|
| Afwijking → incident → schade → wagenpark | 25/25 |
| s11 | 9/9 |
| Herlevering | 22/22 |
| Aanrekening | 10/10 |
| s16 en vervolg | 2/2 en 10/10 |
| Migraties | nieuw 190; upgrades 181→190 en 187→190 |
| Rolopwaardering 38→40 | herstart idempotent |

## Resultaat

De volgorde van de rolsjablonen ligt vast: v38 Kern, v39 Magazijn, v40 Uitzonderingen. Restpunt destijds: datums van herlevering en schade in UTC, later opgelost in `c550f80b`.

Commits: `2d96bc0d`, `52d906ed`, `42ca0710`, `13c680ff`, `d07335d4`.
