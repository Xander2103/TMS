# Master-data integrity rules

Remediation of the master-data acceptance of 2026-10-18 (branch `remediation/core-master-integrity`).
These are the invariants the Core enforces; the code comments carry the finding ids (MD-…).

## 1. Issued and executed history does not follow master data

| Record | What is frozen | When | Where |
|---|---|---|---|
| CMR / delivery note / work order | the complete printed model (parties, carrier, vehicle/trailer/driver, goods lines with the unit **label**, stops, references, dossier number) as structured JSON | at issue, in the same save as the number claim | `issued_transport_documents.SnapshotJson` |
| Invoice / credit note | seller + customer identity, structured addresses, VAT, Peppol scheme/id, buyer reference, IBAN/BIC, footer, logo **file version** | at issue (`PartySnapshotFrozenAt`) | `invoices.*Snapshot`, `Seller*`, `SellerLogoStorageKey` |
| Invoice line ledger | resolved ledger account: legal-entity mapping → category default | at issue | `invoice_lines.LedgerAccount*Snapshot` |
| Completed trip | driver name, vehicle number + plate, trailer number + plate | at completion | `trips.Executed*` |
| Completed order | customer display name | at completion | `transport_orders.CustomerNameAtCompletion` |

* An issued document renders **only** from its snapshot. An empty snapshot field means "empty at issue" —
  never "fill in from today's customer / legal entity". PDF, UBL, Peppol validation, the invoice screen and
  the invoice list all read the snapshot.
* Issued PDFs (transport documents and invoices) are **byte-identical** on every download
  (`Modules/Pdf/DeterministicPdf`: dates, document id, font-subset tags and XMP ids pinned to the document).
* A replaced CMR keeps rendering its own snapshot, marked "VERVANGEN door …" (lifecycle marker, not content).
* A logo file that an issued invoice printed is never deleted when the legal entity uploads another logo.
* Draft invoices and open orders/trips legitimately follow the current master data.
* Live by design: the dossier header's customer name (the dossier is the customer relationship), the
  delivery channel check "Peppol enabled now?" (operational), and open work.

### Legacy rows (before this remediation)

Older rows stored no content snapshot. They are frozen **once** from the master data as it stands at
deployment — the only source left; no older value can be reconstructed:

* issued transport documents — startup `IssuedTransportDocumentSnapshotBackfill`, flagged `SnapshotIsBackfilled`;
* issued invoices — startup `InvoicePartySnapshotBackfill`: invoices issued after the accounting remediation
  (`IssuedAt` set) only get their logo version pinned (their snapshot was taken at issue); older rows get
  their never-frozen fields filled once and are flagged `PartySnapshotBackfilled`;
* completed trips / orders — migration `ExecutedIdentitySnapshots` (SQL, deterministic).

From then on these rows are exactly as immutable as a row frozen at issue.

## 2. Referenced records are deactivated, not deleted

A destructive delete of a record that history or live work references is refused with HTTP 409
`code = common.record_referenced`, a Dutch sentence with the counts and `canDeactivate`. Unused records
can still be deleted. Explicit, per-domain checks (no reflection):

| Record | Counts as a reference | Guard |
|---|---|---|
| Customer | orders, dossiers, invoices, credit notes, issued transport documents, incidents, portal users, customer messages, order imports | `CustomerReferences` |
| Driver | trips, activities, POD, damage, fuel, incidents, execution exceptions, dock appointments (an unused driver releases its vehicle slots / tank card) | `DriverReferences` |
| Unit type | orders, goods lines, price lines, tariff rules, sales-category default unit | `UnitTypeReferenceGuard` |
| Vehicle / trailer / customer / driver category, department, job function, item category | the records classified by it | `ILookupReferenceGuard<T>` registrations |
| Issued-item template | stock on hand, stock movements, issued items, reorder proposals | `IssuedItemTemplateReferences` (reusable by the warehouse remediation) |
| Dock | dock appointments | `WarehouseAdminService.DeleteDockAsync` |
| Vehicle / trailer | trips, fleet records (existing FL-B1 guard) | `FleetReferences` |

Business identifiers that history prints are never handed out again: customer numbers are unique
case-insensitively over **all** rows (deleted included); a unit code that history stores cannot be changed
and the code of a deleted unit cannot be re-created. New orders only accept units that exist, are active and
are allowed for order entry; existing lines keep their (possibly now inactive) unit.

## 3. Employee offboarding ends access

ONE orchestrator, `EmployeeOffboardingService.OffboardAsync`, is reached by every path that ends an
employment (the audit names the trigger):

| Path | Trigger |
|---|---|
| `POST /api/employees/{id}/deactivate` ("Medewerker inactief zetten", permission `employees.deactivate`) | `ExplicitDeactivation` |
| `PUT /api/employees/{id}` that sets status Terminated, or an end date already behind the last working day | `TerminatedByEdit` (same save as the edit) |
| `EmploymentEndSweepHostedService` (15 min, state-driven, restart-safe) once a future end date has passed | `EmploymentEndDatePassed` |
| the same sweep for legacy rows: employment ended but a user / driver still active | `ResidualAccess` |

In one save: employee inactive + Terminated (end date = tenant-local today when none was set) → linked
users inactive with refresh tokens, pending activation/reset links and the security stamp revoked →
linked drivers switched off through `DriverDeactivation` (fixed/current vehicle slots **and the driver's
fixed trailer** released) → clock-in code revoked, open attendance session closed → upcoming trips counted
in the audit (not silently unassigned). History is untouched; nothing is deleted. The tenant's last active
administrator is never locked out this way.

* **End date semantics:** `EmploymentEndDate` is the LAST working day. A future end date changes nothing
  today; access ends from the next tenant-local day. "Terminated" together with a future end date is
  refused as contradictory.
* **Defense in depth:** login, refresh and every authenticated request (`AccountStateAuthorizationFilter`)
  refuse a disabled/blocked account and a user whose LINKED employment has ended (`EmploymentAccess`),
  even before the sweep ran. Users without an employee link (administrators, portal users) are unaffected.
* **Driver-role deactivation is not offboarding:** a driver edit with `isActive=false` takes the person out
  of the driver pickers and releases the fixed/current vehicle slot and fixed trailer, but leaves the user
  account and its other roles alone. An inactive driver cannot be put back into a vehicle slot, and a
  driver of a departed employee cannot be reactivated.
* **Reactivating the employee does not re-enable the login or the driver** — an administrator re-enables
  the account explicitly, and only after the employee is active again (`users/{id}/active` refuses an
  account of a departed employee).

## 4. Numbering

`TenantNumbering.SaveWithClaimedNumberAsync` retries both a counter conflict and a unique-number collision
(skip forward), so parallel creates never surface a 500. A counter can only be lowered / re-prefixed when
none of the numbers it would hand out exists (Dutch refusal naming the highest number in use). Two legal
entities may not print the same invoice series. Invoices keep their gap-free per-entity sequence.

## 5. Identity and concurrency

* E-mail identity is `User.NormalizedEmail` (trim + lower), unique per tenant, used by login and reset.
* Role names are unique per tenant regardless of letter case.
* Customer, legal entity and sales category carry a `Version`; edits send `expectedVersion`; a stale edit
  is refused with 409 `code = common.concurrency_conflict` ("Deze gegevens zijn intussen gewijzigd …").

## 6. Authoritative settings

| Setting | Authoritative source | Notes |
|---|---|---|
| Invoice numbering | legal entity (format, prefix, sequence) | company-settings invoice counter is legacy (entity-less invoices can no longer be issued); hidden from the form |
| IBAN / BIC on invoices | legal entity | company IBAN hidden from the form |
| Payment term | customer → legal entity → company settings | blank customer term = fallback (stored 0) |
| Invoice address | customer default billing address (when marked) → registered address | |
| Logo | legal entity (versioned) | company "logo reference" hidden |
