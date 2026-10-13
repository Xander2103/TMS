# Navigation Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the flat sidebar link-list with collapsible, business-oriented modules that reorganise every existing route without removing any, scaling toward 100+ screens.

**Architecture:** A declarative `navConfig` (single source of truth) feeds pure state helpers (`navState.ts`), a per-user persisted expand hook (`useExpandedModules.ts`), and dumb presentational components (`NavModule`, `NavItemRow`, `NavFilter`). `Sidebar.tsx` becomes a thin composition shell. Permission filtering reuses `hasAnyPermission` exactly as today; the backend still enforces every endpoint.

**Tech Stack:** React 19, react-router-dom 7, TypeScript, Vitest + @testing-library/react, `lucide-react` (new dependency) for icons.

## Global Constraints

- **No route added or removed.** `AppRoutes.tsx` is not touched. Only the sidebar reorganises existing routes.
- **Permissions unchanged.** Every item carries its current any-of permission list; `hasAnyPermission` is the only gate. UI filtering is UX, never security.
- **Every screen belongs to exactly one module.** No duplicate navigation entries.
- **Dutch UI copy** (labels exactly as specified in the spec).
- **Icons via `lucide-react`**, sized/themed with `currentColor`.
- **Config supports nested submenus** (`NavItem.children`) so future nesting needs no redesign.
- **Per-user persistence:** expanded-module state keyed by `user.id` in `localStorage`.
- **Accessible & responsive:** module headers are `<button aria-expanded aria-controls>`; keep the existing ≤900px off-canvas drawer.
- Spec: `docs/superpowers/specs/2026-07-22-navigation-redesign-design.md`.

## File Structure

| File | Responsibility |
|---|---|
| `src/components/layout/nav/navConfig.ts` | **Create.** Types (`NavItem`, `NavSubgroup`, `NavModule`) + `getNavModules()` (static 9 modules + master-data module built from `lookupRegistry`). |
| `src/components/layout/nav/navState.ts` | **Create.** Pure helpers: `findActiveModuleId`, `filterModule`, `moduleHasUnread`. |
| `src/components/layout/nav/useExpandedModules.ts` | **Create.** Per-user expanded-set hook (localStorage + auto-expand active). |
| `src/components/layout/nav/NavModule.tsx` | **Create.** One collapsible module + recursive `NavItemRow` (nested-submenu ready). |
| `src/components/layout/nav/NavFilter.tsx` | **Create.** Filter text input. |
| `src/components/layout/nav.css` | **Create.** Module/item styling, animation, reduced-motion, responsive. |
| `src/components/layout/Sidebar.tsx` | **Modify.** Compose the above; keep `LegalEntitySwitcher`, portal handling via config, user footer, unread poll. |
| `src/components/layout/Sidebar.css` | **Modify.** Remove now-unused flat-list rules; keep shell/user-footer rules. |
| `package.json` | **Modify.** Add `lucide-react`. |

Tests live in `src/components/layout/nav/__tests__/` and `src/components/layout/__tests__/`.

---

### Task 1: Add the `lucide-react` dependency

**Files:**
- Modify: `TransportationService.Web/package.json`

**Interfaces:**
- Produces: `lucide-react` importable (`import { Truck } from 'lucide-react'`), providing the `LucideIcon` type and named icon components used by every later task.

- [ ] **Step 1: Install the dependency**

Run (from `TransportationService.Web/`):
```bash
npm install lucide-react
```
Expected: `package.json` gains `"lucide-react": "^x.y.z"` under `dependencies`; `package-lock.json` updated; no errors.

- [ ] **Step 2: Verify it imports and type-checks**

Run:
```bash
npx tsc --noEmit
```
Expected: exits 0 (no new type errors). If the repo has no `tsc` script, this direct invocation still validates.

- [ ] **Step 3: Commit**

```bash
git add package.json package-lock.json
git commit -m "build: add lucide-react for navigation icons"
```

---

### Task 2: `navConfig.ts` — declarative module structure

**Files:**
- Create: `src/components/layout/nav/navConfig.ts`
- Test: `src/components/layout/nav/__tests__/navConfig.test.ts`

**Interfaces:**
- Consumes: `LOOKUP_RESOURCES`, `LookupGroup` from `../../../features/master-data/lookupRegistry`; `LucideIcon` + named icons from `lucide-react`.
- Produces:
  - `type BadgeKey = 'notifications'`
  - `interface NavItem { label: string; to: string; permissions?: string[]; icon?: LucideIcon; badge?: BadgeKey; children?: NavItem[] }`
  - `interface NavSubgroup { label: string; items: NavItem[] }`
  - `interface NavModule { id: string; label: string; icon: LucideIcon; requiresEmployee?: boolean; items?: NavItem[]; subgroups?: NavSubgroup[] }`
  - `function getNavModules(): NavModule[]`

- [ ] **Step 1: Write the failing test**

```ts
// src/components/layout/nav/__tests__/navConfig.test.ts
import { describe, expect, it } from 'vitest'
import { getNavModules } from '../navConfig'

describe('getNavModules', () => {
  const modules = getNavModules()

  it('returns the ten modules in business order', () => {
    expect(modules.map((m) => m.id)).toEqual([
      'portaal', 'dashboard', 'transport', 'magazijn', 'klanten',
      'personeel', 'vloot', 'communicatie', 'beheer', 'stamgegevens',
    ])
  })

  it('flags the portal module as employee-only', () => {
    expect(modules.find((m) => m.id === 'portaal')?.requiresEmployee).toBe(true)
  })

  it('renames the personnel list to "Medewerkers" and drops the top-level Chauffeurs entry', () => {
    const personeel = modules.find((m) => m.id === 'personeel')!
    const labels = personeel.items!.map((i) => i.label)
    expect(labels).toContain('Medewerkers')
    expect(labels).not.toContain('Chauffeurs')
  })

  it('labels /warehouse as "Magazijn" and keeps /warehouses as "Magazijnen"', () => {
    const magazijn = modules.find((m) => m.id === 'magazijn')!
    const byRoute = new Map(magazijn.items!.map((i) => [i.to, i.label]))
    expect(byRoute.get('/warehouse')).toBe('Magazijn')
    expect(byRoute.get('/warehouses')).toBe('Magazijnen')
  })

  it('marks Meldingen with the notifications badge', () => {
    const comms = modules.find((m) => m.id === 'communicatie')!
    expect(comms.items!.find((i) => i.to === '/notifications')?.badge).toBe('notifications')
  })

  it('builds Stamgegevens from the lookup registry with three subgroups', () => {
    const master = modules.find((m) => m.id === 'stamgegevens')!
    expect(master.subgroups!.map((s) => s.label)).toEqual(['Algemeen', 'Categorieën', 'Templates'])
    const categorieen = master.subgroups!.find((s) => s.label === 'Categorieën')!
    expect(categorieen.items.map((i) => i.to)).toContain('/master-data/customer-categories')
    const algemeen = master.subgroups!.find((s) => s.label === 'Algemeen')!
    expect(algemeen.items[0].to).toBe('/settings/legal-entities')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/components/layout/nav/__tests__/navConfig.test.ts`
Expected: FAIL — cannot find module `../navConfig`.

- [ ] **Step 3: Write the implementation**

```ts
// src/components/layout/nav/navConfig.ts
import {
  ClipboardList, Contact, CircleUser, Database, LayoutDashboard,
  MessageSquare, Settings, Truck, UsersRound, Warehouse, type LucideIcon,
} from 'lucide-react'
import { LOOKUP_RESOURCES, type LookupGroup } from '../../../features/master-data/lookupRegistry'

export type BadgeKey = 'notifications'

export interface NavItem {
  label: string
  to: string
  /** Any-of permissions required to see this entry; omitted = visible to every signed-in user. */
  permissions?: string[]
  icon?: LucideIcon
  badge?: BadgeKey
  /** Future nested submenu — rendered as an inner collapsible group. */
  children?: NavItem[]
}

export interface NavSubgroup {
  label: string
  items: NavItem[]
}

export interface NavModule {
  id: string
  label: string
  icon: LucideIcon
  /** Rendered only when the signed-in user has an employee link. */
  requiresEmployee?: boolean
  items?: NavItem[]
  subgroups?: NavSubgroup[]
}

/** Lookup resources of one registry group as nav items (view+manage = any-of). */
function lookupItems(group: LookupGroup): NavItem[] {
  return LOOKUP_RESOURCES.filter((r) => r.group === group).map((r) => ({
    label: r.title,
    to: `/master-data/${r.slug}`,
    permissions: [r.viewPermission, r.managePermission],
  }))
}

/** Stamgegevens is data-driven: adding a lookup to the registry makes it appear here. */
function masterDataModule(): NavModule {
  return {
    id: 'stamgegevens',
    label: 'Stamgegevens',
    icon: Database,
    subgroups: [
      {
        label: 'Algemeen',
        items: [
          { label: 'Eigen bedrijven', to: '/settings/legal-entities', permissions: ['legal_entities.view', 'legal_entities.manage'] },
          ...lookupItems('organisatie'),
          ...lookupItems('referentie'),
        ],
      },
      { label: 'Categorieën', items: lookupItems('categorieen') },
      {
        // Truly template-only after omitting not-yet-built items, so it stays "Templates".
        label: 'Templates',
        items: [
          { label: 'Bedrijfsmiddelen (sjablonen)', to: '/settings/issued-item-templates', permissions: ['issued_items.manage_templates'] },
        ],
      },
    ],
  }
}

export function getNavModules(): NavModule[] {
  return [
    {
      id: 'portaal',
      label: 'Mijn portaal',
      icon: CircleUser,
      requiresEmployee: true,
      items: [
        { label: 'Mijn dashboard', to: '/portal' },
        { label: 'Mijn planning', to: '/portal/planning' },
        { label: 'Mijn afwezigheden', to: '/portal/absences' },
        { label: 'Mijn kwalificaties', to: '/portal/qualifications' },
        { label: 'Mijn profiel', to: '/portal/profile' },
      ],
    },
    {
      id: 'dashboard',
      label: 'Dashboard',
      icon: LayoutDashboard,
      items: [
        { label: 'Dashboard', to: '/dashboard', permissions: ['dashboard.view'] },
        { label: "KPI's", to: '/kpi', permissions: ['kpi.view'] },
        { label: 'Rendement', to: '/profitability', permissions: ['profitability.view'] },
        { label: 'Rapporten', to: '/reports', permissions: ['reports.view'] },
      ],
    },
    {
      id: 'transport',
      label: 'Transport',
      icon: ClipboardList,
      items: [
        { label: 'Transportopdrachten', to: '/transport-orders', permissions: ['orders.view', 'orders.manage'] },
        { label: 'Dossiers', to: '/dossiers', permissions: ['dossiers.view', 'dossiers.manage'] },
        { label: 'Planning', to: '/planning', permissions: ['planning.view'] },
        { label: 'Planbord', to: '/planning-center', permissions: ['planning.view'] },
        { label: 'Operationeel centrum', to: '/operations', permissions: ['operations.view'] },
        { label: 'Mijn ritten', to: '/my-trips', permissions: ['driver_workflow.view'] },
        { label: 'Chauffeursapp', to: '/driver', permissions: ['driver_workflow.view'] },
      ],
    },
    {
      id: 'magazijn',
      label: 'Magazijn',
      icon: Warehouse,
      items: [
        { label: 'Magazijnen', to: '/warehouses', permissions: ['warehouse.view', 'warehouse.manage'] },
        { label: 'Magazijn', to: '/warehouse', permissions: ['warehouse.view'] },
        { label: 'Dockplanning', to: '/dock-planning', permissions: ['warehouse.view', 'warehouse.schedule'] },
        { label: 'Incidenten', to: '/incidents', permissions: ['incidents.view', 'incidents.manage'] },
        { label: 'Afwijkingen', to: '/exceptions', permissions: ['exceptions.view'] },
      ],
    },
    {
      id: 'klanten',
      label: 'Klanten',
      icon: Contact,
      items: [
        { label: 'Klanten', to: '/customers', permissions: ['customers.view'] },
        { label: 'Klantportaal', to: '/customer-portal', permissions: ['customer_portal.view'] },
        { label: 'Facturen', to: '/invoices', permissions: ['invoices.view'] },
        { label: 'Kostentarieven', to: '/cost-rates', permissions: ['trip_costs.view', 'trip_costs.manage'] },
        { label: 'Verkooptarieven', to: '/rate-cards', permissions: ['tariffs.view', 'tariffs.manage'] },
      ],
    },
    {
      id: 'personeel',
      label: 'Personeel',
      icon: UsersRound,
      items: [
        { label: 'Medewerkers', to: '/employees', permissions: ['employees.view'] },
        { label: 'Personeelsplanning', to: '/employee-planning', permissions: ['employee_planning.view', 'employee_planning.manage'] },
        { label: 'Afwezigheden', to: '/absences', permissions: ['absences.view'] },
        { label: 'Kwalificaties', to: '/qualifications', permissions: ['employee_documents.view'] },
      ],
    },
    {
      id: 'vloot',
      label: 'Vloot',
      icon: Truck,
      items: [
        { label: 'Vlootoverzicht', to: '/fleet', permissions: ['vehicles.view'] },
        { label: 'Voertuigen', to: '/vehicles', permissions: ['vehicles.view'] },
        { label: 'Opleggers', to: '/trailers', permissions: ['trailers.view'] },
        { label: 'Tankkaarten', to: '/tank-cards', permissions: ['tank_cards.view'] },
        { label: 'Onderhoud', to: '/maintenance-policies', permissions: ['maintenance_policies.view', 'maintenance_policies.manage'] },
        { label: 'Locaties', to: '/locations', permissions: ['locations.view'] },
      ],
    },
    {
      id: 'communicatie',
      label: 'Communicatie',
      icon: MessageSquare,
      items: [
        { label: 'Berichten', to: '/inbox' },
        { label: 'Meldingen', to: '/notifications', badge: 'notifications' },
        { label: 'E-mail / SMS', to: '/messaging', permissions: ['messaging.manage'] },
        { label: 'EDI', to: '/edi', permissions: ['edi.manage'] },
        { label: 'Integraties', to: '/integrations', permissions: ['integrations.manage'] },
      ],
    },
    {
      id: 'beheer',
      label: 'Beheer',
      icon: Settings,
      items: [
        { label: 'Gebruikers', to: '/users', permissions: ['users.view'] },
        { label: 'Rollen & rechten', to: '/roles', permissions: ['roles.view'] },
        { label: 'Functie → rol', to: '/job-function-mappings', permissions: ['roles.view', 'roles.manage_permissions'] },
        { label: 'Instellingen', to: '/settings', permissions: ['company_settings.view', 'company_settings.manage'] },
      ],
    },
    masterDataModule(),
  ]
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/components/layout/nav/__tests__/navConfig.test.ts`
Expected: PASS (all 6 assertions green).

- [ ] **Step 5: Commit**

```bash
git add src/components/layout/nav/navConfig.ts src/components/layout/nav/__tests__/navConfig.test.ts
git commit -m "feat(nav): declarative module config for the sidebar"
```

---

### Task 3: `navState.ts` — pure active-module, filter, and badge helpers

**Files:**
- Create: `src/components/layout/nav/navState.ts`
- Test: `src/components/layout/nav/__tests__/navState.test.ts`

**Interfaces:**
- Consumes: `NavModule`, `NavItem`, `NavSubgroup` from `./navConfig`.
- Produces:
  - `interface VisibleModule { module: NavModule; items: NavItem[]; subgroups: NavSubgroup[] }`
  - `function findActiveModuleId(modules: NavModule[], pathname: string): string | null`
  - `function filterModule(module: NavModule, opts: { hasAnyPermission: (codes: string[]) => boolean; hasEmployee: boolean; query: string }): VisibleModule | null`
  - `function moduleHasUnread(vm: VisibleModule, unreadCount: number): boolean`

- [ ] **Step 1: Write the failing test**

```ts
// src/components/layout/nav/__tests__/navState.test.ts
import { describe, expect, it } from 'vitest'
import { getNavModules } from '../navConfig'
import { filterModule, findActiveModuleId, moduleHasUnread } from '../navState'

const modules = getNavModules()
const allowAll = () => true

describe('findActiveModuleId', () => {
  it('matches a nested detail route to its module (longest prefix)', () => {
    expect(findActiveModuleId(modules, '/employees/abc-123')).toBe('personeel')
  })
  it('does not confuse /warehouses with /warehouse', () => {
    expect(findActiveModuleId(modules, '/warehouses')).toBe('magazijn')
    expect(findActiveModuleId(modules, '/warehouse')).toBe('magazijn')
  })
  it('returns null for an unknown route', () => {
    expect(findActiveModuleId(modules, '/nowhere')).toBeNull()
  })
})

describe('filterModule', () => {
  const transport = modules.find((m) => m.id === 'transport')!
  const portaal = modules.find((m) => m.id === 'portaal')!

  it('drops items the user lacks permission for and hides an emptied module', () => {
    const none = () => false
    expect(filterModule(transport, { hasAnyPermission: none, hasEmployee: false, query: '' })).toBeNull()
  })

  it('keeps only permitted items', () => {
    const only = (codes: string[]) => codes.includes('planning.view')
    const vm = filterModule(transport, { hasAnyPermission: only, hasEmployee: false, query: '' })!
    expect(vm.items.map((i) => i.to)).toEqual(['/planning', '/planning-center'])
  })

  it('hides an employee-only module when the user has no employee link', () => {
    expect(filterModule(portaal, { hasAnyPermission: allowAll, hasEmployee: false, query: '' })).toBeNull()
    expect(filterModule(portaal, { hasAnyPermission: allowAll, hasEmployee: true, query: '' })).not.toBeNull()
  })

  it('narrows items by case-insensitive query and drops non-matching modules', () => {
    const vm = filterModule(transport, { hasAnyPermission: allowAll, hasEmployee: false, query: 'plan' })!
    expect(vm.items.map((i) => i.label)).toEqual(['Planning', 'Planbord'])
    expect(filterModule(transport, { hasAnyPermission: allowAll, hasEmployee: false, query: 'zzz' })).toBeNull()
  })

  it('filters subgroup items and drops emptied subgroups', () => {
    const master = modules.find((m) => m.id === 'stamgegevens')!
    const vm = filterModule(master, { hasAnyPermission: allowAll, hasEmployee: false, query: 'categorie' })!
    expect(vm.subgroups.map((s) => s.label)).toEqual(['Categorieën'])
  })
})

describe('moduleHasUnread', () => {
  it('is true only when a badged item exists and the count is positive', () => {
    const comms = filterModule(modules.find((m) => m.id === 'communicatie')!, { hasAnyPermission: allowAll, hasEmployee: false, query: '' })!
    expect(moduleHasUnread(comms, 3)).toBe(true)
    expect(moduleHasUnread(comms, 0)).toBe(false)
    const transport = filterModule(modules.find((m) => m.id === 'transport')!, { hasAnyPermission: allowAll, hasEmployee: false, query: '' })!
    expect(moduleHasUnread(transport, 3)).toBe(false)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/components/layout/nav/__tests__/navState.test.ts`
Expected: FAIL — cannot find module `../navState`.

- [ ] **Step 3: Write the implementation**

```ts
// src/components/layout/nav/navState.ts
import type { NavItem, NavModule, NavSubgroup } from './navConfig'

export interface VisibleModule {
  module: NavModule
  items: NavItem[]
  subgroups: NavSubgroup[]
}

interface FlatEntry {
  moduleId: string
  to: string
}

function flatten(modules: NavModule[]): FlatEntry[] {
  const out: FlatEntry[] = []
  const walk = (moduleId: string, items: NavItem[] | undefined) => {
    for (const item of items ?? []) {
      out.push({ moduleId, to: item.to })
      if (item.children) walk(moduleId, item.children)
    }
  }
  for (const m of modules) {
    walk(m.id, m.items)
    for (const sg of m.subgroups ?? []) walk(m.id, sg.items)
  }
  return out
}

/** Longest-prefix match of the current pathname against every item route. */
export function findActiveModuleId(modules: NavModule[], pathname: string): string | null {
  let best: FlatEntry | null = null
  for (const entry of flatten(modules)) {
    const matches = pathname === entry.to || pathname.startsWith(`${entry.to}/`)
    if (matches && (!best || entry.to.length > best.to.length)) best = entry
  }
  return best?.moduleId ?? null
}

function normalise(query: string): string {
  return query.trim().toLowerCase()
}

/** Item is visible if permitted AND (no query, or self/descendant label matches). */
function keepItem(
  item: NavItem,
  hasAnyPermission: (codes: string[]) => boolean,
  q: string,
): NavItem | null {
  if (item.permissions && !hasAnyPermission(item.permissions)) return null
  const children = (item.children ?? [])
    .map((c) => keepItem(c, hasAnyPermission, q))
    .filter((c): c is NavItem => c !== null)
  const selfMatches = q.length === 0 || item.label.toLowerCase().includes(q)
  if (!selfMatches && children.length === 0) return null
  return item.children ? { ...item, children } : item
}

export function filterModule(
  module: NavModule,
  opts: { hasAnyPermission: (codes: string[]) => boolean; hasEmployee: boolean; query: string },
): VisibleModule | null {
  if (module.requiresEmployee && !opts.hasEmployee) return null
  const q = normalise(opts.query)

  const items = (module.items ?? [])
    .map((i) => keepItem(i, opts.hasAnyPermission, q))
    .filter((i): i is NavItem => i !== null)

  const subgroups = (module.subgroups ?? [])
    .map((sg) => ({
      label: sg.label,
      items: sg.items
        .map((i) => keepItem(i, opts.hasAnyPermission, q))
        .filter((i): i is NavItem => i !== null),
    }))
    .filter((sg) => sg.items.length > 0)

  if (items.length === 0 && subgroups.length === 0) return null
  return { module, items, subgroups }
}

export function moduleHasUnread(vm: VisibleModule, unreadCount: number): boolean {
  if (unreadCount <= 0) return false
  const has = (items: NavItem[]): boolean =>
    items.some((i) => i.badge === 'notifications' || (i.children ? has(i.children) : false))
  return has(vm.items) || vm.subgroups.some((sg) => has(sg.items))
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/components/layout/nav/__tests__/navState.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/components/layout/nav/navState.ts src/components/layout/nav/__tests__/navState.test.ts
git commit -m "feat(nav): pure active-module, permission/query filter, badge helpers"
```

---

### Task 4: `useExpandedModules.ts` — per-user persisted expand state

**Files:**
- Create: `src/components/layout/nav/useExpandedModules.ts`
- Test: `src/components/layout/nav/__tests__/useExpandedModules.test.tsx`

**Interfaces:**
- Produces: `function useExpandedModules(userId: string | null, activeModuleId: string | null): { isExpanded: (id: string) => boolean; toggle: (id: string) => void }`
- Behaviour: localStorage key `nav.expanded.<userId ?? 'anon'>.v1`. On first mount with no stored value, the active module starts expanded. Navigation to a new active module auto-expands it (without collapsing others). `toggle` flips one module and persists.

- [ ] **Step 1: Write the failing test**

```tsx
// src/components/layout/nav/__tests__/useExpandedModules.test.tsx
import { beforeEach, describe, expect, it } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useExpandedModules } from '../useExpandedModules'

describe('useExpandedModules', () => {
  beforeEach(() => window.localStorage.clear())

  it('expands the active module by default when nothing is stored', () => {
    const { result } = renderHook(() => useExpandedModules('u1', 'vloot'))
    expect(result.current.isExpanded('vloot')).toBe(true)
    expect(result.current.isExpanded('klanten')).toBe(false)
  })

  it('toggles a module and persists to a per-user key', () => {
    const { result } = renderHook(() => useExpandedModules('u1', 'vloot'))
    act(() => result.current.toggle('klanten'))
    expect(result.current.isExpanded('klanten')).toBe(true)
    const stored = JSON.parse(window.localStorage.getItem('nav.expanded.u1.v1')!)
    expect(stored).toContain('klanten')
  })

  it('restores stored state instead of the active default', () => {
    window.localStorage.setItem('nav.expanded.u2.v1', JSON.stringify(['beheer']))
    const { result } = renderHook(() => useExpandedModules('u2', 'vloot'))
    expect(result.current.isExpanded('beheer')).toBe(true)
    // Active module still auto-expands on top of stored state.
    expect(result.current.isExpanded('vloot')).toBe(true)
  })

  it('keeps separate state per user id', () => {
    window.localStorage.setItem('nav.expanded.u1.v1', JSON.stringify(['klanten']))
    const { result } = renderHook(() => useExpandedModules('u2', null))
    expect(result.current.isExpanded('klanten')).toBe(false)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/components/layout/nav/__tests__/useExpandedModules.test.tsx`
Expected: FAIL — cannot find module `../useExpandedModules`.

- [ ] **Step 3: Write the implementation**

```ts
// src/components/layout/nav/useExpandedModules.ts
import { useCallback, useEffect, useState } from 'react'

function storageKey(userId: string | null): string {
  return `nav.expanded.${userId ?? 'anon'}.v1`
}

function readStored(key: string): string[] | null {
  try {
    const raw = window.localStorage.getItem(key)
    return raw ? (JSON.parse(raw) as string[]) : null
  } catch {
    return null
  }
}

/**
 * Per-user set of expanded module ids. Persists to localStorage keyed by user id so two
 * accounts on the same browser keep independent state. The active module always expands.
 */
export function useExpandedModules(userId: string | null, activeModuleId: string | null) {
  const key = storageKey(userId)
  const [expanded, setExpanded] = useState<Set<string>>(() => {
    const stored = readStored(key)
    if (stored) return new Set(stored)
    return new Set(activeModuleId ? [activeModuleId] : [])
  })

  // Persist on every change (cheap; the set is tiny).
  useEffect(() => {
    try {
      window.localStorage.setItem(key, JSON.stringify([...expanded]))
    } catch {
      /* storage unavailable — expansion just won't persist */
    }
  }, [key, expanded])

  // Auto-expand the active module when navigation changes it. Same-ref return avoids churn.
  useEffect(() => {
    if (!activeModuleId) return
    setExpanded((prev) => (prev.has(activeModuleId) ? prev : new Set(prev).add(activeModuleId)))
  }, [activeModuleId])

  const toggle = useCallback((id: string) => {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }, [])

  return { isExpanded: (id: string) => expanded.has(id), toggle }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/components/layout/nav/__tests__/useExpandedModules.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/components/layout/nav/useExpandedModules.ts src/components/layout/nav/__tests__/useExpandedModules.test.tsx
git commit -m "feat(nav): per-user persisted module expand state"
```

---

### Task 5: `NavModule.tsx` + `NavItemRow` — collapsible module presentation

**Files:**
- Create: `src/components/layout/nav/NavModule.tsx`
- Test: `src/components/layout/nav/__tests__/NavModule.test.tsx`

**Interfaces:**
- Consumes: `VisibleModule` from `./navState`; `NavItem` from `./navConfig`.
- Produces:
  - `interface NavModuleProps { vm: VisibleModule; expanded: boolean; active: boolean; unreadCount: number; onToggle: (id: string) => void; onNavigate?: () => void }`
  - `function NavModule(props: NavModuleProps): JSX.Element`
- Renders a `<button aria-expanded aria-controls>` header (icon + title + optional collapsed dot + chevron) and a `role="region"` body (`inert` when collapsed) of `NavItemRow`s. Items with `children` render as an inner collapsible submenu (local state) — the future-proofing hook. Active items use the shared `nav-item active` class via `NavLink`. Badged items show the unread count.

- [ ] **Step 1: Write the failing test**

```tsx
// src/components/layout/nav/__tests__/NavModule.test.tsx
import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { Truck } from 'lucide-react'
import { NavModule } from '../NavModule'
import type { VisibleModule } from '../navState'

const vm: VisibleModule = {
  module: { id: 'vloot', label: 'Vloot', icon: Truck, items: [] },
  items: [
    { label: 'Voertuigen', to: '/vehicles' },
    { label: 'Meldingen', to: '/notifications', badge: 'notifications' },
  ],
  subgroups: [],
}

function renderModule(props: Partial<Parameters<typeof NavModule>[0]> = {}) {
  return render(
    <MemoryRouter initialEntries={['/vehicles']}>
      <NavModule vm={vm} expanded active={false} unreadCount={0} onToggle={vi.fn()} {...props} />
    </MemoryRouter>,
  )
}

describe('NavModule', () => {
  it('renders an accessible header reflecting expanded state', () => {
    renderModule({ expanded: false })
    const header = screen.getByRole('button', { name: /Vloot/ })
    expect(header).toHaveAttribute('aria-expanded', 'false')
  })

  it('toggles via the header button', async () => {
    const onToggle = vi.fn()
    renderModule({ onToggle })
    await userEvent.click(screen.getByRole('button', { name: /Vloot/ }))
    expect(onToggle).toHaveBeenCalledWith('vloot')
  })

  it('renders item links when expanded', () => {
    renderModule({ expanded: true })
    expect(screen.getByRole('link', { name: 'Voertuigen' })).toHaveAttribute('href', '/vehicles')
  })

  it('shows the unread badge on a badged item', () => {
    renderModule({ expanded: true, unreadCount: 5 })
    expect(screen.getByText('5')).toBeInTheDocument()
  })

  it('marks the header active when the active prop is set', () => {
    const { container } = renderModule({ active: true })
    expect(container.querySelector('.nav-module-active')).not.toBeNull()
  })

  it('renders a nested submenu for an item with children (future-proofing)', async () => {
    const nested: VisibleModule = {
      module: { id: 'x', label: 'X', icon: Truck, items: [] },
      items: [{ label: 'Ouder', to: '/parent', children: [{ label: 'Kind', to: '/parent/child' }] }],
      subgroups: [],
    }
    render(
      <MemoryRouter>
        <NavModule vm={nested} expanded active={false} unreadCount={0} onToggle={vi.fn()} />
      </MemoryRouter>,
    )
    const parentToggle = screen.getByRole('button', { name: /Ouder/ })
    expect(screen.queryByRole('link', { name: 'Kind' })).toBeNull()
    await userEvent.click(parentToggle)
    expect(screen.getByRole('link', { name: 'Kind' })).toHaveAttribute('href', '/parent/child')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/components/layout/nav/__tests__/NavModule.test.tsx`
Expected: FAIL — cannot find module `../NavModule`.

- [ ] **Step 3: Write the implementation**

```tsx
// src/components/layout/nav/NavModule.tsx
import { useState } from 'react'
import { ChevronDown } from 'lucide-react'
import { NavLink } from 'react-router-dom'
import type { NavItem } from './navConfig'
import { moduleHasUnread, type VisibleModule } from './navState'

interface NavItemRowProps {
  item: NavItem
  depth: number
  unreadCount: number
  onNavigate?: () => void
}

/** One row. With children it becomes a locally-collapsible submenu (nested-ready). */
function NavItemRow({ item, depth, unreadCount, onNavigate }: NavItemRowProps) {
  const [open, setOpen] = useState(false)
  const Icon = item.icon
  const badgeCount = item.badge === 'notifications' ? unreadCount : 0
  const indent = { paddingLeft: `${12 + depth * 14}px` }

  if (item.children && item.children.length > 0) {
    return (
      <li>
        <button
          type="button"
          className="nav-subitem-toggle"
          style={indent}
          aria-expanded={open}
          onClick={() => setOpen((o) => !o)}
        >
          {Icon && <Icon className="nav-item-icon" size={16} aria-hidden />}
          <span className="nav-item-label">{item.label}</span>
          <ChevronDown className={`nav-chevron${open ? ' nav-chevron-open' : ''}`} size={14} aria-hidden />
        </button>
        {open && (
          <ul className="nav-subitems">
            {item.children.map((child) => (
              <NavItemRow key={child.to} item={child} depth={depth + 1} unreadCount={unreadCount} onNavigate={onNavigate} />
            ))}
          </ul>
        )}
      </li>
    )
  }

  return (
    <li>
      <NavLink
        to={item.to}
        style={indent}
        className={({ isActive }) => (isActive ? 'nav-item active' : 'nav-item')}
        onClick={onNavigate}
      >
        {Icon && <Icon className="nav-item-icon" size={16} aria-hidden />}
        <span className="nav-item-label">{item.label}</span>
        {badgeCount > 0 && <span className="nav-badge">{badgeCount > 99 ? '99+' : badgeCount}</span>}
      </NavLink>
    </li>
  )
}

export interface NavModuleProps {
  vm: VisibleModule
  expanded: boolean
  active: boolean
  unreadCount: number
  onToggle: (id: string) => void
  onNavigate?: () => void
}

export function NavModule({ vm, expanded, active, unreadCount, onToggle, onNavigate }: NavModuleProps) {
  const { module } = vm
  const Icon = module.icon
  const regionId = `navmod-${module.id}`
  const collapsedDot = !expanded && moduleHasUnread(vm, unreadCount)

  return (
    <li className={`nav-module${active ? ' nav-module-active' : ''}`}>
      <button
        type="button"
        className="nav-module-header"
        aria-expanded={expanded}
        aria-controls={regionId}
        onClick={() => onToggle(module.id)}
      >
        <Icon className="nav-module-icon" size={18} aria-hidden />
        <span className="nav-module-title">{module.label}</span>
        {collapsedDot && <span className="nav-module-dot" aria-hidden />}
        <ChevronDown className={`nav-chevron${expanded ? ' nav-chevron-open' : ''}`} size={16} aria-hidden />
      </button>
      <div
        id={regionId}
        className="nav-module-region"
        role="region"
        aria-label={module.label}
        data-expanded={expanded}
        inert={!expanded ? true : undefined}
      >
        <div className="nav-module-region-inner">
          <ul className="nav-module-items">
            {vm.items.map((item) => (
              <NavItemRow key={item.to} item={item} depth={0} unreadCount={unreadCount} onNavigate={onNavigate} />
            ))}
            {vm.subgroups.map((sg) => (
              <li key={sg.label} className="nav-subgroup">
                <div className="nav-subgroup-label">{sg.label}</div>
                <ul className="nav-subitems">
                  {sg.items.map((item) => (
                    <NavItemRow key={item.to} item={item} depth={0} unreadCount={unreadCount} onNavigate={onNavigate} />
                  ))}
                </ul>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </li>
  )
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/components/layout/nav/__tests__/NavModule.test.tsx`
Expected: PASS.

> Note: `inert` is a React 19 supported prop; it removes collapsed items from tab order and the a11y tree while the CSS grid-rows animation runs (region stays in the DOM). jsdom keeps the nodes queryable, so the nested-submenu test still finds toggled-open links.

- [ ] **Step 5: Commit**

```bash
git add src/components/layout/nav/NavModule.tsx src/components/layout/nav/__tests__/NavModule.test.tsx
git commit -m "feat(nav): collapsible NavModule with nested-submenu-ready rows"
```

---

### Task 6: `NavFilter.tsx`, `nav.css`, and `Sidebar.tsx` refactor

**Files:**
- Create: `src/components/layout/nav/NavFilter.tsx`
- Create: `src/components/layout/nav.css`
- Modify: `src/components/layout/Sidebar.tsx` (full rewrite of the `nav` composition; keep title, `LegalEntitySwitcher`, user footer, unread poll)
- Modify: `src/components/layout/Sidebar.css` (drop the flat-list `.nav-group-label`/`.nav-subgroup*` rules now owned by `nav.css`; keep shell + user-footer rules)
- Test: `src/components/layout/__tests__/Sidebar.test.tsx`

**Interfaces:**
- Consumes: `getNavModules`, `findActiveModuleId`, `filterModule`, `useExpandedModules`, `NavModule`, `NavFilter`.
- `NavFilter`: `function NavFilter(props: { value: string; onChange: (v: string) => void }): JSX.Element` — a labelled search input.
- `Sidebar` keeps its existing exported signature: `function Sidebar({ open, onNavigate }: { open?: boolean; onNavigate?: () => void }): JSX.Element`.

- [ ] **Step 1: Write the failing Sidebar integration test**

```tsx
// src/components/layout/__tests__/Sidebar.test.tsx
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { Sidebar } from '../Sidebar'

const auth = vi.hoisted(() => ({
  permissions: [] as string[],
  employeeId: null as string | null,
  userId: 'u1' as string | null,
}))

vi.mock('../../../features/auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: auth.userId
      ? { id: auth.userId, firstName: 'Ada', lastName: 'Byron', tenantName: 'Acme', employeeId: auth.employeeId }
      : null,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

vi.mock('../../../features/notifications/api/notificationsApi', () => ({
  getUnreadCount: vi.fn().mockResolvedValue({ count: 0 }),
}))

vi.mock('../../../features/legal-entities/api/legalEntitiesApi', () => ({
  getLegalEntityOptions: vi.fn().mockResolvedValue([]),
  getActiveLegalEntity: vi.fn().mockResolvedValue({ legalEntityId: null }),
  setActiveLegalEntity: vi.fn(),
}))

function renderSidebar(path = '/dashboard') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Sidebar onNavigate={vi.fn()} />
    </MemoryRouter>,
  )
}

describe('Sidebar', () => {
  beforeEach(() => {
    window.localStorage.clear()
    auth.permissions = []
    auth.employeeId = null
    auth.userId = 'u1'
  })

  it('hides modules the user has no permission for, keeps ungated Communicatie', () => {
    renderSidebar()
    expect(screen.queryByRole('button', { name: /Beheer/ })).toBeNull()
    expect(screen.getByRole('button', { name: /Communicatie/ })).toBeInTheDocument()
  })

  it('shows a permitted module and auto-expands the active one', () => {
    auth.permissions = ['vehicles.view']
    renderSidebar('/vehicles')
    const vloot = screen.getByRole('button', { name: /Vloot/ })
    expect(vloot).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('link', { name: 'Voertuigen' })).toBeInTheDocument()
  })

  it('filters the menu and drops non-matching modules', async () => {
    auth.permissions = ['invoices.view', 'vehicles.view']
    renderSidebar()
    await userEvent.type(screen.getByRole('searchbox', { name: /menu/i }), 'facturen')
    expect(screen.getByRole('link', { name: 'Facturen' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Vloot/ })).toBeNull()
  })

  it('shows the portal module only when the user has an employee link', () => {
    auth.employeeId = 'emp-1'
    renderSidebar('/portal')
    expect(screen.getByRole('button', { name: /Mijn portaal/ })).toBeInTheDocument()
  })

  it('calls onNavigate when a link is clicked (drawer close)', async () => {
    auth.permissions = ['vehicles.view']
    const onNavigate = vi.fn()
    render(
      <MemoryRouter initialEntries={['/vehicles']}>
        <Sidebar onNavigate={onNavigate} />
      </MemoryRouter>,
    )
    await userEvent.click(screen.getByRole('link', { name: 'Voertuigen' }))
    expect(onNavigate).toHaveBeenCalled()
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/components/layout/__tests__/Sidebar.test.tsx`
Expected: FAIL — `NavFilter`/new Sidebar not present (searchbox not found, or Beheer still rendered by old flat list).

- [ ] **Step 3: Create `NavFilter.tsx`**

```tsx
// src/components/layout/nav/NavFilter.tsx
import { Search } from 'lucide-react'

export function NavFilter({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  return (
    <div className="nav-filter">
      <Search className="nav-filter-icon" size={16} aria-hidden />
      <input
        type="search"
        className="nav-filter-input"
        placeholder="Filter menu…"
        aria-label="Filter menu"
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
      <kbd className="nav-filter-kbd" aria-hidden>⌘K</kbd>
    </div>
  )
}
```

- [ ] **Step 4: Create `nav.css`**

```css
/* src/components/layout/nav.css */
.nav-filter {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 4px 0 12px;
  padding: 0 10px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--accent-bg);
}
.nav-filter:focus-within { border-color: var(--accent-border); }
.nav-filter-icon { flex-shrink: 0; opacity: 0.6; }
.nav-filter-input {
  flex: 1;
  min-width: 0;
  border: none;
  background: transparent;
  color: var(--text);
  font: inherit;
  padding: 8px 0;
}
.nav-filter-input:focus { outline: none; }
.nav-filter-kbd {
  flex-shrink: 0;
  font-size: 11px;
  opacity: 0.5;
  border: 1px solid var(--border);
  border-radius: 4px;
  padding: 1px 4px;
}

.nav-modules { list-style: none; margin: 0; padding: 0; }

/* Clear separation between modules. */
.nav-module { margin-bottom: 6px; }

/* Module header is visually distinct from items: heavier, uppercase, full-width band. */
.nav-module-header {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 9px 12px;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: var(--text);
  font: inherit;
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  cursor: pointer;
}
.nav-module-header:hover { background: var(--accent-bg); }
.nav-module-header:focus-visible { outline: 2px solid var(--accent); outline-offset: 1px; }
.nav-module-icon { flex-shrink: 0; opacity: 0.85; }
.nav-module-title { flex: 1; text-align: left; }

/* Active module: tinted band + accent left rail so "where am I" is unmistakable. */
.nav-module-active > .nav-module-header {
  background: var(--accent-bg);
  color: var(--text-h);
  box-shadow: inset 3px 0 0 var(--accent);
}

.nav-module-dot {
  width: 7px; height: 7px;
  border-radius: 50%;
  background: var(--accent);
}
.nav-chevron { flex-shrink: 0; opacity: 0.6; transition: transform 0.18s ease; }
.nav-chevron-open { transform: rotate(180deg); }

/* Collapsible body via grid-rows; inert (set in JSX) handles focus/AT when collapsed. */
.nav-module-region {
  display: grid;
  grid-template-rows: 0fr;
  transition: grid-template-rows 0.2s ease;
}
.nav-module-region[data-expanded='true'] { grid-template-rows: 1fr; }
.nav-module-region-inner { overflow: hidden; }

.nav-module-items, .nav-subitems { list-style: none; margin: 0; padding: 0; }
.nav-module-items { padding: 2px 0 6px; display: flex; flex-direction: column; gap: 2px; }

.nav-item, .nav-subitem-toggle {
  display: flex;
  align-items: center;
  gap: 9px;
  width: 100%;
  padding: 7px 12px;
  border: none;
  border-radius: 6px;
  background: transparent;
  color: var(--text);
  font: inherit;
  text-align: left;
  text-decoration: none;
  cursor: pointer;
}
.nav-item:hover, .nav-subitem-toggle:hover { background: var(--accent-bg); }
.nav-item.active { background: var(--accent-bg); color: var(--text-h); font-weight: 500; }
.nav-item:focus-visible, .nav-subitem-toggle:focus-visible {
  outline: 2px solid var(--accent); outline-offset: 1px;
}
.nav-item-icon { flex-shrink: 0; opacity: 0.7; }
.nav-item-label { flex: 1; min-width: 0; }

.nav-subgroup { margin-top: 4px; }
.nav-subgroup-label {
  margin: 6px 12px 2px;
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.02em;
  color: var(--text);
  opacity: 0.45;
}

.nav-empty { padding: 8px 12px; font-size: 13px; opacity: 0.6; }

@media (prefers-reduced-motion: reduce) {
  .nav-module-region, .nav-chevron { transition: none; }
}
```

- [ ] **Step 5: Rewrite `Sidebar.tsx`**

Replace the entire file with:

```tsx
// src/components/layout/Sidebar.tsx
import { useEffect, useMemo, useState } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { useAuth } from '../../features/auth/authContextValue'
import { LegalEntitySwitcher } from '../../features/legal-entities/components/LegalEntitySwitcher'
import { getUnreadCount } from '../../features/notifications/api/notificationsApi'
import { getNavModules } from './nav/navConfig'
import { NavFilter } from './nav/NavFilter'
import { NavModule } from './nav/NavModule'
import { filterModule, findActiveModuleId, type VisibleModule } from './nav/navState'
import { useExpandedModules } from './nav/useExpandedModules'
import '../../features/notifications/pages/notifications.css'
import './Sidebar.css'
import './nav.css'

function initials(firstName: string, lastName: string): string {
  return `${firstName.charAt(0)}${lastName.charAt(0)}`.toUpperCase() || '?'
}

const UNREAD_POLL_MS = 60_000

export function Sidebar({ open = false, onNavigate }: { open?: boolean; onNavigate?: () => void }) {
  const { user, logout, hasAnyPermission } = useAuth()
  const location = useLocation()
  const [unreadCount, setUnreadCount] = useState(0)
  const [query, setQuery] = useState('')

  const modules = useMemo(() => getNavModules(), [])
  const activeModuleId = findActiveModuleId(modules, location.pathname)
  const { isExpanded, toggle } = useExpandedModules(user?.id ?? null, activeModuleId)

  // The sidebar only shows what the user may open; the backend enforces the same
  // permissions on every endpoint (UI filtering is UX, never security).
  const visibleModules = useMemo<VisibleModule[]>(
    () =>
      modules
        .map((m) => filterModule(m, { hasAnyPermission, hasEmployee: !!user?.employeeId, query }))
        .filter((vm): vm is VisibleModule => vm !== null),
    [modules, hasAnyPermission, user?.employeeId, query],
  )

  const filtering = query.trim().length > 0

  // Light poll so the notification badge stays roughly current without a push channel.
  useEffect(() => {
    let mounted = true
    const load = () => {
      getUnreadCount()
        .then((data) => {
          if (mounted) setUnreadCount(data.count)
        })
        .catch(() => {})
    }
    load()
    const timer = window.setInterval(load, UNREAD_POLL_MS)
    return () => {
      mounted = false
      window.clearInterval(timer)
    }
  }, [])

  return (
    <aside className={open ? 'sidebar sidebar-open' : 'sidebar'}>
      <h1 className="app-title">Transportation Service</h1>
      <LegalEntitySwitcher />
      <NavFilter value={query} onChange={setQuery} />
      <nav aria-label="Hoofdnavigatie">
        <ul className="nav-modules">
          {visibleModules.map((vm) => (
            <NavModule
              key={vm.module.id}
              vm={vm}
              expanded={filtering || isExpanded(vm.module.id)}
              active={vm.module.id === activeModuleId}
              unreadCount={unreadCount}
              onToggle={toggle}
              onNavigate={onNavigate}
            />
          ))}
        </ul>
        {filtering && visibleModules.length === 0 && (
          <p className="nav-empty">Geen menu-items voor “{query.trim()}”.</p>
        )}
      </nav>

      {user && (
        <div className="sidebar-user">
          <div className="sidebar-user-avatar" aria-hidden="true">
            {initials(user.firstName, user.lastName)}
          </div>
          <div className="sidebar-user-info">
            <span className="sidebar-user-name" title={`${user.firstName} ${user.lastName}`}>
              {user.firstName} {user.lastName}
            </span>
            <span className="sidebar-user-tenant" title={user.tenantName}>
              {user.tenantName}
            </span>
          </div>
          <button
            type="button"
            className="sidebar-logout"
            onClick={() => void logout()}
            aria-label="Uitloggen"
            title="Uitloggen"
          >
            ⎋
          </button>
        </div>
      )}
    </aside>
  )
}
```

> Note: `NavLink` import is retained only if still referenced; if the final file does not use `NavLink` directly, remove it from the import to satisfy the no-unused-vars lint. (The version above does **not** use `NavLink` — drop it from the import line.)

- [ ] **Step 6: Correct the import per the note**

Edit `src/components/layout/Sidebar.tsx` line 2 to drop the unused `NavLink`:
```tsx
import { useLocation } from 'react-router-dom'
```

- [ ] **Step 7: Trim `Sidebar.css`**

In `src/components/layout/Sidebar.css`, delete the now-unused flat-list rules that `nav.css` replaces: `.sidebar nav ul`, `.nav-item`, `.nav-item.active`, `.nav-group-label`, `.nav-subgroup`, `.nav-subgroup-label`, `.nav-footer`. **Keep** `.sidebar`, `.sidebar nav`, `.app-title`, all `.sidebar-user*`, `.sidebar-logout*`, and the `@media (max-width: 900px)` drawer block.

- [ ] **Step 8: Run the Sidebar test to verify it passes**

Run: `npx vitest run src/components/layout/__tests__/Sidebar.test.tsx`
Expected: PASS (all 5 assertions).

- [ ] **Step 9: Commit**

```bash
git add src/components/layout/nav/NavFilter.tsx src/components/layout/nav.css \
  src/components/layout/Sidebar.tsx src/components/layout/Sidebar.css \
  src/components/layout/__tests__/Sidebar.test.tsx
git commit -m "feat(nav): module-based sidebar with filter, badges, per-user state"
```

---

### Task 7: Full verification + 7-persona findability review

**Files:**
- Possibly modify: `src/components/layout/nav/navConfig.ts` (only if a persona reveals a mis-placed screen; use judgement per the spec)

**Interfaces:**
- Consumes: everything above. Produces: a verified, shippable navigation.

- [ ] **Step 1: Run the full frontend suite**

Run (from `TransportationService.Web/`):
```bash
npx vitest run
```
Expected: all tests pass, including the four new nav test files.

- [ ] **Step 2: Lint and type-check**

Run:
```bash
npm run lint && npx tsc --noEmit
```
Expected: no errors. Fix any unused imports (notably confirm `NavLink` was removed from `Sidebar.tsx`).

- [ ] **Step 3: Production build**

Run:
```bash
npm run build
```
Expected: build succeeds (Vite bundles `lucide-react` tree-shaken).

- [ ] **Step 4: Persona findability walkthrough**

For each persona below, confirm the screens they need are reachable in **one obvious module** with **no duplicates**. If a screen is hard to find, move it in `navConfig.ts` (judgement call), re-run `npx vitest run`, and note the change in the commit.

| Persona | Needs | Lives in |
|---|---|---|
| **Planner** | Transportopdrachten, Dossiers, Planning, Planbord | TRANSPORT |
| **Dispatcher** | Planbord, Operationeel centrum, Mijn ritten, Chauffeursapp | TRANSPORT |
| **Warehouse employee** | Magazijnen, Magazijn, Dockplanning, Incidenten, Afwijkingen | MAGAZIJN |
| **HR** | Medewerkers, Personeelsplanning, Afwezigheden, Kwalificaties | PERSONEEL |
| **Fleet manager** | Vlootoverzicht, Voertuigen, Opleggers, Tankkaarten, Onderhoud, Locaties | VLOOT |
| **Accountant** | Facturen, Kostentarieven, Verkooptarieven, Rendement, KPI's | KLANTEN (invoices/rates) + DASHBOARD (rendement/kpi) |
| **Administrator** | Gebruikers, Rollen & rechten, Functie → rol, Instellingen, Stamgegevens | BEHEER + STAMGEGEVENS |

Judgement checkpoints to weigh during the walkthrough (change only if it genuinely improves findability — record the rationale):
- **Accountant's financial screens are split** across KLANTEN (Facturen, tarieven) and DASHBOARD (Rendement, KPI's). If this feels scattered, consider whether Rendement/KPI's read better alongside the financials — but they are cross-domain analytics, so DASHBOARD is defensible. Decide and note.
- **Incidenten under MAGAZIJN**: confirm incidents are warehouse-oriented here; if they are broader operational incidents a dispatcher raises, TRANSPORT may fit better. Decide and note.
- Confirm no screen appears in two modules (grep the config for duplicate `to:` values): `grep -o "to: '[^']*'" src/components/layout/nav/navConfig.ts | sort | uniq -d` must print nothing.

- [ ] **Step 5: Commit any persona refinements**

```bash
git add src/components/layout/nav/navConfig.ts
git commit -m "refactor(nav): persona-review placement adjustments"
```
(Skip if the walkthrough required no changes.)

- [ ] **Step 6: Final full-suite confirmation**

Run:
```bash
npx vitest run && npm run build
```
Expected: green. Navigation redesign complete.

---

## Self-Review (author check against the spec)

**Spec coverage:**
- Ten modules + route mapping → Task 2 (`getNavModules`) covers every route; personas verified in Task 7. ✔
- Ctrl+K + filter box (no duplicated favorites/recents) → `NavFilter` (Task 6) + unchanged palette. ✔
- lucide-react icons → Task 1 + icons in config. ✔
- Omit routeless items → config contains none of them. ✔
- Collapsible, default-collapsed-except-active, per-user persistence → Tasks 4 + 6. ✔
- Active-module highlight + distinct headers + spacing → `nav.css` (Task 6). ✔
- Nested submenu ready → `NavItem.children` + `NavItemRow` (Tasks 2, 5). ✔
- Badges → `badge` key + `moduleHasUnread` + Meldingen (Tasks 2, 3, 5). ✔
- Responsive + keyboard a11y → retained drawer + `aria-expanded`/`inert`/focus-visible (Tasks 5, 6). ✔
- No routes changed → `AppRoutes.tsx` untouched (stated in Global Constraints). ✔

**Placeholder scan:** none — every step has concrete code/commands.

**Type consistency:** `NavModule`/`NavItem`/`VisibleModule`/`filterModule`/`findActiveModuleId`/`moduleHasUnread`/`useExpandedModules`/`NavModuleProps` names and signatures match across Tasks 2–6.
