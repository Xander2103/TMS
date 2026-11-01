---
id: geschiedenis-factuurveiligheid
title: Factuurveiligheid
category: Projectgeschiedenis
order: 13
---
## Probleem

- R-N1: op een afgewerkte opdracht bood het prijspaneel keuzes aan die de server daarna weigerde, en een herlevering kon na uitvoering niet gratis verklaard worden.
- R-N2: een mislukte levering en haar herlevering stonden allebei op "Klaar" voor €300 en waren allebei vooraf geselecteerd.

## Risico

Eén levering twee keer factureren.

## Oplossing

`RedeliveryBillingRule` (`da50ded5`) leest de actuele feiten en wordt gebruikt door de factuurbouwer, Facturatiecontrole, het aanmaken van de factuur en de controle bij het definitief maken. Een mislukte levering en haar herlevering zijn samen één transport en worden één keer gefactureerd. Normaal draagt de oorspronkelijke opdracht de prijs.

| Situatie | Gevolg |
|---|---|
| Oorspronkelijke opdracht heeft een prijs | Herlevering op *Nakijken*, de server weigert ze |
| Herlevering bevestigd als gratis | Lijn van €0 naast de oorspronkelijke |
| Oorspronkelijke gratis of volledig gecrediteerd | Herlevering draagt de prijs |
| Oorspronkelijke zonder prijs | Herlevering met prijs is de enige aanrekening; de oorspronkelijke blijft op *Nakijken* |
| Herlevering al gefactureerd | Oorspronkelijke op *Nakijken* |
| Extra kost | Goedgekeurde incidentkost, aparte verkooplijn |

Gratis na uitvoering kan alleen binnen zo'n paar, met een reden en met `orders.override_price` of `orders.manage`. Dat kan nooit op een vergrendelde prijs, een gefactureerde opdracht of een opdracht met een goedgekeurde aanrekening. Een mislukte stop vraagt geen geweigerde POD meer zodra een afgewerkte herlevering de goederen heeft geleverd; er wordt geen POD verzonnen.

## Belangrijke architectuurbeslissing

De regel leest actuele feiten en bewaart niets: geen migratie nodig, en dezelfde waarheid op elke plaats. Een niet-afgesloten CSS-regel in `invoices.css` is bewust niet aangepast, omdat afsluiten slapende regels zou activeren.

## Uitgevoerde tests

Eindrun op `be4e7cc9`: 49/0, 5/0 en 2/0.

- €300 + €0 geeft €363 inclusief btw.
- Scenario C: €300 + €75.
- Nummers 1 tot 5 aansluitend.
- Tenant B krijgt 404, portaal 403.
- Een gebruiker zonder prijsrecht krijgt null.
- PO verplicht, en er wordt geen nummer verbruikt bij weigering.

Technische poorten: dotnet 3524/3524, vitest 2785/2785, 0 NU190x, EF schoon.

## Resultaat

De eindcommit `be4e7cc9` is de geaccepteerde broncode voor `main`.
