# Background jobs

Alle jobs volgen hetzelfde patroon: `BackgroundService` + `PeriodicTimer` +
`IServiceScopeFactory` (één scope per tick, fouten gelogd zonder de lus te breken).
Tenant-aware sweeps itereren `Tenants.Where(IsActive)` en componeren hun services handmatig
met een `DevTenantContext(tenantId)` — de request-gebonden accessors zijn buiten een request
fail-closed (gevestigd patroon, zie `ExpiryNotificationProducer`). Deduplicatie loopt via
`Notification.DedupeKey` (onderdrukt zolang een onopgeloste melding met dezelfde sleutel
bestaat; oplossen herwapent) en via one-shot-stempels op de entiteit.

| Job | Interval | Doet |
|---|---|---|
| `OutboxDispatcherHostedService` | 30 s | e-mail/sms-outbox afleveren (bestaand) |
| `PeppolDispatcherHostedService` | 30 s | Peppol-transmissies (bestaand) |
| `CalendarSyncHostedService` | 60 s | agenda-sync (bestaand) |
| `ExpiryNotificationHostedService` | 6 u | kwalificatie-/documentvervaldata (bestaand) |
| `TokenRetentionHostedService` | 6 u | refresh-token-opruiming (bestaand) |
| `GdprRetentionHostedService` | 24 u | GDPR-retentie (bestaand) |
| **`InventorySweepHostedService`** | 1 u | zie hieronder |
| **`TaskSweepHostedService`** | 15 min | zie hieronder |
| **`NotificationMaintenanceHostedService`** | 6 u | zie hieronder |

## InventorySweep (per tenant)

1. **Statusreconciliatie** — `InventoryAlertService.SyncAsync` over elk voorraadgevolgd
   artikel/variant: vangnet bovenop de mutatiegedreven sync (herstelt drift, lost herstelde
   alerts op). Statusregels: zie docs/features/inventory-tasks-notifications-sprint.md §4.
2. **Retouren** — "retour binnenkort" (≤ 2 dagen, dedupe `return_due:{itemId}`), "retour te
   laat" (one-shot via `OverdueNotifiedAt` + dedupe `return_overdue:{itemId}`), en
   "materiaal bij vertrokken medewerker" (dedupe `loan_inactive:{employeeId}`).
3. **Escalaties** — `NegativeStockUnresolved` en `CriticalStockUnhandled` (alleen targets
   zónder open bestelvoorstel), gemeten vanaf `InventoryAlert.ActivatedAt` (per episode;
   herstel lost ook de escalatiesleutels op zodat een nieuwe episode opnieuw kan escaleren),
   en `ReturnOverdue`.

## TaskSweep (per tenant)

1. **Herhalingen** — `TaskRecurrenceGenerator.GenerateDueAsync(today)`; idempotent via de
   tenant-unieke `RecurrenceDedupeKey` per (herhaling, periode, sjabloonitem).
2. **Deadlines** — due-soon (< 24 u, stempel `DueSoonNotifiedAt`) naar de uitvoerder;
   overdue (stempel `OverdueNotifiedAt`) naar uitvoerder én opdrachtgever. Stempels worden
   gereset wanneer de deadline wijzigt.
3. **Geplande berichten** — interne berichten met `VisibleFrom` in de toekomst worden bij het
   opengaan van hun venster exact één keer aangekondigd (`InternalMessage.NotifiedAt`).
4. **Escalaties** — `TaskOverdue` en `AcknowledgementMissing` volgens `EscalationPolicy`.

## NotificationMaintenance (rij-gedreven, tenant-agnostisch)

Verlopen notificaties worden gearchiveerd; gearchiveerde notificaties ouder dan 180 dagen
worden soft-deleted. Batches van 500 per run.

## Escalatiebeleid (fase 16 — bewust géén workflow-engine)

`EscalationPolicy`: per tenant per soort één regel `{DelayHours, TargetPermissionCode,
IsActive}`; ontbrekende rijen materialiseren lazily met defaults
(`EscalationPolicyService.Defaults` — alleen `NegativeStockUnresolved` 24 u en `TaskOverdue`
48 u starten actief). Beheer via `GET/PUT /api/escalation-policies` (permissie
`escalations.manage`); het doel moet een bestaande cataloguspermissie zijn. Escalaties zijn
in-app-notificaties van het type `escalation_raised` (categorie Approval, ernst Critical) en
dedupen per episode (`escalation:{soort}:{entiteits-id}`).
