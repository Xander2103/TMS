import { getTimeZonePreference } from '../utils/dates'
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
  // A timestamp's calendar day is the day it falls on in the TENANT zone (C-03); a date-only
  // value is already a calendar date and must not be converted at all.
  return date.toLocaleDateString(
    LOCALE_TAGS[locale],
    DATE_ONLY.test(iso) ? undefined : { timeZone: getTimeZonePreference() },
  )
}

/**
 * C-03 — deliberate split: the FORMAT follows the reader's language (a French customer reads
 * 15/07/2026), the CLOCK follows the tenant/carrier zone. A stop window is an appointment at
 * the carrier's dock, so 08:00 must stay 08:00 whatever device or country the portal user is
 * on; rendering it in the browser zone would tell a customer in Lisbon to be there at 07:00.
 */
export function formatDateTime(locale: Locale, iso: string): string {
  const date = parseIso(iso)
  if (Number.isNaN(date.getTime())) return iso
  return date.toLocaleString(LOCALE_TAGS[locale], {
    dateStyle: 'short', timeStyle: 'short', timeZone: getTimeZonePreference(),
  })
}

export function formatCurrency(locale: Locale, value: number, currency = 'EUR'): string {
  return value.toLocaleString(LOCALE_TAGS[locale], { style: 'currency', currency })
}
