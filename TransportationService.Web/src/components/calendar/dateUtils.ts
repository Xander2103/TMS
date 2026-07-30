/**
 * Small, dependency-free date helpers shared by the calendar primitives (and re-exported by
 * feature code that needs the same week/ISO-date math, e.g. `features/employee-planning/types`).
 * This is the single implementation — do not re-derive Monday-of-week or ISO formatting
 * elsewhere.
 */

export const DAY_NAMES = ['ma', 'di', 'wo', 'do', 'vr', 'za', 'zo'] as const

export const MONTH_NAMES = [
  'januari', 'februari', 'maart', 'april', 'mei', 'juni',
  'juli', 'augustus', 'september', 'oktober', 'november', 'december',
] as const

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

const CELL_LABEL_FORMATTER = new Intl.DateTimeFormat('nl-BE', { weekday: 'long', day: 'numeric', month: 'long' })

/** Accessible per-cell label, e.g. "dinsdag 4 augustus, 2 items". Colour is never the only signal. */
export function cellAriaLabel(date: Date, entryCount: number): string {
  const base = CELL_LABEL_FORMATTER.format(date)
  if (entryCount === 0) return `${base}, geen items`
  return `${base}, ${entryCount} item${entryCount === 1 ? '' : 's'}`
}
