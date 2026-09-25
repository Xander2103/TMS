import { getActiveLocale } from '../../i18n/activeLocale'
import { translate } from '../../i18n/translations'
import type { TranslateFn } from '../../i18n/localeContext'
import { formatDate as formatDatePreference, formatDateTime } from '../../utils/dates'
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

/**
 * Confirmation sprint 2026-09-23 — the lifecycle facts of a confirmed (status Closed) or
 * cancelled dossier, ready to render: `facts` as " · "-separated parts ("Bevestigd op …",
 * "handmatig"/"automatisch"/"bron onbekend", "door …"), `reason` as a muted line. A legacy
 * closed dossier has no confirmation source and may only carry `closedAt`. Empty when open.
 */
export function dossierLifecycleFacts(dossier: DossierDetail, t: TranslateFn): { facts: string[]; reason: string | null } {
  if (dossier.status === 'Closed') {
    const at = dossier.confirmedAt ?? dossier.closedAt
    const facts: string[] = []
    if (at) facts.push(t('dossiers.lifecycle.confirmedAt', { date: formatDateTime(at) }))
    facts.push(
      dossier.confirmationSource === 'Manual'
        ? t('dossiers.lifecycle.sourceManual')
        : dossier.confirmationSource === 'Automatic'
          ? t('dossiers.lifecycle.sourceAutomatic')
          : t('dossiers.lifecycle.sourceUnknown'),
    )
    if (dossier.confirmedByName) facts.push(t('dossiers.lifecycle.confirmedBy', { name: dossier.confirmedByName }))
    return { facts, reason: dossier.confirmationReason || null }
  }
  if (dossier.status === 'Cancelled') {
    const facts: string[] = []
    if (dossier.cancelledAt) facts.push(t('dossiers.lifecycle.cancelledAt', { date: formatDateTime(dossier.cancelledAt) }))
    const reason = dossier.cancellationReason ? `${t('dossiers.lifecycle.cancelReasonLabel')}: ${dossier.cancellationReason}` : null
    return { facts, reason }
  }
  return { facts: [], reason: null }
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

/**
 * True when at least one billable unit (order OR standalone activity) is priced — the same
 * definition the backend list/detail use. € 0 with provenance counts as priced.
 */
export function isDossierPriced(dossier: DossierDetail): boolean {
  const unitCount = dossier.financials.pricedActivityCount
  if (unitCount !== undefined) return unitCount > 0
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
