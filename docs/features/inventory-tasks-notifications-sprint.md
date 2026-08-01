# Sprint: Inventory Control, Tasks, Notifications & Multilingual Customer Portal

Datum: 2026-08-01 · Branch: `nav-redesign` · Status: in uitvoering

Dit document is de Fase 0-specificatie: inventaris van de bestaande architectuur,
de ontwerpbeslissingen en het implementatieplan voor de sprint.

---

## 1. Inventaris bestaande architectuur (samenvatting)

### Voorraad (Modules/Employees)
- Voorraad hangt volledig aan `IssuedItemTemplate` (bedrijfsmiddelen) + `IssuedItemVariant`,
  met een immutable ledger `StockMovement` (`InitialStock|Purchase|Correction|Issue|Return|Damaged|Lost|Disposed|Transfer`).
- Concurrency: `Guid Version` als `IsConcurrencyToken()` op template én variant; elke mutatie
  roteert de token. Cache `CurrentStock` + ledger committen in één `SaveChanges`.
- Bestaand: `AllowNegativeStock`, `LowStockThreshold` (template + variant), `MinimumStock`
  (kolom bestaat, **ongebruikt**), `StockTrackingEnabled`, `StorageLocation` (vrije tekst),
  `ReturnRequired`. Override-flow bestaat al: `OverrideInsufficientStock` + verplichte
  `OverrideReason` + permissie `inventory.override_negative_stock` (`IssuedItemService.EnsureAvailableAsync`).
- `LowStockNotifier`: event-driven crossing-detectie met 7-dagen dedupe via LinkPath-marker;
  géén periodieke scan, géén herstel-/herdalingsdetectie.
- Geen locatie-entiteit, geen target-/bestelniveau, geen `ExpectedReturnAt` op uitgiftes.

### Notificaties & messaging
- `Notification` (per gebruiker; type/categorie/ernst/titel/link, IsRead/IsArchived) +
  `NotificationPreference` (opt-out per categorie, Critical levert altijd).
- Eventplatform: `INotificationEventService.PublishAsync(eventKey, context)` →
  `NotificationEventCatalog` (statisch) + `NotificationRule` (tenantoverride) +
  `CustomerNotificationOverride` → e-mail via outbox (`OutboxMessage`, uniek
  `(TenantId, IdempotencyKey)`) en/of in-app fan-out.
- Nieuw event = 4 plekken: `MessageKinds` (+ `All`), `NotificationEventCatalog.Entries`,
  `NotificationTypeCatalog.Map`, optioneel `BuiltInMessageTemplates`.
- Dedupe-blauwdrukken: `ReminderDispatchLog` (uniek `(TenantId, DedupeKey)`) en
  `OperationalAlert` (DedupeKey + Active/Acknowledged/Resolved + upsert-sync).
- `InternalMessage` + `InternalMessageRecipient` = bestaande interne inbox (persoon→personen,
  ReadAt per ontvanger), frontend `/inbox`, permissie `messages.send`.
- Frontend: badge op nav-item "Meldingen", 60s-polling op `unread-count`; geen bel-dropdown.
- Portaal: `CustomerMessage`-threads + `PortalAnnouncement`-broadcasts; géén notificatiefeed,
  géén targeting per klant, geen meertaligheid.

### Background jobs
- Patroon: `BackgroundService` + `PeriodicTimer` + `IServiceScopeFactory`, één scope per tick,
  fouten gelogd zonder de lus te breken. Tenantlus: `Tenants.Where(IsActive)` →
  `DevTenantContext(tenantId)` + `DevCurrentUserContext(null)` + handmatige servicecompositie
  (voorbeeld: `ExpiryNotificationProducer.BuildEventService`).

### Permissions, tenancy, audit
- `PermissionCodes.All` (module.action, snake_case) → `PermissionCatalogSeeder` (idempotent,
  retireert verdwenen codes). Role templates in `DefaultRoleDefinitions` (11 templates,
  `TemplateCode` als identiteit), upgrades in `DefaultRoleUpgrades` (**CurrentVersion = 22**),
  guard-tests: elke cataloguscode moet ergens afgedwongen zijn (`Phase8SupplyChainTests`),
  elke upgrade-versie krijgt een `VersionNN_…`-test (`DefaultRoleSeederTests`).
- Tenancy: `ITenantOwned` → automatische globale queryfilter (`ApplyGlobalTenantFilters`);
  entities erven `AuditableTenantEntity`; client-FK's via `TenantReferenceGuard`.
- Audit: `IAuditService.RecordAsync(entityType, id, action, old, new)` met TenantId/UserId/
  IP/CorrelationId; masking-helper voor gevoelige waarden; append-only tabel.
- Concurrency: `Version`-kolom met `IsConcurrencyToken()` (werkt op Npgsql én SQLite-tests).

### Portaal & i18n
- Portalgebruiker = `User` met `CustomerId`; zelfde JWT; scoping via `MyCustomerAsync` (403/404).
- **Geen enkel i18n-systeem in de frontend**; alles hardcoded NL + `'nl-BE'` formatters.
- Backend-taalvelden bestaan al: `Employee.PreferredLanguageCode`, `Customer.DefaultLanguageCode`,
  `CustomerContact.PreferredLanguageCode`, `MessagingProfile.PreferredLanguage`,
  outbox-taalresolutie. **`User` heeft géén taalveld.**

### HR / medewerkers
- `Employee` met `DepartmentId` (lookup), `EmploymentStatus`, multi-`JobFunctions`;
  **geen Team/Branch/Manager-veld** → teamscoping = afdeling.
- Personeelsfiche: page-tabs + `SectionedForm`-secties; nieuw tab-recept gedocumenteerd
  (TAB_IDS + permissiegate + `TabPanel`; optioneel panel-sectie in edit-form).
- Upload-patroon: `IFileStorageService` + `UploadValidation` (size/extensie/magic bytes) +
  malware-scan chokepoint + metadata-entiteit met `StorageKey`.
- Deactivatieflow: `POST /api/employees/{id}/deactivate` (permissie `employees.deactivate`).

---

## 2. Functionele scope

1. **Voorraadregels**: drempels (warning/minimum/target/bestelhoeveelheid), veilige
   negatieve-voorraadflow met servergecontroleerde bevestiging, statusmodel, dashboard.
2. **Notificatieplatform**: dedupe, acknowledge, resolve, expiry op `Notification`;
   bel-icoon met dropdown; nieuwe categorieën.
3. **Medewerkerberichten**: uitbreiding `InternalMessage` (prioriteit, bevestiging,
   zichtbaarheidsvenster, ontvangers per rol/afdeling, delivery-status, e-mailkanaal).
4. **Klantportaalberichten**: nieuw meertalig `PortalMessage`-model (NL/FR/EN, display
   modes, targeting per klant/gebruiker, read/acknowledge, optionele e-mail).
5. **Takenmodule**: `EmployeeTask` + statusmachine + review + bewijs + categorieën +
   templates + terugkerende taken + herverdeling bij uitdiensttreding.
6. **Retourplicht & bestelvoorstellen**: `ExpectedReturnDate`/`ConditionAtIssue` op
   uitgiftes, achterstallige-retourmeldingen, `ReorderProposal` met statusflow.
7. **Meertalig klantenportaal**: NL/FR/EN met taalwisselaar, voorkeur op `User`,
   fallbackketen, meertalige portaalmails.
8. **Dashboardtegels**, notificatievoorkeuren (bestaand model gedocumenteerd/uitgebreid),
   beperkte escalatielaag, rechten + role-template-upgrade v23, audit, jobs, tests, docs.

## 3. Architectuurbeslissingen

| # | Beslissing | Motivatie |
|---|---|---|
| A1 | Voorraad blijft op `IssuedItemTemplate`/`IssuedItemVariant`; géén nieuw SKU-model. | Werkregel 3: geen parallelle infrastructuur. |
| A2 | `LowStockThreshold` ≙ **WarningStockLevel**; `MinimumStock` ≙ **MinimumStockLevel** (wordt nu actief gebruikt); nieuw: `TargetStockLevel`, `ReorderQuantity` (template) — variant krijgt alleen drempels, geen target. | Hergebruik bestaande kolommen, geen duplicaten. |
| A3 | Negatieve-voorraadbevestiging via **optimistic concurrency**: preflight-endpoint geeft actuele stand + `Version`; de mutatie draagt `ExpectedVersion` + `ConfirmNegativeStock` + reden. Version roteert per mutatie → verouderde of hergebruikte bevestigingen falen met 409. | Spec-optie "requestmodel met verwachte versie + optimistic concurrency"; geen aparte tokenopslag nodig; race-veilig bewezen patroon. |
| A4 | Voorraadstatus + meldingen via nieuw `InventoryAlert` (upsert per tenant/template/variant/status-soort, DedupeKey, Active/Resolved) naar het model van `OperationalAlert`; notificatie alleen bij statusovergang; herstel → Resolved; nieuwe daling → nieuwe melding. | Bewezen anti-spam-patroon in dit repo. |
| A5 | Geen `InventoryLocation`-entiteit deze sprint; `StorageLocation` (vrije tekst) blijft. | Bestaat niet; toevoegen raakt het hele uitgifte-domein; expliciet buiten scope. |
| A6 | Notificatie-uitbreiding **op de bestaande `Notification`**: `DedupeKey`, `RequiresAcknowledgement`, `AcknowledgedAt`, `ResolvedAt`, `ExpiresAt`; nieuwe categorieën `Inventory`, `Task`, `CustomerPortal`, `Fleet`, `Document`, `Approval`; nieuwe severity `Success`. Enums zijn string-gestored → additief veilig. | Werkregel 2/3. |
| A7 | Notificatie-endpoints blijven **self-scoped `[Authorize]`** (bestaande, gereviewde allowlist). Er komen géén `notifications.view_own`-permissies: een recht dat iedereen per definitie heeft is ruis in de catalogus en breekt het "elke permissie wordt ergens gecheckt"-guardprincipe niet maar verzwakt het wel. `notifications.view_all` wordt niet ingevoerd (geen use case; audit-inzage loopt via `audit_logs.view`). Beheer = bestaand `notification_rules.view/manage`. | Bewuste afwijking van de spec-voorbeeldlijst, gedocumenteerd. |
| A8 | Medewerkerberichten = **uitbreiding van `InternalMessage`** (geen tweede model): `Priority`, `RequiresAcknowledgement`, `VisibleFrom`, `ExpiresAt`, `RelatedEntityType/Id`, `EmailRequested`; ontvanger krijgt `AcknowledgedAt`; verzending naar rol/afdeling wordt bij verzenden **geëxpandeerd** naar concrete ontvangerrijen (accountability + delivery-status per persoon). | Werkregel 2/3; comma-separated lijsten vermeden. |
| A9 | Klantportaalberichten = nieuw `PortalMessage` (kolommen per taal `TitleNl/Fr/En`, `BodyNl/Fr/En`) + `PortalMessageRecipient` (per klant; `UserId` optioneel voor één gebruiker) + `PortalMessageReceipt` (per gebruiker: ReadAt/AcknowledgedAt). Kolommen-per-taal i.p.v. translations-tabel: exact 3 gekende talen, simpeler query- en formulierbeeld. `PortalAnnouncement` blijft bestaan voor tenantbrede banners (gedocumenteerd als legacy-broadcast). | Genormaliseerd waar het telt (ontvangers/receipts), pragmatisch waar het kan (3 vaste talen). |
| A10 | Taken: nieuw domein `Modules/Tasks`. **Eén taak = één verantwoordelijke** (`AssignedEmployeeId`); toewijzen aan meerdere medewerkers maakt per medewerker een eigen taak (fan-out bij aanmaak, gedeelde `BatchId` voor traceerbaarheid). Geen `TaskAssignment`-tabel. | Accountability per persoon zonder join-complexiteit; spec laat de keuze expliciet open. |
| A11 | Teamscoping = **afdeling** (`Employee.DepartmentId`): `tasks.view_team` toont taken van medewerkers in de eigen afdeling. Er is geen team-/managerstructuur; die bouwen we niet bij (werkregel: geen tweede teammodel). | Bestaande structuur hergebruiken. |
| A12 | Terugkerend werk: `TaskTemplate` (+ items) en `TaskRecurrence` los van de gegenereerde `EmployeeTask` (snapshotvelden, `RecurrenceId` + `RecurrenceDedupeKey` uniek per tenant). Generatie in een tenant-aware job, idempotent per periode-sleutel. | Spec Fase 10. |
| A13 | i18n: **eigen lichtgewicht i18n** (LocaleProvider + `t()` + JSON-bestanden per domein onder `src/locales/{nl,fr,en}/`). Geen i18next: het project voert bewust een minimaal dependency-beleid (4 runtime-deps) en de behoefte (3 talen, portaalscope, interpolatie) is klein. | Past bij bestaand beleid; geen supply-chain-oppervlak erbij. |
| A14 | Taalvoorkeur op `User.PreferredLanguageCode` (nl/fr/en, nullable). `PUT /api/portal/profile/language`; meegeleverd in login/refresh-`me`-payload en portal-context. Fallback: gebruikersvoorkeur → browsertaal (alleen client-side eerste bezoek) → klantdefault (`Customer.DefaultLanguageCode`) → `nl`. Portaalmails gebruiken dezelfde keten via de bestaande outbox-taalresolutie. | Sluit aan op bestaande messaging-taalketen. |
| A15 | Escalaties: klein vast model `EscalationPolicy` (per tenant per soort: vertraging, doelpermissie, actief) met geseedete defaults (inactief tenzij logisch), verwerkt in de sweep-jobs met `ReminderDispatchLog`-dedupe. Geen workflow-engine. | Spec Fase 16. |
| A16 | Nieuwe jobs volgens het bestaande patroon: `InventorySweepHostedService` (statusscan, retouren, escalaties voorraad; 1 u), `TaskSweepHostedService` (recurrence-generatie, bijna-vervallen/achterstallig, escalaties taken; 15 min), `NotificationMaintenanceHostedService` (expiry + retentie; 6 u). | Gescheiden verantwoordelijkheden, geen god-service. |
| A17 | Bestelvoorstellen: `ReorderProposal` met statusflow `Proposed→Reviewed→Approved→Ordered→Completed` / `Dismissed`; max één open voorstel per template/variant (gefilterde unieke index). Geen purchase-ordermodule. | Spec Fase 13. |
| A18 | Retourplicht: `ExpectedReturnDate` + `ConditionAtIssue` op `EmployeeIssuedItem`; "achterstallig" is afgeleid (geen statusduplicatie). En passant worden de bestaande gaten gedicht: `IssuedByUserId`/`ReceivedBackByUserId` worden gezet en `ReturnDisposition` gepersisteerd. | Klein, veilig, dicht bestaande bugs. |

## 4. Datamodel (nieuw/gewijzigd)

### Gewijzigd
- `IssuedItemTemplate` + `TargetStockLevel int?`, `ReorderQuantity int?`,
  `NegativeStockRequiresReason bool` (default true).
- `EmployeeIssuedItem` + `ExpectedReturnDate DateOnly?`, `ConditionAtIssue string?`,
  `ReturnDisposition string?` (gepersisteerd).
- `Notification` + `DedupeKey string?`, `RequiresAcknowledgement bool`,
  `AcknowledgedAt DateTime?`, `ResolvedAt DateTime?`, `ExpiresAt DateTime?`.
- `InternalMessage` + `Priority`, `RequiresAcknowledgement`, `VisibleFrom?`, `ExpiresAt?`,
  `RelatedEntityType/Id`, `EmailRequested`; `InternalMessageRecipient` + `AcknowledgedAt?`,
  `EmailOutboxMessageId?`.
- `User` + `PreferredLanguageCode string?` (maxlength 5).

### Nieuw
- `InventoryAlert` (tenant; TemplateId, VariantId?, AlertKind `LowStock|CriticalStock|OutOfStock|NegativeStock|ReturnOverdue`,
  Status `Active|Resolved`, DedupeKey uniek per tenant, StockSnapshot, ThresholdSnapshot,
  LastSeenAt, ResolvedAt) — bron voor dedupe + dashboard "open alerts".
- `ReorderProposal` (TemplateId, VariantId?, CurrentStock, TargetStock, SuggestedQuantity,
  ApprovedQuantity?, Status, Notes, CreatedBy/ApprovedBy, ResolvedAt).
- `PortalMessage`, `PortalMessageRecipient`, `PortalMessageReceipt` (zie A9).
- `TaskCategory` (LookupEntity + kleur), `EmployeeTask`, `TaskAttachment`,
  `TaskTemplate`, `TaskTemplateItem`, `TaskRecurrence`.
- `EscalationPolicy`.

Alle nieuwe entiteiten erven `AuditableTenantEntity` (tenant-queryfilter automatisch,
soft delete, audit-stamps). `EmployeeTask` krijgt `int Version` concurrency-token.

### Statussen & enums
- `InventoryStatus`: `Normal | LowStock | CriticalStock | OutOfStock | NegativeStock`.
  Regels (variant-niveau wint, template-fallback voor drempels):
  `NegativeStock: stock < 0` · `OutOfStock: stock == 0 && tracked` ·
  `CriticalStock: 0 < stock <= Minimum` · `LowStock: Minimum < stock <= Warning`
  (bij `Minimum == null`: LowStock = `0 < stock <= Warning`) · anders `Normal`.
  `Warning == null` → geen LowStock-band; beide null → alleen Negative/OutOf/Normal.
  Meldingen alleen voor `StockTrackingEnabled`-artikelen.
- `TaskStatus`: `Todo | InProgress | Blocked | WaitingForReview | Completed | Cancelled`.
  Overgangen (backend afgedwongen in `TaskStatusMachine`):
  `Todo→InProgress|Cancelled`, `InProgress→Blocked|WaitingForReview|Completed*|Cancelled`,
  `Blocked→InProgress|Cancelled`, `WaitingForReview→Completed|InProgress(afkeuring, commentaar verplicht)`,
  `Completed→InProgress` alleen met `tasks.reopen`; `Cancelled` idem.
  (*Completed direct alleen wanneer `RequiresReview == false`.)
- `TaskPriority`: `Low | Normal | High | Urgent`.
- `ReorderProposalStatus`: `Proposed | Reviewed | Approved | Ordered | Dismissed | Completed`.
- `PortalMessageDisplayMode`: `Notification | DashboardBanner | BlockingAcknowledgement`.

## 5. API-contracten (nieuw; bestaande conventies: DTO's, ProblemDetails, 404 bij cross-tenant)

### Inventory
- `GET  /api/inventory/overview` — artikel/variant, drempels, status, laatste mutatie (paginated, filter op status).
- `GET  /api/inventory/alerts` — open `InventoryAlert`s (filter kind).
- `POST /api/issued-item-templates/{id}/stock/preflight` — `{variantId?, quantityDelta, employeeId?}` →
  `{currentStock, expectedStock, requiresConfirmation, requiresReason, version, warnings[]}`.
- Bestaande mutatie-endpoints krijgen `ExpectedVersion`/`ConfirmNegativeStock`/`Reason` in de request.
- `PUT  /api/issued-item-templates/{id}/thresholds` — drempels + target + reorder (permissie `inventory.manage_thresholds`).
- `GET/POST /api/inventory/reorder-proposals`, `POST /api/inventory/reorder-proposals/{id}/status`.

### Notifications (bestaand pad `api/notifications`)
- Nieuw: `POST /api/notifications/{id}/acknowledge`; lijst-DTO krijgt ack/resolve/expiry-velden.

### Employee messages (bestaand pad `api/internal-messages`)
- `POST` uitgebreid: `{recipientUserIds?, roleId?, departmentId?, allEmployees?, priority, requiresAcknowledgement, visibleFrom?, expiresAt?, sendEmail}`.
- `GET /api/internal-messages/{id}/delivery-status` — per ontvanger: bezorgd/gelezen/bevestigd/e-mailstatus.
- `POST /api/internal-messages/{id}/acknowledge` (ontvanger), `POST /api/internal-messages/{id}/cancel` (afzender/manage).

### Portal messages
- Intern: `POST /api/portal-messages` (targeting: customerIds[] of specifieke portalUserIds[]),
  `GET /api/portal-messages`, `GET /api/portal-messages/{id}/delivery-status`, `POST /api/portal-messages/{id}/cancel`.
- Portaal: `GET /api/customer-portal/portal-messages`, `POST .../{id}/read`, `POST .../{id}/acknowledge`,
  `GET .../portal-messages/unread-count`; banners/blocking komen mee in de dashboard-payload.
- `PUT /api/customer-portal/profile/language` — `{language: "nl"|"fr"|"en"}`.

### Tasks
- `GET/POST /api/tasks` (filters: mijn/door mij/medewerker/afdeling/categorie/status/prioriteit/
  deadline/achterstallig/review/related), `GET/PUT /api/tasks/{id}`,
  `POST /api/tasks/{id}/start|block|submit-for-review|complete|review|reopen|cancel`
  (statusacties dragen `expectedVersion`), `POST /api/tasks/{id}/attachments` (+ download),
  `GET /api/employees/{employeeId}/tasks`,
  `GET /api/employees/{employeeId}/tasks/open-summary`, `POST /api/tasks/redistribute`.
- Categorieën: `/api/task-categories` (LookupControllerBase).
- Templates: `GET/POST/PUT/DELETE /api/task-templates`, `POST /api/task-templates/{id}/apply`
  (→ taken voor medewerker), recurrences: `/api/task-recurrences` CRUD.

### Dashboard
- `GET /api/dashboard` uitgebreid met permissiegevoelige secties `inventory` (5 tellers) en
  `tasks` (mijn open/vandaag/achterstallig/wacht-op-review + teamtellers) en `messages` (ongelezen).

## 6. Permissions (nieuw in catalogus, module → codes)

- `inventory.manage_thresholds` — drempels/target/bestelhoeveelheid wijzigen.
- `inventory.view_movements` — mutatiehistoriek bekijken (bestaande endpoints krijgen deze i.p.v. brede view).
- `inventory.reorder_view`, `inventory.reorder_manage` — bestelvoorstellen zien / aanmaken+status.
- `inventory.loans_view`, `inventory.loans_manage_overdue` — uitleningen/retouren zien, achterstallig beheren.
- `tasks.view_own`, `tasks.manage_own` (starten/blokkeren/voltooien eigen taak),
  `tasks.view_team`, `tasks.view_all`, `tasks.create`, `tasks.assign`, `tasks.edit`,
  `tasks.cancel`, `tasks.review`, `tasks.reopen`, `tasks.manage_categories`,
  `tasks.manage_templates`, `tasks.manage_recurring`.
- `messages.send_bulk` (rol/afdeling/iedereen), `messages.view_delivery_status`, `messages.cancel`.
- `portal_messages.view`, `portal_messages.send`, `portal_messages.send_bulk`,
  `portal_messages.cancel` (view_status ⊂ view).
- `escalations.manage`.
Bestaand hergebruikt: `inventory.view/adjust/manage/override_negative_stock/low_stock_alerts`,
`issued_items.*`, `messages.send`, `notification_rules.view/manage`.

Role-template-upgrade **v23** (guard-test verplicht):
- `magazijn`: thresholds nee; wél `inventory.view_movements`, `inventory.reorder_view`,
  `inventory.reorder_manage` (aanmaken), `inventory.loans_view`, `tasks.view_own`, `tasks.manage_own`.
- `management`: alles van inventory (incl. thresholds, loans_manage_overdue), `tasks.*`
  (behalve manage_categories/templates/recurring → wél), `messages.send_bulk`,
  `messages.view_delivery_status`, `messages.cancel`, `portal_messages.*`, `escalations.manage`.
- `hr`: `tasks.view_own/manage_own/create/assign/view_team/review`, `messages.send_bulk`,
  `messages.view_delivery_status`; geen voorraaddrempels.
- `chauffeur`: `tasks.view_own`, `tasks.manage_own`.
- `planner`/`dispatcher`: `tasks.view_own`, `tasks.manage_own`, `tasks.create`.
- `boekhouding`: `tasks.view_own`, `tasks.manage_own`.
- Klantportaalrollen: níets van bovenstaande (portaalberichten lezen is self-scoped).

## 7. Notificatietypes (nieuwe eventkeys)

Inventory: `inventory_status_low`, `inventory_status_critical`, `inventory_status_out`,
`inventory_status_negative`, `inventory_negative_confirmed`, `inventory_return_due`,
`inventory_return_overdue`, `inventory_reorder_proposed`.
Tasks: `task_assigned`, `task_due_soon`, `task_overdue`, `task_blocked`,
`task_waiting_review`, `task_review_approved`, `task_review_rejected`, `task_reopened`,
`task_cancelled`, `task_redistributed`.
Messages/portal: `employee_message_received`, `portal_message_published` (e-mail in voorkeurstaal).
Escalatie: `escalation_raised`.
Alle in-app-types in `NotificationTypeCatalog` met categorie `Inventory|Task|CustomerPortal`;
deduplicatie via `Notification.DedupeKey` + `InventoryAlert`/`ReminderDispatchLog`.

## 8. Vertaalstrategie

- `src/locales/{nl,fr,en}/{common,navigation,auth,dashboard,orders,invoices,documents,messages,notifications,errors}.json`.
- `LocaleProvider` (React context) + `useT(domain)`; interpolatie `{name}`; ontbrekende sleutel →
  NL-waarde → sleutel zelf (dev-waarschuwing in console).
- Locale-aware datum/valuta-helpers (`nl-BE`/`fr-BE`/`en-GB`).
- Persistentie: `User.PreferredLanguageCode` via `PUT /api/customer-portal/profile/language`;
  eerste bezoek: browsertaal; na login wint de opgeslagen voorkeur; overleeft logout/login.
- Portaalberichten: inhoud per taal in het bericht zelf; weergavefallback voorkeurstaal →
  klantdefault → NL. E-mail bij publicatie gebruikt dezelfde keuze per ontvanger.
- Scope: het klantenportaal (layout, login/activatie, dashboard, orders, documenten,
  facturen, berichten, gebruikers, foutmeldingen). De interne app blijft NL.

## 9. Migratieplan (definitief)

Additieve migraties, in uitvoeringsvolgorde:
1. `20260731223702_NotificationLifecycle` — dedupe/ack/resolve/expiry-kolommen op
   `notifications` + indexen `(TenantId, DedupeKey)` en `(TenantId, ExpiresAt)`.
2. `20260731224943_InventoryControls` — drempel-/beleidkolommen op `issued_item_templates`,
   tabel `inventory_alerts` (unieke gefilterde indexen per target, NULL-variant gesplitst).
3. `20260731230641_EmployeeAndPortalMessages` — berichtvelden op `internal_messages`/
   `internal_message_recipients`; tabellen `portal_messages`/`_recipients`/`_receipts`;
   `users.PreferredLanguageCode`.
4. `20260731232110_EmployeeTasks` — `task_categories`, `employee_tasks` (indexen
   `(TenantId, AssignedEmployeeId, Status)`, `(TenantId, DueAt)`, `(TenantId,
   CreatedByUserId)`, related-entity, uniek gefilterde `(TenantId, RecurrenceDedupeKey)`),
   `task_attachments`, `task_templates`, `task_template_items`, `task_recurrences`.
5. `20260801074257_ReturnsAndReorders` — retourvelden op `employee_issued_items` (+ index
   `(TenantId, Status, ExpectedReturnDate)`); tabel `reorder_proposals` (max één open
   voorstel per target via gefilterde unieke indexen).
6. `20260801075801_EscalationPoliciesAndAlertEpisodes` — `escalation_policies` (uniek
   `(TenantId, Kind)`), `inventory_alerts.ActivatedAt`.

Rollbackrisico: puur additief (nieuwe kolommen nullable of met veilige defaults); droppen
van de nieuwe tabellen/kolommen is de omkeerroute; geen dataconversies. **Geverifieerd**:
apply op de bestaande dev-database én op een verse database (170 tabellen, scratch-DB
`ts_fresh_check`, daarna verwijderd).

## 10. Testplan

- Voorraad: preflight, geblokkeerde negatieve mutatie, bevestiging (verlopen versie, zonder
  permissie, zonder reden), concurrente uitgiftes, statusberekening (nulls), alert-dedupe +
  herstel + herdaling, cross-tenant 404, audit, bestelvoorstel-uniciteit, retour te laat.
- Taken: CRUD + scoping (eigen/afdeling/all), statusmachine (alle geldige/ongeldige
  overgangen), blocked-reden, reviewflow, reopen-permissie, evidence-upload, recurrence-dedupe,
  herverdeling + audit, cross-tenant.
- Notificaties: ack, dedupe, expiry, resolve, voorkeuren, self-scoping.
- Berichten: bulkpermissie, expansie rol/afdeling, delivery-status, ack vs read, outbox-idempotentie.
- Portaal: taalvoorkeur opslaan/valideren, berichtfallback per taal, isolatie (andere klant 404),
  read/ack, banner/blocking-selectie, e-mail-taal.
- Architectuurguards: nieuwe controllers geclassificeerd, permissies in catalogus + gecheckt,
  role-upgrade v23-test, tenantfilters (automatisch via bestaande guards), geen storage-tokens.
- Frontend: negative-stock-modal, taakfilters + statusacties, bel/badge, taalwisselaar +
  persistentie + vertaalvolledigheid (testhelper die sleutelsets nl/fr/en vergelijkt).

## 11. Expliciet niet in deze sprint

- Purchase orders/leveranciersfacturen (alleen `ReorderProposal` als voorloper).
- `InventoryLocation`-entiteit / multi-locatievoorraad (A5).
- Bijlagen op medewerkerberichten (extensiepunt aanwezig via bestaand uploadpatroon;
  taak-bewijs hééft wel bijlagen).
- Push-provider, realtime kanalen (polling blijft), SMS.
- Kanban-weergave voor taken (lijst + filters volstaan; komt evt. later).
- BPMN/workflow-engine; AI-/automatische vertaling; i18n van de interne app.
- Goedkeuringsaanvraag-flow voor negatieve voorraad zonder overridepermissie
  (`NegativeStockRequiresApproval`): gebruiker zonder permissie krijgt een duidelijke
  domeinfout; approval-wachtrij is een latere uitbreiding.
- Digest-mails (alleen immediate).

## 12. Commits (definitief)

1. `docs: add inventory/tasks/notifications sprint specification (phase 0)`
2. `feat(notifications): notification lifecycle (ack/resolve/dedupe/expiry) and bell`
3. `feat(inventory): negative-stock confirmation, status alerts, returns and reorder proposals`
   — fasen 1-2 en 12-13 samen: de voorraadfasen delen entiteiten/services/DTO's en zijn als
   één samenhangende wijziging gecommit.
4. `feat(messaging): employee broadcast messages and multilingual portal messages` (fasen 4-5)
5. `feat(tasks): employee task management with status machine, templates and recurrence` (fasen 6-11 backend)
6. `feat(platform): tenant-aware sweeps, escalation policies, dashboard sections and portal language` (fasen 2/8/14-backend/16/22)
7. `security: role-template upgrade v23 with sprint permission defaults and guard tests` (fase 17)
8. Frontend-afsluiting: taken-UI, portaal-i18n NL/FR/EN, dashboardtegels + docs.
