# Verlof-/afwezigheidscategorieën

> Corrections wave 2026-07-27 §5. Tests: `LeaveCategoryManagementTests`, `LeaveConfigAndGuardTests`.

Verloftypes (`leave_types`) en saldotypes (`leave_balance_types`) zijn **tenant-gebonden
masterdata**, beheerd onder **Instellingen → Verlof** (permissie `leave_types.manage` — de
bestaande, specifieke permissie; er was geen nieuwe nodig). Niet elk bedrijf gebruikt dezelfde
categorieën: een tenant kan Compensatie of ADR gewoon verwijderen of deactiveren.

## Verwijderen versus deactiveren

- **Nooit gebruikt** → verwijderen kan (soft delete volgens de huisconventie; audit `Deleted`).
- **Al gebruikt** (verloftype: gerefereerd door een afwezigheid — ook een soft-verwijderde;
  saldotype: gerefereerd door een verloftype of een saldorij) → verwijderen wordt geweigerd met
  exact: *"Categorie '…' is al gebruikt en kan niet worden verwijderd. Je kunt de categorie wel
  deactiveren."* De poging zelf wordt geauditeerd (`DeleteBlocked`).
- **Inactief**: verschijnt niet meer in de categoriekeuze voor nieuwe registraties
  (`activeOnly`-lijsten), blijft leesbaar op historische records en in de volledige beheerlijst.
- De lazy add-if-missing seeding (`EnsureSeededAsync`) kijkt bewust **door de soft-delete-filter
  heen**: een verwijderde standaardcategorie wordt nooit stilletjes heraangemaakt.

## Afwezigheden registreren

Het afwezigheidsformulier (medewerkerfiche → Afwezigheden) selecteert sinds deze wave een
**categorie uit de masterdata** (actieve verloftypes) in plaats van de hardgecodeerde
enum-lijst; het verloftype is de bron van waarheid voor het onderliggende `AbsenceType`. Oude
registraties zonder verloftype tonen "… (oude registratie)" en blijven bewerkbaar.

Endpoints: `GET/POST/PUT/DELETE api/leave-types[/{id}]`, `GET/POST/PUT/DELETE
api/leave-balance-types[/{id}]` (reads `employees.view`, writes `leave_types.manage`).
