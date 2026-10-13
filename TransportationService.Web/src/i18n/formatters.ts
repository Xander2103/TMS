import { formatDate as formatTenantDate, formatDateTime as formatTenantDateTime } from '../utils/dates'
import type { Locale } from './translations'

/** BCP-47 tags the portal formats dates/amounts with, per UI language. */
const LOCALE_TAGS: Record<Locale, string> = {
  nl: 'nl-BE',
  fr: 'fr-BE',
  en: 'en-GB',
}

export function formatDate(locale: Locale, iso: string): string {
  // B27: ONE date notation across the product. The tenant's configured pattern (default
  // dd/MM/yyyy, zero-padded) wins over the browser locale's loose "15/7/2026"; the reader's
  // language only drives month/weekday names elsewhere (formatDateLong). Unparseable input
  // falls back to the raw value so a bad timestamp is still visible, never hidden.
  void locale
  return formatTenantDate(iso) || iso
}

/**
 * C-03 — deliberate split: the FORMAT follows the reader's language (a French customer reads
 * 15/07/2026), the CLOCK follows the tenant/carrier zone. A stop window is an appointment at
 * the carrier's dock, so 08:00 must stay 08:00 whatever device or country the portal user is
 * on; rendering it in the browser zone would tell a customer in Lisbon to be there at 07:00.
 */
export function formatDateTime(locale: Locale, iso: string): string {
  // Same tenant pattern plus a 24h clock in the tenant zone ("15/07/2026 08:00"), the
  // notation the internal screens already use — one convention for carrier and customer.
  void locale
  return formatTenantDateTime(iso) || iso
}

export function formatCurrency(locale: Locale, value: number, currency = 'EUR'): string {
  return value.toLocaleString(LOCALE_TAGS[locale], { style: 'currency', currency })
}
