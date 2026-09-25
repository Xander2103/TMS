import type { SearchDossiersParams } from '../api/dossiersApi'
import type {
  DossierConfirmationSource,
  DossierInvoiceStatus,
  DossierPriceStatusFilter,
  DossierSortKey,
  DossierStatus,
} from '../types'

/**
 * Filter state of the dossier list. Every field is a plain string ('' = not set) so the whole
 * state round-trips through the querystring without a per-field codec; `page` is the exception.
 * The UI derives everything (request, chips, inputs) from this one object.
 */
export interface DossierSearchFilters {
  search: string
  status: '' | DossierStatus
  customerId: string
  dateFrom: string
  dateTo: string
  dossierNumber: string
  orderNumber: string
  customerReference: string
  customerNumber: string
  confirmationSource: '' | DossierConfirmationSource
  confirmedFrom: string
  confirmedTo: string
  createdFrom: string
  createdTo: string
  planningFrom: string
  planningTo: string
  driverId: string
  vehicleId: string
  licensePlate: string
  trailerId: string
  activityTypeId: string
  loadingCity: string
  unloadingCity: string
  postalCode: string
  countryCode: string
  priceStatus: '' | DossierPriceStatusFilter
  invoiceStatus: '' | DossierInvoiceStatus
  /** Tri-state as text so it lives in the URL unchanged. */
  hasCmr: '' | 'true' | 'false'
  sort: '' | DossierSortKey
  dir: '' | 'asc' | 'desc'
  page: number
}

/** Filters that describe WHAT is listed (chips, "Filters wissen"); search/sort/page are excluded. */
export type DossierFilterKey = Exclude<keyof DossierSearchFilters, 'search' | 'sort' | 'dir' | 'page'>

/** Order doubles as the querystring order, so URLs stay stable and readable. */
export const DOSSIER_FILTER_KEYS: readonly DossierFilterKey[] = [
  'status',
  'customerId',
  'dateFrom',
  'dateTo',
  'dossierNumber',
  'orderNumber',
  'customerReference',
  'customerNumber',
  'confirmationSource',
  'confirmedFrom',
  'confirmedTo',
  'createdFrom',
  'createdTo',
  'planningFrom',
  'planningTo',
  'driverId',
  'vehicleId',
  'licensePlate',
  'trailerId',
  'activityTypeId',
  'loadingCity',
  'unloadingCity',
  'postalCode',
  'countryCode',
  'priceStatus',
  'invoiceStatus',
  'hasCmr',
]

export const EMPTY_DOSSIER_FILTERS: DossierSearchFilters = {
  search: '',
  status: '',
  customerId: '',
  dateFrom: '',
  dateTo: '',
  dossierNumber: '',
  orderNumber: '',
  customerReference: '',
  customerNumber: '',
  confirmationSource: '',
  confirmedFrom: '',
  confirmedTo: '',
  createdFrom: '',
  createdTo: '',
  planningFrom: '',
  planningTo: '',
  driverId: '',
  vehicleId: '',
  licensePlate: '',
  trailerId: '',
  activityTypeId: '',
  loadingCity: '',
  unloadingCity: '',
  postalCode: '',
  countryCode: '',
  priceStatus: '',
  invoiceStatus: '',
  hasCmr: '',
  sort: '',
  dir: '',
  page: 1,
}

const STATUSES: readonly DossierStatus[] = ['Open', 'Closed', 'Cancelled']
const CONFIRMATION_SOURCES: readonly DossierConfirmationSource[] = ['Manual', 'Automatic']
const PRICE_STATUSES: readonly DossierPriceStatusFilter[] = ['Priced', 'Partial', 'Unpriced']
const INVOICE_STATUSES: readonly DossierInvoiceStatus[] = ['NotInvoiced', 'Draft', 'Sent', 'Paid']
const SORT_KEYS: readonly DossierSortKey[] = ['number', 'date', 'customer', 'status', 'confirmedAt', 'planningDate', 'createdAt']
const DIRS = ['asc', 'desc'] as const
const CMR = ['true', 'false'] as const

function oneOf<T extends string>(allowed: readonly T[], value: string | null): '' | T {
  return value !== null && (allowed as readonly string[]).includes(value) ? (value as T) : ''
}

function text(value: string | null): string {
  return value ?? ''
}

/** Reads the list state from a querystring; unknown/invalid values fall back to "not set". */
export function parseDossierSearchParams(params: URLSearchParams): DossierSearchFilters {
  const rawPage = Number(params.get('page'))
  return {
    search: text(params.get('search')),
    status: oneOf(STATUSES, params.get('status')),
    customerId: text(params.get('customerId')),
    dateFrom: text(params.get('dateFrom')),
    dateTo: text(params.get('dateTo')),
    dossierNumber: text(params.get('dossierNumber')),
    orderNumber: text(params.get('orderNumber')),
    customerReference: text(params.get('customerReference')),
    customerNumber: text(params.get('customerNumber')),
    confirmationSource: oneOf(CONFIRMATION_SOURCES, params.get('confirmationSource')),
    confirmedFrom: text(params.get('confirmedFrom')),
    confirmedTo: text(params.get('confirmedTo')),
    createdFrom: text(params.get('createdFrom')),
    createdTo: text(params.get('createdTo')),
    planningFrom: text(params.get('planningFrom')),
    planningTo: text(params.get('planningTo')),
    driverId: text(params.get('driverId')),
    vehicleId: text(params.get('vehicleId')),
    licensePlate: text(params.get('licensePlate')),
    trailerId: text(params.get('trailerId')),
    activityTypeId: text(params.get('activityTypeId')),
    loadingCity: text(params.get('loadingCity')),
    unloadingCity: text(params.get('unloadingCity')),
    postalCode: text(params.get('postalCode')),
    countryCode: text(params.get('countryCode')),
    priceStatus: oneOf(PRICE_STATUSES, params.get('priceStatus')),
    invoiceStatus: oneOf(INVOICE_STATUSES, params.get('invoiceStatus')),
    hasCmr: oneOf(CMR, params.get('hasCmr')),
    sort: oneOf(SORT_KEYS, params.get('sort')),
    dir: oneOf(DIRS, params.get('dir')),
    page: Number.isInteger(rawPage) && rawPage >= 1 ? rawPage : 1,
  }
}

/** Writes the list state as a querystring: empty values and page 1 are omitted (inverse of parse). */
export function toSearchParams(filters: DossierSearchFilters): URLSearchParams {
  const params = new URLSearchParams()
  if (filters.search) params.set('search', filters.search)
  for (const key of DOSSIER_FILTER_KEYS) {
    if (filters[key]) params.set(key, filters[key])
  }
  if (filters.sort) params.set('sort', filters.sort)
  if (filters.dir) params.set('dir', filters.dir)
  if (filters.page > 1) params.set('page', String(filters.page))
  return params
}

/** Request of GET /api/dossiers/search for this state; only set filters are included. */
export function toSearchDossiersParams(filters: DossierSearchFilters, pageSize: number): SearchDossiersParams {
  const params: SearchDossiersParams = {}
  if (filters.search) params.search = filters.search
  for (const key of DOSSIER_FILTER_KEYS) {
    const value = filters[key]
    if (!value) continue
    if (key === 'hasCmr') params.hasCmr = value === 'true'
    else (params as Record<string, unknown>)[key] = value
  }
  if (filters.sort) params.sort = filters.sort
  if (filters.dir) params.dir = filters.dir
  params.page = filters.page
  params.pageSize = pageSize
  return params
}

/** Keys of the set filters, in display order (chips). */
export function activeFilterKeys(filters: DossierSearchFilters): DossierFilterKey[] {
  return DOSSIER_FILTER_KEYS.filter((key) => filters[key] !== '')
}

// --- Quick date ranges (local calendar; the dossier date is a plain date, not an instant) ---

function isoLocalDate(date: Date): string {
  const yyyy = date.getFullYear()
  const mm = String(date.getMonth() + 1).padStart(2, '0')
  const dd = String(date.getDate()).padStart(2, '0')
  return `${yyyy}-${mm}-${dd}`
}

export function todayRange(now: Date = new Date()): { dateFrom: string; dateTo: string } {
  const today = isoLocalDate(now)
  return { dateFrom: today, dateTo: today }
}

/** Monday through Sunday of the week containing `now`. */
export function weekRangeOf(now: Date = new Date()): { dateFrom: string; dateTo: string } {
  const monday = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  // getDay(): 0 = Sunday … 6 = Saturday; shift so Monday is the first day.
  const offsetFromMonday = (monday.getDay() + 6) % 7
  monday.setDate(monday.getDate() - offsetFromMonday)
  const sunday = new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + 6)
  return { dateFrom: isoLocalDate(monday), dateTo: isoLocalDate(sunday) }
}
