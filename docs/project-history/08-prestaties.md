---
id: geschiedenis-prestaties
title: Prestaties, belasting en gelijktijdigheid
category: Projectgeschiedenis
order: 8
---
## Probleem

Gemeten op ongeveer 32.000 dossiers en 211.000 auditregels:

- de hoofdzoekbalk gaf na 30 seconden een fout;
- een wettelijk factuurnummer ging verloren en er konden twee levende CMR's ontstaan;
- planning kostte 1.000 tot 10.000 SQL-opdrachten per verzoek;
- wijzigingen gingen ongemerkt verloren;
- auditantwoorden waren onbegrensd (97,6 MB).

De tijden zijn relatief: ze zijn gemeten op een belaste laptop met een lokale database, zoals het rapport zelf waarschuwt.

## Risico

Een systeem dat onder normale groei stilvalt, juridisch foute nummering, en gegevens die zonder melding overschreven worden.

## Oplossing

### Dossierzoekopdracht (`d17629d6`, `4a8d1c0d`)

De oorzaak was een gecorreleerde OR-van-EXISTS die per rij opnieuw liep. De oplossing bouwt eerst verzamelingen id's met UNION ALL en gebruikt die als `IN`.

| Vraag | Voor | Na |
|---|---|---|
| `search=antwerp` | fout na 30.017 ms | 185 tot 198 ms |
| `sort=planningdate` | fout na 30.019 ms | 49 ms |

### N+1 in de planning (`d94fa046`, `6103d9ab`, `04cef710`)

De conflictmotor `EvaluateManyAsync` werkt met één momentopname van de feiten.

| Verzoek | SQL-opdrachten voor → na |
|---|---|
| Planbord 3 dagen | 1.450 → 68 |
| Planbord 31 dagen | 9.280 → 68 |
| Ritten 14 dagen | 5.102 → 62 |
| Mijn ritten chauffeur | 206 → 20 |
| Dashboard | 416 → 134 |

### Gelijktijdigheid bij facturen (`3aff1684`, `abead311`)

`Invoice.Status` is een concurrency token, met `pg_advisory_xact_lock` per nummerreeks. Dezelfde factuur twee keer gaf eerst tweemaal 200 met een verloren nummer; nu 200 en 409 met behoud van het nummer. Acht verschillende concepten gaven eerst 1×200 en 7×400; nu 8×200 zonder gaten in de nummering.

### Gelijktijdigheid bij CMR's (`86811018`, `09b987c5`, `137edf69`)

Een gedeeltelijke unieke index `IX_issued_transport_documents_Live` en een vergrendeling per reeks. De migratie ruimde 3 echte dubbele paren levende CMR's op.

### Optimistische gelijktijdigheid op dossier en opdracht (`66c40810`)

`Version` is een concurrency token geworden. Eerst slaagden in 7 van de 10 rondes twee wijzigingen tegelijk; nu krijgt de tweede een 409, 0 van de 10 keer dubbel. Hetzelfde geldt voor `Incident.Version` (`ae7c0f0f`).

### Begrensde auditlog (`292f7165`)

Paginagrootte 1 tot 200, standaard 25, vaste volgorde. `pageSize=5000` gaf 5.000 rijen en 2,4 MB; nu 200 rijen en 91,4 KB.

### Overig

- Het overzicht van Live opvolging schrijft niet meer bij het lezen: van 1.045 opdrachten en 986 schrijfacties naar 40 en 0 (`148cad2f`, `ac1b839f`).
- Kiezers zoeken op de server (25 resultaten per zoekvraag).
- Het incidentenregister werkt met pagina's.
- Afgekapte resultaten worden gemeld met headers (`1fd65156`).
- Verzoeken hebben een deadline van 30 seconden, downloads 120 (`a4ac19a2`).

## Belangrijke architectuurbeslissing

Races worden opgelost in de database en niet in applicatiecode. Voor de zoekopdracht is geen index toegevoegd: `pg_trgm` is bewust uitgesteld, omdat de herschreven query volstond.

## Uitgevoerde tests

- Racematrix van 12/24 naar 19/24. De 5 overige zijn beleidskeuzes: dubbele boeking op concepten (2), verschillende incidentsecties (1), dubbel verzenden via de ruwe API zonder idempotentiesleutel (2).
- Tenant A/B op de geoptimaliseerde eindpunten: 21/21.
- Volledige regressie gelijk aan de basislijn van 19 oktober.
- Technische poorten: dotnet 3332/3332, vitest 2737/2737.

## Resultaat

Oordeel "PERFORMANCE READY FOR PILOT". De absolute capaciteit moet opnieuw gemeten worden op de echte pilotinfrastructuur. Restpunten: geen idempotentiesleutel bij aanmaken, de lijst met afwijkingen (280 opdrachten), `pg_trgm`, en de boekhoudexport die niet is gemeten.

De voor- en nameetwaarden verschillen per run en per testset. Vergelijk daarom cijfers alleen binnen één hoofdstuk.
