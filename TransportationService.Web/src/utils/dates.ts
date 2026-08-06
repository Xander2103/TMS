/**
 * Central Belgian (nl-BE) date formatting utility. Any UI surface rendering a backend date/
 * timestamp should go through here instead of printing the raw ISO string.
 *
 * NOTE: this duplicates the small, day-shift-safe `parseIso` from `src/i18n/formatters.ts`
 * (portal-only module, not importable from the main app) rather than sharing it.
 */

const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/

/**
 * Backend timestamps arrive as UTC but not always with an explicit offset; date-only values
 * ("2026-07-20") must stay calendar dates and never shift a day through timezone conversion.
 * Mirrors `parseIso` in `src/i18n/formatters.ts`.
 */
export function parseIsoDate(value: string | null | undefined): Date | null {
  if (!value) return null
  if (DATE_ONLY.test(value)) {
    const [year, month, day] = value.split('-').map(Number)
    return new Date(year, month - 1, day)
  }
  return new Date(value.endsWith('Z') || value.includes('+') ? value : `${value}Z`)
}

export function formatDate(value: string | null | undefined): string {
  const date = parseIsoDate(value)
  if (!date || Number.isNaN(date.getTime())) return ''
  return date.toLocaleDateString('nl-BE')
}

export function formatDateTime(value: string | null | undefined): string {
  const date = parseIsoDate(value)
  if (!date || Number.isNaN(date.getTime())) return ''
  return date.toLocaleString('nl-BE', { dateStyle: 'short', timeStyle: 'short' })
}

export function formatDateLong(value: string | null | undefined): string {
  const date = parseIsoDate(value)
  if (!date || Number.isNaN(date.getTime())) return ''
  return date.toLocaleDateString('nl-BE', { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' })
}
