# Rapportage- en documentgeneratiecontract

Status: vastgelegd contract (fase 12 van de verbeteringsgolf 2026-07-20). De bestaande
acht XLSX-rapporten en de labelrendering volgen dit al; nieuwe rapporten en documenten
moeten het volgen. Het rapportcentrum (zie onderaan) is inmiddels geïmplementeerd.

## Rapporten (lees-only exports)

Elk rapport is een endpoint onder de module die de data bezit (niet in een centrale
"reports"-module) en voldoet aan:

1. **Permissie**: een expliciete `*_reports.export`- of `*.export`-permissie via
   `RequirePermission`; nooit alleen UI-verbergen.
2. **Tenant-scoping**: elke query filtert op `TenantId` uit `ITenantContext` — geen
   client-supplied tenant of klant-id's (klantcontext komt uit de ingelogde gebruiker).
3. **Read-model**: rapporten lezen via `AsNoTracking()` en muteren nooit; zware aggregaties
   krijgen een eigen read-modelservice (patroon: KPI-module).
4. **Formaat**: XLSX via de bestaande ClosedXML-helpers (of CSV voor platte lijsten);
   bestandsnaam `snake-case-onderwerp-{yyyyMMdd}.xlsx`; bedragen als getallen (geen
   strings), datums als echte datumcellen.
5. **Auditing**: exports van persoons- of financiële gegevens schrijven een auditregel
   ("Exported", met filters in de payload — nooit de data zelf).
6. **FE-ontsluiting**: download via `fetch` met Bearer-token en blob-download (patroon:
   KPI-export / ICS-export), met bezig-status en toastfout.

## Documentgeneratie (records die een document opleveren)

Documenten (labels, vrachtbrieven, POD-PDF's, facturen) volgen het snapshotpatroon van
`PackageLabel`/`LabelSnapshot`:

1. **Snapshot bij generatie**: het document rendert uit een opgeslagen, geversioneerd
   snapshot (JSON) — nooit live uit muterende entiteiten, zodat een herafdruk identiek is.
2. **Achterwaartse compatibiliteit**: snapshotvelden zijn alleen-toevoegen met defaults;
   oude snapshots blijven deserialiseerbaar (patroon: `SequenceLabel` in `LabelSnapshot`).
3. **Nummering/identiteit**: elk gegenereerd document verwijst naar zijn bronentiteit en
   draagt het tenantnummer van die entiteit; hergeneratie verhoogt een versienummer in
   plaats van te overschrijven (patroon: `ProofOfDelivery.Version` + `IsCurrent`).
4. **Opslag**: bestanden via `IFileStorageService`-storage-keys, nooit paden in de DB.

## Rapportcentrum (geïmplementeerd)

Het rapportcentrum is de dunne catalogus over bestaande module-endpoints — géén
herimplementatie van queries.

### Architectuur

- **`Modules/Reporting/ReportCatalog.cs`** — het register: statische
  `ReportDefinition`-records (`Id`, `Category`, `Title`, `Description`, any-of
  `Permissions`, `Kind`, `Endpoint`/`Route`, `Filters`, `FileType`). Drie soorten:
  - `Export`: verwijst naar een bestaand downloadendpoint (colli-XLSX'en, KPI-exports,
    orders-CSV);
  - `Page`: verwijst naar een bestaand scherm dat als rapport fungeert (KPI-dashboard,
    vlootdashboard, incidentenregister);
  - `ComingSoon`: aangekondigd, niet uitvoerbaar, geen nep-data.
- **`GET /api/reports/catalog`** (`ReportCatalogController`, permissie `reports.view`) —
  levert uitsluitend metadata en filtert op de volledige permissieset van de gebruiker
  (één query via `IPermissionSetService`, gedeeld met het globale zoeken). Verborgen ≠
  beveiligd: elk gerefereerd endpoint dwingt zijn eigen permissie zelf af.
- **FE `features/reports/`** — `/reports` toont categoriekaarten + rapportlijst
  (alleen metadata, lazy geladen); `/reports/{id}` is een generieke runner die uitsluitend
  de gedeclareerde filters rendert (`dateRange`, `search`, `orderStatus`) en downloadt
  via het bestaande endpoint van het rapport. `Page`-rapporten redirecten naar hun route.

### Permissies

`reports.view` opent de catalogus (planner, dispatcher, management, boekhouding en hr via
sjablonen + upgradestap 7). Per rapport gelden daarbovenop de bestaande permissies van het
onderliggende endpoint of scherm; rapporten zonder die permissie worden verborgen, niet
uitgegrijsd.

### Nieuw rapport toevoegen

1. Bouw (of hergebruik) het endpoint in de module die de data bezit, conform de regels
   bovenaan dit document.
2. Voeg één `ReportDefinition` toe in `ReportCatalog.All` met de juiste permissies en
   gedeclareerde filters.
3. Klaar — de FE-pagina's renderen catalogusmetadata en hoeven niet aangepast te worden.
   Nieuwe filtertypes vergen één uitbreiding in `ReportViewerPage`.
