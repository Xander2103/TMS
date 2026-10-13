# Personeelstaken

Takenmodule uit de inventory-tasks-notifications-sprint (2026-08-01). Backend:
`Modules/Tasks`; frontend: `src/features/tasks` (centrale pagina `/tasks`, tab "Taken" op de
personeelsfiche, beheer onder `/settings/task-templates`).

## Model

- **`EmployeeTask`** — één taak = één verantwoordelijke medewerker
  (`AssignedEmployeeId`). Toewijzen aan meerdere medewerkers maakt per medewerker een eigen
  taak; een gedeelde `BatchId` groepeert die siblings. Geen assignment-jointabel (bewuste
  keuze: accountability per persoon, zie sprintdocument A10).
- Snapshots: `CategorySnapshot` bevriest de categorienaam; door herhalingen gegenereerde
  taken snapshotten de sjablooninhoud — een latere sjabloonwijziging raakt historiek nooit.
- `int Version` is het optimistic-concurrency-token: **elke** statusactie stuurt
  `expectedVersion` mee; een verouderde versie geeft een duidelijke 400 en de UI herlaadt.
- `TaskAttachment` = bewijsstukken (pdf/jpg/png ≤ 10 MB) via de geharde uploadpijplijn
  (extensie- + magic-byte-validatie in de controller, malwarescan in de storage-service).

## Statusmachine (`TaskStatusMachine`, backend is de bron van waarheid)

```
Todo ─────────→ InProgress ──→ Blocked ──→ InProgress
  │                 │  │
  │                 │  └→ WaitingForReview ──→ Completed (goedkeuren)
  │                 │            └──────────→ InProgress (afkeuren, commentaar verplicht)
  │                 └→ Completed        (alleen wanneer RequiresReview = false)
  └→ Cancelled      (ook vanuit InProgress/Blocked)
Completed/Cancelled ──→ Todo   (uitsluitend via reopen, permissie tasks.reopen)
```

- Blokkeren vereist een reden; de opdrachtgever krijgt een notificatie.
- `RequiresCompletionNote`/`RequiresEvidence` gelden bij voltooien én bij indienen ter
  controle.
- De uitvoerder kan **nooit** zijn eigen taak goedkeuren, ook niet met tasks.review.

## Scoping (afdeling = team; er is bewust geen tweede teammodel)

| Permissie | Betekenis |
|---|---|
| `tasks.view_own` / `tasks.manage_own` | eigen taken zien / aanmaken+uitvoeren |
| `tasks.view_team` | taken van de eigen afdeling (`Employee.DepartmentId`) |
| `tasks.view_all` | alle taken |
| `tasks.assign` | toewijzen/herverdelen; zonder view_all beperkt tot de eigen afdeling |
| `tasks.edit` / `tasks.cancel` / `tasks.review` / `tasks.reopen` | beheer daarbuiten |
| `tasks.manage_categories` / `manage_templates` / `manage_recurring` | inrichting |

Buiten scope = 404 (onvindbaar, nooit 403 op andermans record). Alle referenties
(medewerkers, categorieën) worden server-side tegen de tenant gevalideerd.

## Sjablonen & herhalingen

- `TaskTemplate` + `TaskTemplateItem` (relatieve deadline in dagen, review/notitie/bewijs
  per item). `POST /api/task-templates/{id}/apply` materialiseert het sjabloon voor één
  medewerker (via de taakservice, dus met scope-checks en notificaties).
- `TaskRecurrence` (Daily/Weekly/Monthly/Yearly/CustomDays) wordt door de task-sweep
  gematerialiseerd. Idempotentie: per taak een `RecurrenceDedupeKey`
  `recurrence:{recurrenceId}:{periodStart:yyyyMMdd}:{itemId}`, uniek per tenant (gefilterde
  unieke index) — herhaalde runs, retries en races kunnen nooit dupliceren. Inactieve
  sjablonen/herhalingen/medewerkers worden overgeslagen.

## Herverdeling (fase 11)

`POST /api/tasks/redistribute` `{fromEmployeeId, action: reassign|cancel, targetEmployeeId?,
newDueAt?, reason?}` verplaatst of annuleert alle **open** taken; elke taak wordt individueel
geaudit (`Redistributed`/`CancelledOnRedistribution`) en de nieuwe verantwoordelijke wordt
genotificeerd. Nooit automatisch/stil: de deactivatieflow op de personeelsfiche biedt de
dialoog alleen aan. `cancel` vereist bovenop `tasks.assign` ook `tasks.cancel`.

## Notificaties

`task_assigned`, `task_due_changed`, `task_due_soon`, `task_overdue`, `task_blocked`,
`task_waiting_review`, `task_review_approved/rejected`, `task_reopened`, `task_cancelled`,
`task_redistributed` — categorie **Task** in het notificatiecentrum. Due-soon/overdue zijn
one-shot (stempels `DueSoonNotifiedAt`/`OverdueNotifiedAt`, gereset wanneer de deadline
wijzigt); escalatie loopt via de escalatielaag (zie docs/background-jobs.md).

## Audit

Aangemaakt/gewijzigd/gestart/geblokkeerd/ingediend/goedgekeurd/afgekeurd/heropend/
geannuleerd/herverdeeld + bewijs toegevoegd/verwijderd — allemaal via `IAuditService` met
doelgerichte payloads (nooit hele entiteiten).
