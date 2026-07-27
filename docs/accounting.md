# Boekhouding — grootboekrekeningen & verkoopcategorieën

> Corrections wave 2026-07-27 §7. Tests: `AccountingServiceTests`, `InvoiceLedgerSnapshotTests`,
> `DefaultRoleSeederTests.Version16…`.

## Masterdata (per tenant — nooit hardgecodeerd)

- **Grootboekrekening** (`ledger_accounts`): `AccountNumber` + `Name` verplicht, optionele
  externe code/omschrijving, `IsActive`. Uniek rekeningnummer per tenant onder de
  niet-verwijderde rijen. Elke onderneming configureert haar eigen nummers — bedrijf A koppelt
  Transport aan 700000, bedrijf B aan iets totaal anders; "Transport = 700000" bestaat nergens
  in code.
- **Verkoopcategorie** (`sales_categories`): code, naam, `SystemRole`
  (`None`/`Transport`/`Surcharge`/`Diesel` — max. één actieve categorie per rol) en de **live
  mapping** `LedgerAccountId`. Zes Nederlandse standaardcategorieën seeden lui per tenant
  (add-if-missing op code; verwijderde categorieën komen nooit terug): Transport, Supplementen,
  Diesel, Verkoop europallets, Diverse verkoop binnenland, Diverse verkoop buitenland.

Regels: een **inactieve** rekening kan niet meer nieuw worden toegewezen (bestaande mappings
blijven leesbaar); een rekening die door een categorie of een factuurlijnsnapshot gebruikt
wordt, kan niet worden verwijderd — wel gedeactiveerd. Alle mutaties worden geauditeerd; een
mappingwijziging expliciet met **oude rekening → nieuwe rekening**.

## Automatische categorisering van factuurlijnen

Bij het aanmaken van een factuur krijgt elke lijn haar categorie **structureel** via de
systeemrollen: de basistransportlijn per order → rol `Transport`; de dienst-/toeslaglijnen
(uit `TransportOrderServiceLine`) → rol `Surcharge`; de diesellijnen → rol `Diesel`. Handmatige
lijnen kiezen hun categorie zelf (conceptbewerking, kolom "Verkoopcategorie").

## Snapshot bij verzenden (§7.3)

Zolang de factuur **concept** is, toont de detailpagina de live categorie + de live gekoppelde
rekening en een duidelijke waarschuwing per lijn zonder categorie of zonder mapping
("Geen grootboekrekening ingesteld voor 'Diesel'. Configureer deze bij Bedrijfsinstellingen →
Boekhouding."). Concepten worden hierdoor **nooit geblokkeerd**.

Bij **Verzenden** bevriest `FreezeLedgerSnapshotsAsync` per lijn: categorienaam,
rekening-id/-nummer/-naam uit de op dát moment geldende mapping. Daarna is de snapshot de enige
waarheid: een latere mappingwijziging verandert een historische factuur nooit (getest).

## Boekhoudexport (§7.4)

`GET api/accounting/export?from=&to=` (knop **Boekhoudexport** op de facturenlijst) exporteert
alle **Verzonden/Betaalde** factuurlijnen met factuurdatum in de periode als XLSX — uitsluitend
uit de snapshots. Ontbreekt op één opgenomen lijn de vastgelegde rekening, dan wordt de export
**geblokkeerd** met de betrokken factuurnummers. Kolommen: Factuurnummer, Factuurdatum, Klant,
Omschrijving, Verkoopcategorie, Grootboekrekening, Rekeningnaam, Netto, Btw %, Btw-bedrag,
Bruto, Valuta.

`GET api/accounting/health` + de waarschuwingsbanner op de instellingenpagina tonen welke
actieve verkoopcategorieën nog geen rekening hebben (configuratiegezondheid).

## Permissies & isolatie

`accounting.view` (bekijken/exporteren) en `accounting.manage` (beheren). Standaardrollen:
Boekhouding krijgt beide, Management alleen view — role-upgrade **v16** voor bestaande tenants,
zonder hardgecodeerde rolnamen (templatecodes). Alle queries zijn expliciet tenant-gefilterd;
rekeningen, categorieën en exports lekken nooit tussen bedrijven (getest).

## Voorbeeld

Bedrijf A: Transport → 700000 Transportopbrengsten, Supplementen → 700100, Diesel → 700200,
Verkoop europallets → 700300, Diverse verkoop binnenland → 700400, buitenland → 700500.
Factuur verzonden in juli met mapping 700400; in augustus wijzigt de boekhouder de mapping naar
709999 → de julifactuur en -export blijven 700400 tonen; nieuwe facturen krijgen 709999.
