# Permissies — sprint 2026-08-01 (inventory / tasks / notifications / portaal)

Volledige catalogus: `Modules/Identity/PermissionCodes.cs` (autoritatief, geseed door
`PermissionCatalogSeeder`). Role-template-defaults: `Data/DefaultRoleDefinitions.cs` +
versiestappen in `Data/DefaultRoleUpgrades.cs` (huidige versie: **30**). Guards:
`Phase8SupplyChainTests` (elke cataloguscode wordt ergens afgedwongen),
`Phase10SystematicSecurityTests` (elk endpoint bewust geclassificeerd),
`DefaultRoleSeederTests.Version23_…`/`Version24_…`/`Version25_…` (positieve én negatieve
grants per template).

## Nieuw in deze sprint

| Code | Betekenis | Default (v23) |
|---|---|---|
| `inventory.manage_thresholds` | drempels/target/verpakking + negatieve-voorraadbeleid | management |
| `inventory.view_movements` | mutatiehistoriek | magazijn, management |
| `inventory.reorder_view` / `reorder_manage` | bestelvoorstellen zien / aanmaken+behandelen | magazijn, management |
| `inventory.loans_view` | uitleningen/retourtermijnen | magazijn, management, hr |
| `tasks.view_own` / `manage_own` | eigen taken | alle medewerker-templates |
| `tasks.view_team` | taken eigen afdeling | hr, management |
| `tasks.view_all` | alle taken | management |
| `tasks.assign` | toewijzen/herverdelen (zonder view_all: eigen afdeling) | hr, management |
| `tasks.edit` / `cancel` / `review` / `reopen` | beheer | management (review ook hr) |
| `tasks.manage_categories` / `manage_templates` / `manage_recurring` | inrichting | management |
| `messages.send_bulk` | rol/afdeling/iedereen (service-side gate) | hr, management |
| `messages.view_delivery_status` / `messages.cancel` | bezorgstatus / intrekken van anderen | management (status ook hr) |
| `portal_messages.view` / `send` / `send_bulk` / `cancel` | portaalberichten | management |
| `escalations.manage` | escalatieregels | management |

Bestaand hergebruikt: `inventory.view/adjust/manage/override_negative_stock/
low_stock_alerts`, `issued_items.*`, `messages.send`, `notification_rules.view/manage`,
`customer_portal.*`.

## Latere versiestappen

| Versie | Code | Betekenis | Default |
|---|---|---|---|
| 24 | `orders.confirm_incomplete_price` | prijs bevestigen met ongeprijsde goederenlijnen (reden verplicht) | management, boekhouding |
| 25 | `locations.view_sensitive` | toegangscodes en gevoelige locatiegegevens bekijken/bewerken (naast bestaande `locations.view/create/edit/delete`); zonder deze permissie blijft de opgeslagen toegangscode bij een update onaangeroerd en is het veld in de detailrespons null | planner, dispatcher, management |
| 26 | `activity_types.view` / `manage` | tenant-activiteitstypecatalogus | planner/dispatcher/management (manage: management) |
| 27 | `dossiers.override_entity` | afwijkende uitgevende entiteit (reden verplicht) | management, boekhouding |
| 28 | `problems.approve_charge` | doorrekenen van een probleem goedkeuren | management, boekhouding |
| 29 | `system_info.view` / `backups.view` | systeeminformatie + back-upoverzicht (acties bewust per persoon) | management |
| 30 | `attendance.self` | eigen urenregistratie: punchen, status, historiek, driver-day | alle interne medewerker-templates incl. chauffeur |
| 30 | `attendance.view` | aanwezigheid + urenregistratie van medewerkers bekijken | hr, management (dispatcher BEWUST niet) |
| 30 | `attendance.correct` | correcties/annulering/manuele sessies (reden verplicht, audit) | hr |
| 30 | `attendance.report` | rapport + XLSX-export | hr, management |
| 30 | `attendance.manage_credentials` | prikklokcodes (PIN) beheren — nooit uitlezen | hr |
| 30 | `attendance.manage_settings` | urenregistratie-instellingen | hr |
| 30 | `attendance.manage_kiosks` | prikklok-devices provisionen/roteren/uitschakelen | geen template — administrator kent per persoon toe |

## Bewuste beslissingen

- **Geen `notifications.view_own`-familie**: de notificatie-endpoints zijn self-scoped
  `[Authorize]` (gereviewde allowlist in Phase10) — een permissie die iedereen per definitie
  heeft is catalogusruis (sprintdocument A7).
- **Magazijnier** krijgt bewust géén `inventory.manage_thresholds` en géén
  `inventory.override_negative_stock`: gewone uitgifte en bestelvoorstellen wél, negatieve
  voorraad definitief bevestigen niet.
- **HR** ziet niet automatisch alle operationele taken (`tasks.view_all` alleen management);
  teamcoördinatie loopt via `tasks.view_team` + `tasks.assign`.
- **Chauffeur**: alleen eigen taken/berichten/toegewezen materiaal.
- **Klantportaalrollen** krijgen niets uit deze sprint: portaalfeed en taalvoorkeur zijn
  self-scoped onder de bestaande `customer_portal.view`.
- Service-side gates (bulk-targeting, negatieve-voorraadbevestiging) staan geregistreerd in
  `Phase8SupplyChainTests.ServiceSideEnforcedCodes` met hun enforcement-site.
