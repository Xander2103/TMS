# Magazijnen en dockplanning (`/warehouses`, `/dock-planning`)

## Domein (module Warehousing)

- **Warehouse**: naam, gekoppelde master-`Location` (adres/coördinaten worden NIET
  gedupliceerd), actief-vlag, openingsuren (`OpensAt`/`ClosesAt`), contactgegevens.
- **Dock**: code (uniek per magazijn), capabilities (laden/lossen/ADR/koeling),
  maximale voertuigafmetingen, actief-vlag.
- **DockAppointment**: koppelt aan de BESTAANDE aggregaten (rit, opdracht, voertuig,
  oplegger, chauffeur), operatietype, gepland venster, werkelijke tijden, prioriteit
  (zelfde schaal als orders), referentie/opmerkingen, `Version`-concurrencytoken.
  `DockId = null` betekent: in de wachtrij.

## Statusmachine (`DockAppointmentStatusMachine`)

Planned → Expected | Arrived | Cancelled
Expected → Arrived | NoShow | Cancelled
Arrived → Waiting | AssignedToDock | Cancelled
Waiting → AssignedToDock | Cancelled
AssignedToDock → InProgress | Waiting | Cancelled
InProgress → Completed
Completed / Cancelled / NoShow: eindstatussen.

Fysieke overgangen stempelen tijden (ArrivedAt, StartedAt, CompletedAt). InProgress kan
alleen mét toegewezen dock. Eindstatussen weigeren verdere bewerkingen; verwijderen kan
alleen vóór aankomst (anders annuleren).

## Conflictregels (backend-afgedwongen)

Blokkerend en overschrijfbaar met `warehouse.conflict_override` + verplichte reden
(gedeelde `conflict_overrides`-trail): dockoverlap met een bezettende afspraak,
docktype-mismatch (laden/lossen), ADR-vereiste zonder ADR-dock, inactief dock of magazijn,
buiten openingsuren. Blokkerend en NIET overschrijfbaar: minimumduur (< 15 min), onbekend
dock. Informatief: geen dock toegewezen (wachtrij). Stale versies geven 409 met de actuele
staat.

## Wachtrij en dashboard

Wachtrij = Arrived/Waiting zonder dock, gesorteerd op prioriteit en dan aankomsttijd.
Dashboard per magazijn per dag: verwacht, wachtend, bezig, afgerond, vertraagd (gepland
begin verstreken zonder aankomst, of behandeling voorbij gepland einde), no-shows en
dockbezetting (geboekte minuten / openingsvenster).

## Scans: één pakketlevenscyclus

Er is GEEN tweede pakketsysteem. Scanvoortgang per afspraak wordt afgeleid uit de
bestaande `Package.CurrentLifecycleStatus` van de gekoppelde opdracht (laden: alles
voorbij AwaitingLoading telt; lossen: Delivered/PartiallyDelivered/ReturnedToDepot), en
open afwijkingen komen uit `PackageExceptionState`. Magazijnscans zelf lopen via de
bestaande one-scan-pipeline (`ScanService.SubmitAsync`).

## Frontend

`/warehouses`: masterdata (magazijnen + docks) met `warehouse.manage`.
`/dock-planning`: docks als rijen op het openingsurenvenster, afspraken als blokken;
slepen naar een ander dock of naar de wachtrij roept altijd het backend-commando aan;
statusacties via de detailmodal; conflictdialoog met permissiebewuste override-met-reden;
KPI-strook uit het dashboard. Sneltoets `g w` opent de magazijnen.
