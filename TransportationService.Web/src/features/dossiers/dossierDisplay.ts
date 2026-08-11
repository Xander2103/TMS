import { ORDER_STATUS_LABELS, type TransportOrderStatus } from '../transport-orders/types'
import type { DossierDetail } from './types'

/** "2,5 u" — Dutch decimal comma, trailing zeros trimmed. */
export function formatDuration(hours: number): string {
  return `${hours.toLocaleString('nl-BE', { maximumFractionDigits: 2 })} u`
}

/** "12-08-2026" from an ISO date. */
export function formatDate(iso: string): string {
  const [year, month, day] = iso.slice(0, 10).split('-')
  return `${day}-${month}-${year}`
}

/** Worst-first ranking for the derived operational chip (§11). */
const OPERATIONAL_PRIORITY: TransportOrderStatus[] = [
  'InProgress', 'Planned', 'Confirmed', 'Submitted', 'Draft', 'Completed', 'Invoiced', 'Cancelled',
]

/** Dutch label of the "worst" linked-order status, or null without orders. */
export function operationalStatus(dossier: DossierDetail): string | null {
  const statuses = new Set(dossier.orders.map((o) => o.status))
  const worst = OPERATIONAL_PRIORITY.find((status) => statuses.has(status))
  return worst ? ORDER_STATUS_LABELS[worst] : null
}

/** §11 price chip: ⚠ bij open pricing-readiness, ✓ wanneer alles geprijsd is, — zonder opdrachten. */
export function priceChip(dossier: DossierDetail): { label: string; tone: 'warning' | 'success' | 'neutral' } {
  if (dossier.readiness.some((issue) => issue.code.startsWith('pricing.') && issue.severity !== 'Info')) {
    return { label: '⚠ Onvolledig', tone: 'warning' }
  }
  if (dossier.orders.length > 0 && !dossier.readiness.some((issue) => issue.code.startsWith('pricing.'))) {
    return { label: '✓ In orde', tone: 'success' }
  }
  return { label: '—', tone: 'neutral' }
}
