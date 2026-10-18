import { EMPTY_DOSSIER_FILTERS, DOSSIER_FILTER_KEYS, type DossierFilterKey, type DossierSearchFilters } from './dossierSearchParams'

/**
 * B11: quick views of the dossier list. A preset is nothing but a filter patch over the SAME
 * server-side search — no second query path — so the chips under the bar keep explaining what
 * is listed and the planner can refine from there. Saved views are the planner's own patches,
 * kept per user in the browser (the server has no view store; see docs).
 */
export type DossierPresetId = 'all' | 'myOpen' | 'unplannedWeek' | 'unpriced' | 'toInvoice' | 'openIncident'

export const DOSSIER_PRESETS: readonly DossierPresetId[] = ['all', 'myOpen', 'unplannedWeek', 'unpriced', 'toInvoice', 'openIncident']

export interface PresetContext {
  /** The signed-in user; without one "Mijn open" cannot be built and is hidden. */
  userId: string | null
  /** SEC-M1: false hides the invoice-status preset — the API refuses that filter without a price right. */
  canViewPrices?: boolean
  now?: Date
}

function isoLocalDate(date: Date): string {
  const yyyy = date.getFullYear()
  const mm = String(date.getMonth() + 1).padStart(2, '0')
  const dd = String(date.getDate()).padStart(2, '0')
  return `${yyyy}-${mm}-${dd}`
}

/** Only the WHAT-is-listed part of a filter set (no search/sort/page). */
export type DossierFilterPatch = Partial<Pick<DossierSearchFilters, DossierFilterKey>>

/** The filter patch of a preset, or null when it cannot be built in this context ("Mijn open" without a user). */
export function presetPatch(id: DossierPresetId, context: PresetContext): DossierFilterPatch | null {
  const now = context.now ?? new Date()
  switch (id) {
    case 'all':
      return {}
    case 'myOpen':
      return context.userId ? { status: 'Open', responsibleUserId: context.userId } : null
    case 'unplannedWeek': {
      const end = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 7)
      return { status: 'Open', planning: 'unplanned', dateFrom: isoLocalDate(now), dateTo: isoLocalDate(end) }
    }
    case 'unpriced':
      return { status: 'Open', priceStatus: 'Unpriced' }
    case 'toInvoice':
      return context.canViewPrices === false ? null : { status: 'Closed', invoiceStatus: 'ReadyToInvoice' }
    case 'openIncident':
      return { status: 'Open', hasOpenIncident: 'true' }
  }
}

/** The filter part of the current state as a patch (set keys only). */
export function filterPatchOf(filters: DossierSearchFilters): DossierFilterPatch {
  const patch: DossierFilterPatch = {}
  for (const key of DOSSIER_FILTER_KEYS) {
    if (filters[key] !== '') (patch as Record<string, string>)[key] = filters[key]
  }
  return patch
}

/** True when the current filters are EXACTLY the patch (search/sort/page do not count). */
export function matchesPatch(filters: DossierSearchFilters, patch: DossierFilterPatch): boolean {
  const current = filterPatchOf(filters)
  const wanted = filterPatchOf({ ...EMPTY_DOSSIER_FILTERS, ...patch })
  const keys = new Set([...Object.keys(current), ...Object.keys(wanted)])
  for (const key of keys) {
    if ((current as Record<string, string>)[key] !== (wanted as Record<string, string>)[key]) return false
  }
  return true
}

/** Which preset the current filters are, or null when the planner refined beyond one. */
export function activePreset(filters: DossierSearchFilters, context: PresetContext): DossierPresetId | null {
  for (const id of DOSSIER_PRESETS) {
    const patch = presetPatch(id, context)
    if (patch && matchesPatch(filters, patch)) return id
  }
  return null
}

/** Applying a preset replaces the WHAT (filters); the search text, sort and direction stay. */
export function applyPreset(filters: DossierSearchFilters, patch: DossierFilterPatch): DossierSearchFilters {
  return { ...EMPTY_DOSSIER_FILTERS, ...patch, search: filters.search, sort: filters.sort, dir: filters.dir, page: 1 }
}

// ---- saved views (per user, browser-local) ---------------------------------------------------

export interface SavedDossierView {
  id: string
  name: string
  filters: DossierFilterPatch
}

const VIEWS_PREFIX = 'ts.dossiers.views:'
const MAX_VIEWS = 12

function isFilterKey(value: string): value is DossierFilterKey {
  return (DOSSIER_FILTER_KEYS as readonly string[]).includes(value)
}

export function parseSavedViews(raw: string | null | undefined): SavedDossierView[] {
  if (!raw) return []
  try {
    const parsed = JSON.parse(raw) as unknown
    if (!Array.isArray(parsed)) return []
    const views: SavedDossierView[] = []
    for (const entry of parsed) {
      if (!entry || typeof entry !== 'object') continue
      const { id, name, filters } = entry as Partial<SavedDossierView>
      if (typeof id !== 'string' || typeof name !== 'string' || !filters || typeof filters !== 'object') continue
      const patch: DossierFilterPatch = {}
      for (const [key, value] of Object.entries(filters)) {
        if (isFilterKey(key) && typeof value === 'string' && value !== '') (patch as Record<string, string>)[key] = value
      }
      views.push({ id, name: name.trim().slice(0, 60), filters: patch })
    }
    return views.slice(0, MAX_VIEWS)
  } catch {
    return []
  }
}

export function readSavedViews(userId: string | null): SavedDossierView[] {
  if (!userId) return []
  try {
    return parseSavedViews(window.localStorage.getItem(`${VIEWS_PREFIX}${userId}`))
  } catch {
    return []
  }
}

export function writeSavedViews(userId: string | null, views: SavedDossierView[]): void {
  if (!userId) return
  try {
    window.localStorage.setItem(`${VIEWS_PREFIX}${userId}`, JSON.stringify(views.slice(0, MAX_VIEWS)))
  } catch {
    /* best-effort convenience */
  }
}

export function newViewId(): string {
  return `v-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 7)}`
}

export { MAX_VIEWS }
