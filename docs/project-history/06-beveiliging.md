---
id: geschiedenis-beveiliging
title: Beveiliging, rollen en tenantscheiding
category: Projectgeschiedenis
order: 6
---
## Probleem

- Een chauffeur kon de ETA en vertraging van de rit van een andere chauffeur lezen en wijzigen (SEC-H1).
- De Dispatcher kreeg prijzen te zien (SEC-M1).
- Het eigen wachtwoord wijzigen liet andere sessies open (SEC-M2).
- Een toegangstoken bleef geldig na het afmelden (SEC-M3).
- Op afgeschermde routes verschenen ruwe foutmeldingen of eindeloze herhalingen (SEC-M5).

## Risico

Toegang tot en wijziging van gegevens van anderen, lekken van financiële gegevens, en sessies die niet te beëindigen zijn.

## Oplossing

### DriverTripScope (`796dc13d`)

`DriverIdentity.ResolveAsync` vervangt zeven afzonderlijke kopieën. Met een kantoorrecht is elke rit van de tenant bereikbaar. Zonder dat recht alleen een rit van het eigen actieve chauffeursprofiel. De rit van een andere chauffeur is niet te onderscheiden van een onbekend id (404), en dat wordt gecontroleerd vóór de validatie van de inhoud.

### Afscherming van financiële velden (`2260c481`, `866876bc`, `de322920`)

`CommercialVisibility` bepaalt wie prijzen mag zien: wie minstens één commercieel recht heeft. Bij twijfel gaat het veld dicht. Zonder dat recht verdwijnen prijzen van opdrachten en dossiers, lijnen, snapshots, factuurkoppelingen, het totaal en de factuurstatus in de dossierlijst, prijsgebeurtenissen in de tijdlijn en klanttarieven. Omzet en openstaande bedragen op het dashboard vragen `invoices.view`. Een bewuste uitzondering: houders van `incidents.manage` zien incidentkosten.

### Sessies ongeldig maken (`d5d65638`)

Toegangstokens (60 minuten) dragen `security_stamp` en `session_id`. `AccountStateAuthorizationFilter` controleert dit bij elk verzoek in één query, zonder zwarte lijst.

| Gebeurtenis | Gevolg |
|---|---|
| Afmelden | meteen 401, andere sessies blijven |
| Wachtwoord wijzigen of deactiveren | alle sessies eindigen |
| Rol wijzigen | rechten wijzigen bij het volgende verzoek, de sessie blijft |
| Oud verversingstoken hergebruiken | alle sessies eindigen |

### Tenantscheiding

Bovenop de globale filter zijn tests toegevoegd in beide richtingen tussen tenant A en B: 12 entiteitstypes geven 404 op id, 9 lijsten bevatten geen vreemde rijen, schrijven met vreemde id's wordt geweigerd en de auditlog blijft schoon.

### Klant A en B

De lijsten zijn volledig gescheiden, een id van de andere klant geeft 404 en een portaalgebruiker krijgt 403 op interne API's.

## Belangrijke architectuurbeslissing

Een rit van iemand anders is "niet gevonden" en niet "geen toegang": zo verraadt het antwoord niet dat de rit bestaat. De rolsjablonen bleven op v40, want er werd geen enkel recht gewijzigd om deze fouten op te lossen.

## Uitgevoerde tests

| Reeks | Resultaat |
|---|---|
| Probes (21 lezen en 3 schrijven × 11 gebruikers) | 381 geslaagd, 0 mislukt |
| Sessies | 36/36 |
| Offboarding | 40/40 |
| Browser | 47/48; de ene mislukking bleek een bestaande basislijnfout, eindrun 48/48 |
| Toekomstig einde | 7/7 |
| Pakketbereik | 5/5 |

Technische poorten: dotnet 3258/3258 (eerste run 1 mislukking in `IssuedItemReturnTests`), vitest 2701/2701.

## Resultaat

Commits: `796dc13d`, `2260c481`, `d5d65638`, `41780e4d`, `b1d42fd1`, `e356a783`, `a703f702`, `cf11da4c`.
