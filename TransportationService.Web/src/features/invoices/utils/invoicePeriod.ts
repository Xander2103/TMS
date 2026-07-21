/**
 * Conversion helpers between <input type="month"> values ("2026-07") and the
 * backend's { invoicePeriodYear, invoicePeriodMonth } pair.
 */

export interface InvoicePeriod {
  year: number
  month: number
}

/** Parses a month-input value ("2026-07") into a period, or null when invalid/empty. */
export function monthInputToPeriod(value: string): InvoicePeriod | null {
  const match = /^(\d{4})-(\d{2})$/.exec(value)
  if (!match) return null
  const year = Number(match[1])
  const month = Number(match[2])
  if (month < 1 || month > 12) return null
  return { year, month }
}

/** Formats a period as a month-input value: (2026, 7) → "2026-07". */
export function periodToMonthInput(year: number, month: number): string {
  return `${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}`
}

/** Dutch display format for a period: (2026, 7) → "07-2026" (MM-YYYY). */
export function formatPeriod(year: number, month: number): string {
  return `${String(month).padStart(2, '0')}-${String(year).padStart(4, '0')}`
}

/** Period of an ISO date string ("2026-07-21"); falls back to today's month when absent/invalid. */
export function dateToPeriod(isoDate?: string | null): InvoicePeriod {
  const parsed = isoDate ? monthInputToPeriod(isoDate.slice(0, 7)) : null
  if (parsed) return parsed
  const now = new Date()
  return { year: now.getFullYear(), month: now.getMonth() + 1 }
}

/** Negative when a lies before b, zero when equal, positive when after (in months). */
export function comparePeriods(a: InvoicePeriod, b: InvoicePeriod): number {
  return (a.year - b.year) * 12 + (a.month - b.month)
}
