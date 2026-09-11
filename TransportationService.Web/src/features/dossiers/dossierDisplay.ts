import { getActiveLocale } from '../../i18n/activeLocale'
import { translate } from '../../i18n/translations'
import { formatDate as formatDatePreference } from '../../utils/dates'
import { formatDecimal, getDecimalSeparatorPreference } from '../../utils/numbers'
import { ORDER_STATUS_LABELS, type TransportOrderStatus } from '../transport-orders/types'
import type { DossierDetail } from './types'

/** "2,5 u" — tenant-decimaalteken, trailing zeros getrimd, uureenheid per taal. */
export function formatDuration(hours: number): string {
  const separator = getDecimalSeparatorPreference()
  const value = formatDecimal(hours, 2)
    .replace(/0+$/, '')
    .replace(new RegExp(`\\${separator}$`), '')
  return `${value} ${translate(getActiveLocale(), 'dossiers.display.hoursUnit')}`
}

/** Tenant-format date from an ISO date — delegates to the central formatter. */
export function formatDate(iso: string): string {
  return formatDatePreference(iso)
}

/** Worst-first ranking for the derived operational chip (§11). */
const OPERATIONAL_PRIORITY: TransportOrderStatus[] = [
  'InProgress', 'Planned', 'Confirmed', 'Submitted', 'Draft', 'Completed', 'Invoiced', 'Cancelled',
]

/** Translation KEY of the "worst" linked-order status (render via t()), or null without orders. */
export function operationalStatus(dossier: DossierDetail): string | null {
  const statuses = new Set(dossier.orders.map((o) => o.status))
  const worst = OPERATIONAL_PRIORITY.find((status) => statuses.has(status))
  return worst ? ORDER_STATUS_LABELS[worst] : null
}

/** True when at least one linked order is priced — the same definition the backend list/detail use. */
export function isDossierPriced(dossier: DossierDetail): boolean {
  const count = dossier.financials.pricedOrderCount
  if (count !== undefined) return count > 0
  // Older payloads without the count: fall back to a positive per-order price.
  return dossier.orders.some((o) => o.agreedPrice != null && o.agreedPrice > 0)
}

/** §11 price chip: ⚠ bij open pricing-readiness, ✓ wanneer alles geprijsd is, — zonder prijs. */
export function priceChip(dossier: DossierDetail): { labelKey: string | null; tone: 'warning' | 'success' | 'neutral' } {
  if (dossier.readiness.some((issue) => issue.code.startsWith('pricing.') && issue.severity !== 'Info')) {
    return { labelKey: 'dossiers.display.priceIncomplete', tone: 'warning' }
  }
  if (isDossierPriced(dossier) && !dossier.readiness.some((issue) => issue.code.startsWith('pricing.'))) {
    return { labelKey: 'dossiers.display.priceOk', tone: 'success' }
  }
  return { labelKey: null, tone: 'neutral' }
}
