import type { TranslateFn } from '../../../i18n/localeContext'

/**
 * Portal-owned translated status labels. The internal app keeps using the Dutch constants in
 * features/transport-orders, features/invoices and features/peppol; the portal deliberately
 * resolves the same status codes through its own translation maps instead.
 *
 * Unknown codes (e.g. a status added backend-side before the frontend ships) fall back to the
 * raw code rather than the translation key.
 */
function statusLabel(t: TranslateFn, keyPrefix: string, code: string): string {
  const key = `${keyPrefix}.${code}`
  const label = t(key)
  return label === key ? code : label
}

export function orderStatusLabel(t: TranslateFn, code: string): string {
  return statusLabel(t, 'orders.status', code)
}

export function exceptionStatusLabel(t: TranslateFn, code: string): string {
  return statusLabel(t, 'orders.exceptionStatus', code)
}

export function stopTypeLabel(t: TranslateFn, code: string): string {
  return statusLabel(t, 'orders.stopType', code)
}

export function unitTypeLabel(t: TranslateFn, code: string): string {
  return statusLabel(t, 'orders.unitType', code)
}

export function invoiceStatusLabel(t: TranslateFn, code: string): string {
  return statusLabel(t, 'invoices.status', code)
}

export function peppolStatusLabel(t: TranslateFn, code: string): string {
  return statusLabel(t, 'invoices.peppolStatus', code)
}

export function documentSourceLabel(t: TranslateFn, code: string): string {
  return statusLabel(t, 'documents.source', code)
}

type StatusTone = 'neutral' | 'success' | 'warning' | 'danger' | 'info'

/** Tone per customer-facing status code; the code itself is decided server-side. */
const CUSTOMER_STATUS_TONE: Record<string, StatusTone> = {
  Received: 'neutral',
  InReview: 'neutral',
  InfoRequested: 'warning',
  Confirmed: 'info',
  Planned: 'info',
  InTransit: 'info',
  Delivered: 'success',
  PartiallyDelivered: 'warning',
  NotDelivered: 'danger',
  RedeliveryPlanned: 'warning',
  Redelivered: 'success',
  ExecutionClosed: 'warning',
  Rejected: 'danger',
  Cancelled: 'neutral',
}

/**
 * The customer-facing status (PortalOrderStatusRules on the server already folded drafts, the
 * EX-08 delivery outcome and an open "Info opvragen" question into one code): only translated
 * and toned here — the portal never decides a status itself.
 */
export function portalOrderStatus(t: TranslateFn, status: string): { label: string; tone: StatusTone } {
  return { label: statusLabel(t, 'orders.customerStatus', status), tone: CUSTOMER_STATUS_TONE[status] ?? 'neutral' }
}

/** True for the statuses whose explanation sentence (orders.detail.deliveryOutcome.*) is shown. */
export function isShortfallStatus(status: string): boolean {
  return status === 'NotDelivered' || status === 'PartiallyDelivered' || status === 'RedeliveryPlanned' || status === 'Redelivered'
}

export function portalExceptionTypeLabel(t: TranslateFn, code: string): string {
  return statusLabel(t, 'orders.exceptionType', code)
}
