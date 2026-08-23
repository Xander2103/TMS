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
  translate: (key: string) => string,
): NavItem | null {
  if (item.permissions && !hasAnyPermission(item.permissions)) return null
  const children = (item.children ?? [])
    .map((c) => keepItem(c, hasAnyPermission, q, translate))
    .filter((c): c is NavItem => c !== null)
  // Labels zijn vertaalsleutels: het menufilter matcht op de GETOONDE tekst in de
  // actieve taal (i18n-wave §47) — een Franse gebruiker zoekt op "Clients".
  const selfMatches = q.length === 0 || translate(item.label).toLowerCase().includes(q)
  if (!selfMatches && children.length === 0) return null
  return item.children ? { ...item, children } : item
}

export function filterModule(
  module: NavModule,
  opts: {
    hasAnyPermission: (codes: string[]) => boolean
    hasEmployee: boolean
    query: string
    /** Vertaalfunctie voor label-sleutels; default identiteit (sleutel zelf) voor tests. */
    translate?: (key: string) => string
  },
): VisibleModule | null {
  if (module.requiresEmployee && !opts.hasEmployee) return null
  const q = normalise(opts.query)
  const translate = opts.translate ?? ((key: string) => key)

  const items = (module.items ?? [])
    .map((i) => keepItem(i, opts.hasAnyPermission, q, translate))
    .filter((i): i is NavItem => i !== null)

  const subgroups = (module.subgroups ?? [])
    .map((sg) => ({
      label: sg.label,
      items: sg.items
        .map((i) => keepItem(i, opts.hasAnyPermission, q, translate))
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
