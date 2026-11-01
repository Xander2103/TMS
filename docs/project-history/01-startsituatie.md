---
id: geschiedenis-startsituatie
title: Startsituatie
category: Projectgeschiedenis
order: 1
---
## Probleem

Op 13 oktober 2026 werd `nav-redesign` (401 commits, gebouwd tussen 20 juli en 13 oktober) in `main` samengevoegd op commit `f17a0811`. Het platform was functioneel breed: klanten, personeel, wagenpark, opdrachten, planning, chauffeur, portaal, facturatie, magazijn. Niemand had het echter al beoordeeld als één samenhangend bedrijfssysteem.

Tussen 13 en 17 oktober volgden functionele golven: UX (B01–B55), HR (H1–H7, P2), Transport (T-H, T-M), Boekhouding (ACC-*) en Wagenpark (FL-*). De bevindingen daarvan zijn alleen via de commits bekend; losse rapporten bestaan niet meer.

Daarna kwamen drie alleen-lezen audits op dezelfde basis (`d07335d4`):

| Audit | Oordeel | Omvang |
|---|---|---|
| Prestaties | NOT READY FOR PILOT AT SCALE | 4 blokkers |
| Productiegereedheid | NOT PRODUCTION READY | 4 blokkers, 12 hoog |
| UI/UX | GOOD WITH IMPORTANT IMPROVEMENTS | 49 bevindingen: 1 blokker, 12 hoog, 28 middel, 8 laag |

De acceptatie van het klantportaal gaf "PASS WITH ISSUES". De klantscheiding hield wel al stand (24 pogingen met een rechtstreeks id en 18 lijstscans). Het stamgegevenslog bevatte 2 blokkers: uitgegeven CMR's en laadbrieven toonden live gegevens, en een klant met verwijzingen kon verwijderd worden.

## Risico

Juridische documenten konden achteraf veranderen en ex-medewerkers konden blijven inloggen. Een deploy kon alle opgeslagen bestanden wissen. De hoofdzoekbalk liep bij een realistische hoeveelheid gegevens vast op een time-out na 30 seconden.

## Oplossing

Geen herschrijving. Het project werkte in afgebakende remediatie-takken, elk met eigen bewijs, en voegde die telkens gecontroleerd samen.

## Belangrijke architectuurbeslissing

De bestaande basis bleef behouden: een modulaire .NET 10 API met PostgreSQL en een React-frontend. De globale tenantfilter (`ApplyGlobalTenantFilters`, met een architectuurtest die de build laat falen als een tenant-entiteit ongefilterd is) dateert al van de beveiligingssprint van juli en bleef het fundament van de tenantscheiding.

## Uitgevoerde tests

De drie audits zelf, plus de acceptatie van het klantportaal en het stamgegevenslog.

## Resultaat

Een vaste aanpak: eerst meten en vastleggen, dan herstellen, dan samenvoegen en opnieuw meten. De periode van `f17a0811` tot `be4e7cc9` omvat 258 commits (1458 bestanden, +417.184 / −11.983 regels).
