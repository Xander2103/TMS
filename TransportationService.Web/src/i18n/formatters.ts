import type { Locale } from './translations'

/** BCP-47 tags the portal formats dates/amounts with, per UI language. */
const LOCALE_TAGS: Record<Locale, string> = {
  nl: 'nl-BE',
  fr: 'fr-BE',
  en: 'en-GB',
}

const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/

/**
 * Backend timestamps arrive as UTC but not always with an explicit offset; date-only values
 * ("2026-07-20") must stay calendar dates and never shift a day through timezone conversion.
 */
function parseIso(iso: string): Date {
  if (DATE_ONLY.test(iso)) {
    const [year, month, day] = iso.split('-').map(Number)
    return new Date(year, month - 1, day)
  }
  return new Date(iso.endsWith('Z') || iso.includes('+') ? iso : `${iso}Z`)
}

export function formatDate(locale: Locale, iso: string): string {
  const date = parseIso(iso)
  if (Number.isNaN(date.getTime())) return iso
  return date.toLocaleDateString(LOCALE_TAGS[locale])
}

export function formatDateTime(locale: Locale, iso: string): string {
  const date = parseIso(iso)
  if (Number.isNaN(date.getTime())) return iso
  return date.toLocaleString(LOCALE_TAGS[locale], { dateStyle: 'short', timeStyle: 'short' })
}

export function formatCurrency(locale: Locale, value: number, currency = 'EUR'): string {
  return value.toLocaleString(LOCALE_TAGS[locale], { style: 'currency', currency })
}
