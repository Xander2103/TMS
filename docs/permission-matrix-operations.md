# Permissiematrix — operationele wave (2026-07-21)

## Nieuwe permissiecodes

| Code | Omschrijving | Gebruikt door |
|---|---|---|
| `operations.view` | Operationeel controlecentrum bekijken | `GET /api/operations/overview`, `GET /api/operations/alerts`, chauffeursincident-notificaties |
| `operations.manage_alerts` | Meldingen bevestigen/toewijzen/afhandelen | `POST /api/operations/alerts/{id}/acknowledge|resolve|assign` |
| `warehouse.manage` | Magazijnen en docks beheren | `POST/PUT /api/warehouses*`, dock-CRUD |
| `warehouse.schedule` | Dockafspraken plannen en verplaatsen | `POST/PUT/DELETE /api/dock-appointments*` |
| `warehouse.conflict_override` | Blokkerende dockconflicten overschrijven (reden verplicht) | override-gate in DockPlanningController |
| `profitability.export` | Rendementsrapporten exporteren (XLSX) | `GET /api/profitability/export` |

## Bewust HERGEBRUIKT in plaats van nieuw

Planbord = bestaande `planning.view/create/edit` + `orders.assign` +
`planning.override_restriction` (overschrijven met verplichte reden). Chauffeursapp =
`driver_workflow.view/execute` + `scanning.*` + `pod.finalize` + `exceptions.create`
(self-scoped endpoints; documenttoegang wordt door scoping, niet door extra codes
afgedwongen). Rendement = bestaande `profitability.view`; kostendetail `trip_costs.view`;
correcties `trip_costs.manage`/`trip_costs.override`. Favorieten/recent/vastgepind zijn
self-scoped (/api/me, auth-only) met per-type permissie-hercontrole bij het tonen —
bewust geen aparte codes.

## Roltemplates (DefaultRoleUpgrades v8)

Bestaande tenants krijgen bij de volgende start automatisch stap 8 (éénmalig, add-only,
gematcht op TemplateCode; maatwerk blijft intact):

- **planner**: operations.view, operations.manage_alerts, warehouse.schedule, warehouse.conflict_override
- **dispatcher**: operations.view, operations.manage_alerts, warehouse.schedule
- **management**: operations.view, profitability.export
- **boekhouding**: profitability.export
- **magazijn**: operations.view, warehouse.manage, warehouse.schedule, warehouse.conflict_override

`DefaultRoleDefinitions` is gelijklopend bijgewerkt voor nieuwe tenants; Administrator
(systeemrol) krijgt zoals altijd automatisch de volledige catalogus via
`PermissionCatalogSeeder`.

## Upgradestappen

1. `dotnet ef database update` (additieve migraties `OperationalFoundations` en
   `Warehousing`).
2. API starten: de permissiecatalogus synchroniseert en roltemplate-stap v8 wordt per
   tenant precies één keer toegepast (`role_template_states`).
3. Geen handmatige datamigratie nodig; bestaande orders krijgen prioriteit "Normal".

## Nieuwe endpointgroepen (backend-afgedwongen)

| Groep | Endpoints | Permissie |
|---|---|---|
| Planbord | `GET /api/planning-board`, `/unplanned-orders`, `/resources` | planning.view |
| Gerichte ritcommando's | `POST/DELETE /api/trips/{id}/orders*`, `PUT {id}/driver|vehicle|trailer`, `POST {id}/reschedule`, `POST {id}/validate-assignment` | planning.edit (+ orders.assign voor orders; override-gate planning.override_restriction) |
| Operationeel centrum | `GET /api/operations/overview|alerts`, `POST alerts/{id}/…` | operations.view / operations.manage_alerts |
| Chauffeursapp | `GET /api/my/dashboard|documents(+download)`, `GET/POST /api/my/incidents` | driver_workflow.view / execute (self-scoped) |
| Rendement | `GET /api/profitability/trips|grouped|trips/{id}/explanation|export` | profitability.view / profitability.export |
| Magazijnen | `GET/POST/PUT /api/warehouses*` | warehouse.view / warehouse.manage |
| Dockplanning | `GET /api/dock-appointments/board|dashboard`, `POST/PUT/DELETE …` | warehouse.view / warehouse.schedule (+ conflict_override-gate) |
| Voorkeuren | `GET/PUT/DELETE /api/me/resource-links`, `POST …/order` | auth-only, self-scoped |

Idempotente endpoints (client-sleutel, replay = huidige staat): stopovergangen
(`ClientRequestId`), stop afronden/overslaan, POD-afronding, incident aanmaken, scans
(bestaand `ClientEventId`).
