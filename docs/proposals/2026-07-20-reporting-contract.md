# Rapportage- en documentgeneratiecontract

Status: vastgelegd contract (fase 12 van de verbeteringsgolf 2026-07-20). De bestaande
acht XLSX-rapporten en de labelrendering volgen dit al; nieuwe rapporten en documenten
moeten het volgen.

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

## Toekomstig rapportcentrum

Wanneer een overkoepelend "rapportcentrum" gebouwd wordt, is dat een dunne catalogus
(`code`, titel, omschrijving, permissie, endpoint) over de bestaande module-endpoints —
géén herimplementatie van queries. De catalogus toont alleen rapporten waarvoor de
gebruiker de permissie heeft; de endpoints blijven zelf de permissie afdwingen.
