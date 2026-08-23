/**
 * Small, dependency-free date helpers shared by the calendar primitives (and re-exported by
 * feature code that needs the same week/ISO-date math, e.g. `features/employee-planning/types`).
 * This is the single implementation — do not re-derive Monday-of-week or ISO formatting
 * elsewhere.
 */

import { getActiveLocale } from '../../i18n/activeLocale'

/** @deprecated Nederlandstalige constante — gebruik getDayNames() (volgt de UI-taal). */
export const DAY_NAMES = ['ma', 'di', 'wo', 'do', 'vr', 'za', 'zo'] as const

/** @deprecated Nederlandstalige constante — gebruik getMonthNames() (volgt de UI-taal). */
export const MONTH_NAMES = [
  'januari', 'februari', 'maart', 'april', 'mei', 'juni',
  'juli', 'augustus', 'september', 'oktober', 'november', 'december',
] as const

const LOCALE_TAGS = { nl: 'nl-BE', fr: 'fr-BE', en: 'en-GB' } as const

function localeTag(): string {
  return LOCALE_TAGS[getActiveLocale()]
}

/** Korte weekdagnamen, maandag-eerst, in de actieve UI-taal (ma/lu/Mon …). */
export function getDayNames(): string[] {
  const formatter = new Intl.DateTimeFormat(localeTag(), { weekday: 'short' })
  // 5 jan 2026 is een maandag.
  return Array.from({ length: 7 }, (_, index) =>
    formatter.format(new Date(2026, 0, 5 + index)).replace('.', ''))
}

/** Maandnamen in de actieve UI-taal. */
export function getMonthNames(): string[] {
  const formatter = new Intl.DateTimeFormat(localeTag(), { month: 'long' })
  return Array.from({ length: 12 }, (_, index) => formatter.format(new Date(2026, index, 1)))
}

/** Monday of the week containing the given date (ISO week start). */
export function mondayOf(date: Date): Date {
  const result = new Date(date)
  const day = (result.getDay() + 6) % 7
  result.setDate(result.getDate() - day)
  result.setHours(0, 0, 0, 0)
  return result
}

export function toIsoDate(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

export function addDays(date: Date, days: number): Date {
  const result = new Date(date)
  result.setDate(result.getDate() + days)
  return result
}

export function startOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1)
}

/** 0 = Monday .. 6 = Sunday, unlike `Date#getDay` which is Sunday-first. */
export function dayIndexMonday(date: Date): number {
  return (date.getDay() + 6) % 7
}

/**
 * Inclusive Monday..Sunday range of the full calendar grid used to render `anchor`'s month
 * (leading/trailing pad weeks from the neighbouring months included), always a whole number of
 * weeks and never more than 42 days. `MonthGrid` renders exactly this range, so callers can use
 * it to fetch data covering every cell the grid will show (not just the month itself).
 */
export function monthGridRange(anchor: Date): { start: Date; end: Date } {
  const monthStart = startOfMonth(anchor)
  const leadingPad = dayIndexMonday(monthStart)
  const daysInMonth = new Date(anchor.getFullYear(), anchor.getMonth() + 1, 0).getDate()
  const totalCells = Math.ceil((leadingPad + daysInMonth) / 7) * 7
  const start = addDays(monthStart, -leadingPad)
  const end = addDays(start, totalCells - 1)
  return { start, end }
}

/** Accessible per-cell label, e.g. "dinsdag 4 augustus, 2 items" — volgt de UI-taal. */
export function cellAriaLabel(date: Date, entryCount: number): string {
  const base = new Intl.DateTimeFormat(localeTag(), { weekday: 'long', day: 'numeric', month: 'long' }).format(date)
  const locale = getActiveLocale()
  if (entryCount === 0) {
    const none = locale === 'fr' ? 'aucun élément' : locale === 'en' ? 'no items' : 'geen items'
    return `${base}, ${none}`
  }
  const unit = locale === 'fr'
    ? entryCount === 1 ? 'élément' : 'éléments'
    : entryCount === 1 ? 'item' : 'items'
  return `${base}, ${entryCount} ${unit}`
}
