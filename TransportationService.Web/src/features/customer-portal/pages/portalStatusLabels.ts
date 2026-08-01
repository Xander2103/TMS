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
