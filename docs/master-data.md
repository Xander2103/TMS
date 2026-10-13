# Master data: klanten, contactpersonen, locaties en medewerkers

Stand: 2026-08-05 (master-data stabilisatiewave, branch nav-redesign). Dit document beschrijft het
datamodel en de gedragsregels voor de kern-stamgegevens en de bijhorende invoerflows.

## Klant

- `Customer` (module Partners): één inline hoofdadres (straat/nr/postcode/gemeente/land), algemene
  contactgegevens (algemeen e-mailadres, algemeen telefoonnummer, website) en een **interne
  klantmemo** (`Notes`, alleen zichtbaar voor interne gebruikers).
- **Minimale aanmaak**: alleen de naam is verplicht. Klantnummer wordt automatisch toegekend
  (TenantSettings-prefix, bv. `KL-0001`); een expliciet nummer mag maar moet uniek zijn per tenant.
  BTW-nummer, ondernemingsnummer, bank, Peppol, facturatie-instellingen: allemaal optioneel; formaat
  wordt alléén gevalideerd wanneer een waarde is ingevuld.
- Create is transactioneel: klant + contactpersonen + nummerclaim + audittrail committen samen.
- `PurchaseOrderRequired` (bool, klantformulier) en `PurchaseOrderPolicy` (enum, facturatiepaneel)
  blijven gesynchroniseerd: bool→true zet het beleid op `Required`; bool→false zet `Required` terug
  naar `None` maar laat `Optional` ongemoeid.

### Algemene contactgegevens vs. contactpersonen

"Algemene contactgegevens" (e-mail/telefoon/website/memo) zijn bedrijfsgegevens en staan los van
persoonsrecords. Contactpersonen zijn aparte `CustomerContact`-records.

### Contactpersonen

- Velden: voornaam*, achternaam*, weergavenaam, roepnaam, functie (vrije tekst), afdeling (lookup),
  e-mail, telefoon, GSM, voorkeurstaal, type, primair, actief, notities.
- **Type** (`CustomerContactType`): Algemeen (default), Planning, Facturatie, Magazijn, Directie,
  Operationeel, Overig.
- **Primair per type**: per klant maximaal één primaire contactpersoon per type (gefilterde unieke
  index; promotie degradeert alleen binnen hetzelfde type).
- Meerdere contactpersonen kunnen al bij het aanmaken van de klant meegegeven worden
  (`CreateCustomerRequest.Contacts[]`; het oudere `InitialContact` blijft werken).
- **Verwijderen**: een contactpersoon waarnaar verwezen wordt (bv. communicatievoorkeuren) kan niet
  hard verwijderd worden — deactiveer in dat geval. Alle acties worden geauditeerd met leesbare
  oude/nieuwe waarden.

## Locaties & adressen

Klantlocaties zijn `Location`-records (module Locations) met `CustomerId`; er is bewust géén apart
"klantadres"-entiteit. Types: Bedrijfssite, Depot, Magazijn, Klantlocatie, Terminal, Laadadres,
Losadres, Parking, Kantoor, Maatschappelijke zetel, Administratief adres, Facturatieadres,
Retouradres, **Werf**, **Tijdelijke locatie**, **Overig**.

- **Naam is het enige verplichte veld.** Code wordt automatisch toegekend (`LOC-XXXXXXXX`) wanneer
  leeg. Adres, coördinaten, contact en alle operationele velden zijn optioneel.
- Operationele contactpersoon per locatie: naam/telefoon/GSM/e-mail, optioneel gekoppeld aan een
  bestaande klant-contactpersoon (`CustomerContactId`, zelfde tenant én zelfde klant).
- **Toegang & operationele informatie**: poort, toegangscode, aanmeldpunt, kade/dok,
  routebeschrijving, afspraak verplicht, leveren enkel op afspraak, hoogte-/gewichtsbeperking,
  voertuigbeperkingen, ADR toegelaten (ja/nee/onbekend), kraan vereist, heftruck beschikbaar.
- **Instructies**: laad-, los-, toegangs- en chauffeursinstructies + interne memo (alleen intern;
  nooit in klantportaal of klantcommunicatie).
- **Planningsstandaarden**: standaard laad-/lostijd (minuten), voorkeursvenster, vroegste/laatste
  aankomst — defaults die naar orderstops gekopieerd worden, geen harde regels.

### Gevoelige toegangsgegevens

De toegangscode is gevoelig: permissie `locations.view_sensitive` (rolsjabloon v25: planner,
dispatcher, management) is vereist om ze te zien; zonder die permissie blijft een bestaande code bij
bewerken behouden. In de audittrail verschijnt alleen `•••`, nooit de code zelf. Chauffeurs op de rit
zien de code wél in de rituitvoering (operationeel noodzakelijk); het klantportaal nooit.

### Openingsuren

Gestructureerde openingsuren per locatie: `location_opening_intervals` (weekdag 1=ma..7=zo, van/tot,
optionele notitie). Meerdere blokken per dag zijn mogelijk (07:00–12:00 + 13:00–17:00); een dag
zonder blokken is gesloten. Validatie: eindtijd na starttijd, geen overlappende blokken per dag.
Het oude vrije-tekstveld blijft bestaan als fallbackweergave. Bij update vervangt de meegegeven
lijst de bestaande blokken volledig (`openingIntervals: null` wist ze dus ook — de UI stuurt altijd
de volledige lijst mee).

`IOpeningHoursEvaluator` beoordeelt een tijdstip tegen de blokken: `Inside`, `BeforeOpening`,
`AfterClosing`, `ClosedDay` of `NoData`.

### Verwijderen vs. deactiveren

Een locatie die al gebruikt is (orderstops, magazijnen, EDI-koppelingen) kan niet verwijderd worden:
"Deze locatie is al gebruikt en kan niet worden verwijderd. Je kunt de locatie wel deactiveren."
Inactieve locaties verschijnen niet meer in de locatiekeuze op nieuwe orders, blijven zichtbaar op
historische orders en zijn via de filter "toon inactieve" terug te vinden.

## Orderstops: snapshotgedrag

Bij het kiezen van een klantlocatie op een stop worden de relevante gegevens **gekopieerd** naar de
stop (naam, adres, contact, openingsuren-samenvatting, poort/toegangscode/kade, instructies,
afspraak verplicht, standaard laad-/lostijden). Latere wijzigingen aan de locatie wijzigen
historische orders dus niet. De stop-UI toont "Overgenomen van klantlocatie"; met "Opnieuw overnemen
van locatie" worden de actuele stamgegevens bewust opnieuw gekopieerd (met waarschuwing dat lokale
aanpassingen vervallen; geauditeerd). Waarschuwingen voor geplande tijden buiten de openingsuren
zijn adviserend en blokkeren niets.

## Medewerkers

- **Minimale aanmaak**: alleen voornaam + achternaam. Personeelsnummer automatisch. E-mail, telefoon,
  adres, geboortedatum en startdatum zijn optioneel; e-mailformaat wordt alleen gecontroleerd
  wanneer ingevuld; einddatum moet na startdatum liggen wanneer beide ingevuld zijn.
- **Chauffeur**: chauffeursvelden verschijnen alleen wanneer de medewerker als chauffeur wordt
  gemarkeerd (of een CHAUF*-functie krijgt). Geen enkel certificaat is verplicht bij aanmaak —
  rijbewijzen, code 95, ADR, medische schifting enz. zijn kwalificatierecords die de
  inzetbaarheids-/gereedheidsscore voeden, geen blokkades. Chauffeurscategorieën zijn ook na
  aanmaak te bewerken (eerste = primair).
- **Notities**: meerdere notities per medewerker (`EmployeeNote`): tekst, aangemaakt/gewijzigd
  door+op, vastpinnen op het startscherm, verwijderen met bevestiging. Permissies:
  `employee_notes.view` / `manage` / `pin`. Het oude enkelvoudige notitieveld is alleen-lezen
  historiek en wordt niet meer beschreven.
- **Historiek**: leesbare wijzigingsgeschiedenis (veld, oude/nieuwe waarde, wie, wanneer) per
  categorie; vertrouwelijke waarden gemaskeerd; geen ruwe id's.

## Historiek klanten

`GET /api/customers/{id}/history` (permissie `customers.view`) projecteert de audittrail naar
leesbare Nederlandse entries met categorieën Klant / Contactpersonen / Locaties / Facturatie /
Communicatie, inclusief oude→nieuwe waarden en actor. IBAN en toegangscodes zijn al bij het
schrijven gemaskeerd.

## Demo-testdata (alleen development)

```
dotnet run --project TransportationService.Api -- --seed-demo
```

Draait alleen in Development, is idempotent (slaat over zodra `DEMO-*`-klanten bestaan) en maakt in
de dev-tenant: 5 fictieve klanten (met contactpersonen van meerdere types, 3–5 locaties incl.
openingsuren, poort/kade/instructies en één inactieve locatie) en 10 medewerkers (chauffeurs,
magazijn, planning, management; meerdere notities; enkele inactief).

## Permissies (relevant)

| Actie | Permissie |
|---|---|
| Klanten bekijken/aanmaken/bewerken/deactiveren | `customers.view` / `create` / `edit` / `deactivate` |
| Contactpersonen beheren | `customers.edit` |
| Klanthistoriek | `customers.view` |
| Locaties bekijken/aanmaken/bewerken/verwijderen | `locations.view` / `create` / `edit` / `delete` |
| Toegangscodes locaties | `locations.view_sensitive` (v25) |
| Medewerkers | `employees.view` / `create` / `edit` / `deactivate` |
| Notities | `employee_notes.view` / `manage` / `pin` |

Tenantisolatie geldt overal: het globale queryfilter plus expliciete `Ensure…InTenantAsync`-controles
op geneste verwijzingen (categorie, afdeling, contactpersoon, locatie).

## Handmatige rooktest (checklist)

1. **Klant A** — maak "Testklant Transport NV" met taal/telefoon/e-mail, 3 contactpersonen
   (Jan Peeters — Planning primair, Sofie Janssens — Facturatie primair, Marc De Smet — Magazijn) en
   4 locaties (Hoofdzetel Brussel, Magazijn Antwerpen met contact/openingsuren/poort 2/toegangscode/
   kade 4/afspraak verplicht/instructies/interne memo, Leveradres Gent, Werf Mechelen). Opslaan,
   heropenen, elk veld controleren. Wijzig daarna Jans telefoon, Sofies e-mail, de openingsuren en
   poort van het magazijn en één instructie; opnieuw opslaan en heropenen: geen dataverlies, geen
   dubbele contacten/locaties, historiek toont de wijzigingen.
2. **Klant B** — maak een klant met alléén een naam. Moet lukken; heropenen werkt.
3. **Medewerker A** — niet-chauffeur met naam/telefoon/e-mail/functie + notitie; opslaan, heropenen,
   telefoon en status wijzigen; historiek controleren.
4. **Medewerker B** — chauffeur met minimale gegevens + notitie; geen certificaat vereist.
5. **Ordercontrole** — order voor Testklant Transport NV met Magazijn Antwerpen als stop: adres,
   contact, telefoon, openingsuren, poort/kade/instructies gekopieerd. Wijzig daarna het telefoonnummer
   op de locatie; heropen het order: de stop toont nog de oorspronkelijke snapshot.

## Bekende beperkingen

- Openingsuren kennen geen feestdag-/uitzonderingsregeling (bewust buiten scope).
- Locatie-openingsblokken worden bij update integraal vervangen (geen individuele blok-historiek);
  wijzigingen zijn wel als samenvatting in de audit terug te vinden.
- De openingsuren-waarschuwing op orders evalueert de actuele stamgegevens (advies), niet de
  snapshot.
