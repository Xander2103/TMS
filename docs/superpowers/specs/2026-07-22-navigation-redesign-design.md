# Navigation Redesign — Design

**Date:** 2026-07-22
**Status:** Approved (design), pending implementation plan
**Scope:** Frontend sidebar navigation (`TransportationService.Web`). No backend, no route changes.

## Problem

The sidebar has grown into a single flat list of ~30 links under one "Operaties"
label, plus separate "Beheer", "Stamgegevens", a portal block, and a footer — all in
one scrolling `<aside>`. It no longer communicates structure: a planner, HR employee,
warehouse worker, dispatcher, or accountant cannot tell at a glance where their work
lives. It also does not scale toward 100+ screens.

## Goal

Reorganise the existing routes into logical, collapsible **business modules** so the
navigation reads like a modern enterprise TMS/ERP (SAP / Dynamics / Oracle OTM) rather
than a long list of unrelated links.

**Hard constraints**

- Reuse the existing permission system verbatim (`hasAnyPermission`). UI filtering is
  UX only; the backend already enforces every endpoint.
- Do **not** remove any route. Only reorganise them.
- Data-driven and future-proof: adding a screen = adding one config line.

## Decisions (resolved during brainstorming)

1. **Search / favorites / recents** — the app already has all three, server-backed, in
   the `Ctrl+K` command palette (`CommandPalette.tsx`, `resourceLinksApi`). The sidebar
   will **not** duplicate them. It adds only a lightweight client-side **filter box**
   that narrows the visible menu; deep search/favorites/recents stay in `Ctrl+K`.
2. **Icons** — add the `lucide-react` dependency. One icon per module (and optional
   per-item icons), themed via `currentColor`.
3. **Items with no route yet** (Tachograaf, Leasing, Documenttypes, Contactafdelingen,
   Eenheden, "Overige lookups") — **omitted**. Only existing routes are reorganised.
   Tachograaf/Leasing remain as tabs on the vehicle/trailer detail page. Adding them to
   the menu later is a one-line config change.

## Module structure & route mapping

Ten modules. Every item keeps its **exact current permission list**. A module with zero
visible items for a given user is hidden entirely (no empty headers). Order below is the
render order.

### MIJN PORTAAL — icon `CircleUser` — *rendered only when `user.employeeId` is set; pinned at top*
| Label | Route | Permission |
|---|---|---|
| Mijn dashboard | `/portal` | (any signed-in employee) |
| Mijn planning | `/portal/planning` | — |
| Mijn afwezigheden | `/portal/absences` | — |
| Mijn kwalificaties | `/portal/qualifications` | — |
| Mijn profiel | `/portal/profile` | — |

### DASHBOARD — icon `LayoutDashboard`
| Label | Route | Permission |
|---|---|---|
| Dashboard | `/dashboard` | `dashboard.view` |
| KPI's | `/kpi` | `kpi.view` |
| Rendement | `/profitability` | `profitability.view` |
| Rapporten | `/reports` | `reports.view` |

### TRANSPORT — icon `ClipboardList`
| Label | Route | Permission |
|---|---|---|
| Transportopdrachten | `/transport-orders` | `orders.view`, `orders.manage` |
| Dossiers | `/dossiers` | `dossiers.view`, `dossiers.manage` |
| Planning | `/planning` | `planning.view` |
| Planbord | `/planning-center` | `planning.view` |
| Operationeel centrum | `/operations` | `operations.view` |
| Mijn ritten | `/my-trips` | `driver_workflow.view` |
| Chauffeursapp | `/driver` | `driver_workflow.view` |

### MAGAZIJN — icon `Warehouse`
| Label | Route | Permission |
|---|---|---|
| Magazijnen | `/warehouses` | `warehouse.view`, `warehouse.manage` |
| Magazijn | `/warehouse` | `warehouse.view` |
| Dockplanning | `/dock-planning` | `warehouse.view`, `warehouse.schedule` |
| Incidenten | `/incidents` | `incidents.view`, `incidents.manage` |
| Afwijkingen | `/exceptions` | `exceptions.view` |

### KLANTEN — icon `Contact`
| Label | Route | Permission |
|---|---|---|
| Klanten | `/customers` | `customers.view` |
| Klantportaal | `/customer-portal` | `customer_portal.view` |
| Facturen | `/invoices` | `invoices.view` |
| Kostentarieven | `/cost-rates` | `trip_costs.view`, `trip_costs.manage` |
| Verkooptarieven | `/rate-cards` | `tariffs.view`, `tariffs.manage` |

### PERSONEEL — icon `UsersRound`
| Label | Route | Permission |
|---|---|---|
| Medewerkers | `/employees` | `employees.view` |
| Personeelsplanning | `/employee-planning` | `employee_planning.view`, `employee_planning.manage` |
| Afwezigheden | `/absences` | `absences.view` |
| Kwalificaties | `/qualifications` | `employee_documents.view` |

> The former top-level **Chauffeurs** entry (`/employees?view=chauffeurs`) is **removed**.
> Driver profiles already live inside the employee dossier as tabs
> (`chauffeursprofiel` etc.), so nothing is lost. The `/drivers*` routes stay intact.

### VLOOT — icon `Truck`
| Label | Route | Permission |
|---|---|---|
| Vlootoverzicht | `/fleet` | `vehicles.view` |
| Voertuigen | `/vehicles` | `vehicles.view` |
| Opleggers | `/trailers` | `trailers.view` |
| Tankkaarten | `/tank-cards` | `tank_cards.view` |
| Onderhoud | `/maintenance-policies` | `maintenance_policies.view`, `maintenance_policies.manage` |
| Locaties | `/locations` | `locations.view` |

> Tachograaf & Leasing remain detail tabs on the vehicle/trailer pages — not menu items.

### COMMUNICATIE — icon `MessageSquare`
| Label | Route | Permission |
|---|---|---|
| Berichten | `/inbox` | (any signed-in user — unchanged) |
| Meldingen | `/notifications` | (any signed-in user) — **unread badge** |
| E-mail / SMS | `/messaging` | `messaging.manage` |
| EDI | `/edi` | `edi.manage` |
| Integraties | `/integrations` | `integrations.manage` |

### BEHEER — icon `Settings`
| Label | Route | Permission |
|---|---|---|
| Gebruikers | `/users` | `users.view` |
| Rollen & rechten | `/roles` | `roles.view` |
| Functie → rol | `/job-function-mappings` | `roles.view`, `roles.manage_permissions` |
| Instellingen | `/settings` | `company_settings.view`, `company_settings.manage` |

### STAMGEGEVENS — icon `Database` — *data-driven from `lookupRegistry.ts`*
Rendered as sub-headers. The lookup items come from `LOOKUP_RESOURCES` (adding a lookup
there makes it appear automatically); the two settings routes are appended to **Algemeen**
and **Templates**.

- **Algemeen**: Eigen bedrijven (`/settings/legal-entities`, `legal_entities.view|manage`),
  Afdelingen, Functies (registry group `organisatie`), Contracttypes, Talen, Nationaliteiten
  (registry group `referentie`).
- **Categorieën** (registry group `categorieen`): Klantcategorieën, Chauffeurcategorieën,
  Voertuigcategorieën, Opleggercategorieën.
- **Templates**: Bedrijfsmiddelen (sjablonen) (`/settings/issued-item-templates`,
  `issued_items.manage_templates`).

> The third sub-header stays **"Templates"** (not "Referentiegegevens"): after omitting
> not-yet-built items it contains only the issued-item **templates**, which is genuinely a
> template resource. If Documenttypes/Eenheden/etc. are added later and the section stops
> being template-only, revisit the label then.

## Behavior & UX

- **Collapsible modules.** Module header is a `<button aria-expanded aria-controls>` with
  icon + title + chevron. Click / Enter / Space toggles. Expansion animates via a
  grid-rows `0fr → 1fr` transition; disabled under `prefers-reduced-motion`.
- **Default state.** All modules collapsed **except the one containing the active route**.
  On navigation the active module auto-expands.
- **Persistence.** The set of expanded module ids is stored in `localStorage`
  (`nav.expanded.v1`) and restored on load. Manual expand/collapse persists.
- **Active highlighting.** The active item uses the existing `NavLink` active style; the
  containing module header also gets an active tint so the current location is visible even
  when other modules are collapsed.
- **Filter box.** A text input at the top (with a `Ctrl+K` affordance hint) filters visible
  item labels instantly. While filtering, modules with a match auto-expand and show only
  matching items; `Esc` clears. It is a quick narrowing aid — deep/global search stays in
  the `Ctrl+K` palette.
- **Badges.** The unread-notifications count renders on *Meldingen*. When COMMUNICATIE is
  collapsed and unread > 0, its header shows a small dot. The badge is a generic `badge`
  key in the item config, so future counters (e.g. open exceptions) are a one-line add.
- **Sticky & responsive.** Keep the sticky `<aside>` and the existing ≤900px off-canvas
  drawer + hamburger. Navigating from the drawer still closes it (`onNavigate`).
- **Accessibility.** Real `<a>` (NavLink) items stay tab-navigable; module headers are
  buttons with correct `aria-expanded`/`aria-controls`; chevron is decorative
  (`aria-hidden`). Focus-visible outlines retained.

## Architecture & files

Declarative and data-driven so the structure scales to 100+ screens.

| File | Responsibility |
|---|---|
| `src/components/layout/nav/navConfig.ts` | Single source of truth: `NavModule[]` with `id`, `label`, lucide `icon`, and `NavItem[]` (`label`, `to`, `permissions?`, optional item `icon`, optional `badge` key). STAMGEGEVENS composes from `lookupRegistry`. |
| `src/components/layout/nav/useNavState.ts` | Expanded-set hook (localStorage round-trip), active-module detection from `useLocation`, and filter matching. |
| `src/components/layout/nav/NavModule.tsx` | One collapsible module: a11y header button + animated region + item list. |
| `src/components/layout/nav/NavFilter.tsx` | The filter input. |
| `src/components/layout/Sidebar.tsx` | Refactored to compose the above. Retains `LegalEntitySwitcher`, the portal block, the user footer, and the unread-count poll. |
| `src/components/layout/nav.css` (+ trims to `Sidebar.css`) | Module/animation styling, reduced-motion, responsive. |
| `package.json` | Add `lucide-react`. |

**Unchanged:** `AppRoutes.tsx` (no route added/removed), the permission system, the
command palette, the shortcut registry, backend.

**Isolation.** `navConfig` (data), `useNavState` (state/logic), and `NavModule`/`NavFilter`
(presentation) each have one clear responsibility and can be tested independently.
`Sidebar` becomes a thin composition shell.

## Testing

Vitest + `@testing-library/react`, matching existing patterns:

- Permission filtering hides items and empties → module hidden when no visible items.
- `localStorage` persistence round-trip (expanded set survives remount).
- Active route → its module auto-expands and header shows active tint.
- Filter narrows items and auto-expands modules with a match; `Esc` resets.
- Notification badge renders from unread count; collapsed-module dot appears.
- Keyboard: Enter/Space toggles a module; `aria-expanded` reflects state.

## Out of scope

- Icon-only "mini rail" collapse of the whole sidebar (not requested).
- Duplicating favorites/recents into the sidebar (kept in `Ctrl+K`).
- New pages for the omitted menu items (Documenttypes, Eenheden, etc.).
