/**
 * B10: the columns of the dossier list as a model — which ones the planner shows, how wide each
 * one is, and which ones have to step aside when the container is narrower than their sum.
 * One mechanism (this file) replaces the per-nth-child CSS cascade, so hiding a column through
 * "Kolommen ▾" frees its width for the others and the responsive fit stays correct.
 */

export type DossierColumnKey =
  | 'number'
  | 'status'
  | 'customer'
  | 'reference'
  | 'date'
  | 'transport'
  | 'planning'
  | 'driver'
  | 'price'
  | 'invoice'
  | 'responsible'
  | 'actions'

export type DossierListDensity = 'compact' | 'normal'

/** Display order of every column the list knows. */
export const DOSSIER_COLUMN_ORDER: readonly DossierColumnKey[] = [
  'number', 'status', 'customer', 'reference', 'date', 'transport', 'planning', 'driver', 'price', 'invoice', 'responsible', 'actions',
]

/** Columns the planner cannot hide: identity, state, who, when — plus the row actions. */
export const DOSSIER_FIXED_COLUMNS: readonly DossierColumnKey[] = ['number', 'status', 'customer', 'date', 'actions']

/** Columns offered in "Kolommen ▾", in display order. */
export const DOSSIER_OPTIONAL_COLUMNS: readonly DossierColumnKey[] = ['reference', 'transport', 'planning', 'driver', 'price', 'invoice', 'responsible']

/**
 * SEC-M1: the commercial columns. Without a price right the API sends no amount and no invoice
 * status, so these are neither shown nor offered (the stored preference is kept for later).
 */
export const DOSSIER_PRICE_COLUMNS: readonly DossierColumnKey[] = ['price', 'invoice']

/** Default visible optional columns: the daily questions (planned? priced? invoiced?) plus route and reference. */
export const DOSSIER_DEFAULT_OPTIONAL: readonly DossierColumnKey[] = ['reference', 'transport', 'planning', 'price', 'invoice']

/**
 * Fixed widths (px, incl. 10px cell padding left and right). `customer` and `reference` have none:
 * they share whatever is left (klant first). `MIN_FLEX_WIDTH` is what each flexible column needs
 * to stay readable before a lower-priority column is dropped.
 */
export const DOSSIER_COLUMN_WIDTHS: Readonly<Record<Exclude<DossierColumnKey, 'customer' | 'reference'>, number>> = {
  number: 92,
  status: 120,
  date: 90,
  transport: 140,
  planning: 150,
  driver: 150,
  price: 112,
  invoice: 92,
  responsible: 130,
  actions: 40,
}

export const MIN_FLEX_WIDTH: Readonly<Record<'customer' | 'reference', number>> = { customer: 140, reference: 100 }

/**
 * When the container cannot hold every visible column, this is the order in which they step aside
 * (least operational first). Planning and Facturatie answer the daily questions, so they go last;
 * the fixed columns never go.
 */
export const DOSSIER_DROP_ORDER: readonly DossierColumnKey[] = ['responsible', 'reference', 'driver', 'transport', 'invoice', 'planning', 'price']

export function columnMinWidth(key: DossierColumnKey): number {
  return key === 'customer' || key === 'reference' ? MIN_FLEX_WIDTH[key] : DOSSIER_COLUMN_WIDTHS[key]
}

/** Width the table needs so that every column in `columns` keeps at least its (minimum) width. */
export function tableMinWidth(columns: readonly DossierColumnKey[]): number {
  return columns.reduce((sum, key) => sum + columnMinWidth(key), 0)
}

/** The columns to render: the fixed ones plus the chosen optional ones, in display order. */
export function visibleColumns(optional: readonly DossierColumnKey[]): DossierColumnKey[] {
  const chosen = new Set<DossierColumnKey>([...DOSSIER_FIXED_COLUMNS, ...optional])
  return DOSSIER_COLUMN_ORDER.filter((key) => chosen.has(key))
}

/**
 * Drops columns (in `DOSSIER_DROP_ORDER`) until the rest fits `containerWidth`. An unknown width
 * (0, before the first measurement) shows everything; the fixed columns are never dropped, so
 * below their sum the wrapper simply scrolls.
 */
export function fitColumns(columns: readonly DossierColumnKey[], containerWidth: number): DossierColumnKey[] {
  if (!Number.isFinite(containerWidth) || containerWidth <= 0) return [...columns]
  let shown = [...columns]
  for (const candidate of DOSSIER_DROP_ORDER) {
    if (tableMinWidth(shown) <= containerWidth) break
    if (shown.includes(candidate)) shown = shown.filter((key) => key !== candidate)
  }
  return shown
}

// ---- per-user preferences (browser-local; the server keeps no column store) -----------------

export interface DossierListPrefs {
  optional: DossierColumnKey[]
  density: DossierListDensity
}

export const DEFAULT_DOSSIER_LIST_PREFS: DossierListPrefs = { optional: [...DOSSIER_DEFAULT_OPTIONAL], density: 'normal' }

const PREFS_PREFIX = 'ts.dossiers.list:'

function isOptionalKey(value: unknown): value is DossierColumnKey {
  return typeof value === 'string' && (DOSSIER_OPTIONAL_COLUMNS as readonly string[]).includes(value)
}

export function parseDossierListPrefs(raw: string | null | undefined): DossierListPrefs {
  if (!raw) return DEFAULT_DOSSIER_LIST_PREFS
  try {
    const parsed = JSON.parse(raw) as Partial<DossierListPrefs>
    return {
      optional: Array.isArray(parsed.optional) ? DOSSIER_OPTIONAL_COLUMNS.filter((key) => parsed.optional!.some((v) => isOptionalKey(v) && v === key)) : [...DOSSIER_DEFAULT_OPTIONAL],
      density: parsed.density === 'compact' ? 'compact' : 'normal',
    }
  } catch {
    return DEFAULT_DOSSIER_LIST_PREFS
  }
}

export function readDossierListPrefs(userId: string | null): DossierListPrefs {
  if (!userId) return DEFAULT_DOSSIER_LIST_PREFS
  try {
    return parseDossierListPrefs(window.localStorage.getItem(`${PREFS_PREFIX}${userId}`))
  } catch {
    return DEFAULT_DOSSIER_LIST_PREFS
  }
}

export function writeDossierListPrefs(userId: string | null, prefs: DossierListPrefs): void {
  if (!userId) return
  try {
    window.localStorage.setItem(`${PREFS_PREFIX}${userId}`, JSON.stringify(prefs))
  } catch {
    /* best-effort convenience */
  }
}
