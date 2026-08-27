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

/** §11 price chip: ⚠ bij open pricing-readiness, ✓ wanneer alles geprijsd is, — zonder opdrachten. */
export function priceChip(dossier: DossierDetail): { labelKey: string | null; tone: 'warning' | 'success' | 'neutral' } {
  if (dossier.readiness.some((issue) => issue.code.startsWith('pricing.') && issue.severity !== 'Info')) {
    return { labelKey: 'dossiers.display.priceIncomplete', tone: 'warning' }
  }
  if (dossier.orders.length > 0 && !dossier.readiness.some((issue) => issue.code.startsWith('pricing.'))) {
    return { labelKey: 'dossiers.display.priceOk', tone: 'success' }
  }
  return { labelKey: null, tone: 'neutral' }
}
