---
id: geschiedenis-kern
title: Kern en stamgegevens
category: Projectgeschiedenis
order: 2
---
## Probleem

- Uitgegeven CMR's en laadbrieven toonden live gegevens: naam, btw-nummer, nummerplaat of eenheid veranderden bij een nieuwe download.
- De UBL van een uitgegeven factuur nam latere klantgegevens over.
- Een klant met facturen kon verwijderd worden, en het nummer werd meteen hergebruikt.
- Medewerkers uit dienst konden nog inloggen.
- Gelijktijdig aanmaken gaf 1×201 en 4×500. Een teller verlagen gaf een 500. E-mailadressen konden dubbel bestaan met een ander hoofdlettergebruik. Een inline bewerking van de grootboekrekening wiste velden. Wijzigingen aan klanten gingen ongemerkt verloren.

## Risico

Juridische documenten die achteraf veranderen, verlies van historiek, ex-medewerkers met toegang en mislukte nummering.

## Oplossing

- **Onveranderlijke documenten:** een `SnapshotJson` wordt bevroren in dezelfde opslag als de nummertoekenning (`0fc32f45`).
- **Verwijzingen:** een klant met verwijzingen krijgt een 409 `common.record_referenced`, met deactiveren als uitweg (`3e350d5c`).
- **Factuurgegevens:** snapshot van de partijen, vaste voorrang voor grootboekrekeningen, versies op klant, juridische entiteit en verkoopcategorie.
- **Nummering:** één allocator met een `SELECT … FOR UPDATE` rijvergrendeling (`49a8cd4a`).

### Uitdiensttreding (offboarding)

Eén orkestrator, `EmployeeOffboardingService`, wordt door elk pad gebruikt: expliciete deactivering, einde via bewerking (`TerminatedByEdit`), verstreken einddatum (`EmploymentEndDatePassed`) en resterende toegang (`ResidualAccess`, controle elke 15 minuten).

In één opslag gebeurt het volgende:

- de gebruiker wordt inactief en tokens, koppelingen en de beveiligingsstempel worden ingetrokken;
- de chauffeur wordt uitgeschakeld en vast of huidig voertuig en vaste oplegger komen vrij;
- de aanwezigheid wordt afgesloten;
- komende ritten worden geteld in de audit, en niet stilzwijgend losgekoppeld.

De controle zit in lagen: bij het aanmelden, bij het verversen van de sessie en bij elk verzoek (`86ae768c`, `1754c0ba`). De medewerker heractiveren heractiveert de login niet. De laatste beheerder wordt nooit buitengesloten.

## Belangrijke architectuurbeslissing

Een snapshot is de waarheid, en een leeg veld betekent "leeg op het moment van uitgifte". Oude rijen werden eenmalig bevroren bij de deployment (`SnapshotIsBackfilled`). Records met verwijzingen worden gedeactiveerd, nooit verwijderd. De chauffeursrol uitschakelen is geen offboarding.

## Uitgevoerde tests

| Test | Resultaat |
|---|---|
| API-flows (`flows-api.txt`) | 31 geslaagd, 2 mislukt in de PO-flow. `flowI-api.txt` toont die flow werkend; de bronnen verklaren de 2 regels niet |
| Vervolg H6, flows A–F | 46/46 |
| Vervolg F/G/H/J | 24/24 |
| Parallelle nummering vóór de vergrendeling | gemengde ronde: 7×409 en 4×500 |
| Parallelle nummering na de vergrendeling | 20× per allocator en 40 gemengd: alles 2xx en uniek, 0×5xx |
| UBL voor en na | identiek |

## Resultaat

190 migraties, rolsjablonen v38. Integratiesmokes 15/15, met CMR en UBL byte-identiek. De totalen van de technische poorten van deze tak zijn niet in de bewijsmap bewaard.

Belangrijkste commits: `0fc32f45`, `3e350d5c`, `86ae768c`, `5289093c`, `1754c0ba`, `abb989e5`, `8c47c7ed`, `49a8cd4a`, `44e3d1c1`, `a7e23e5a`.
