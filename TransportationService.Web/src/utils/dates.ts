/**
 * THE central date formatting layer of the internal app. Every UI surface rendering a
 * backend date/timestamp goes through here — components must never call
 * `toLocaleDateString()`/`toLocaleString()` or hand-roll patterns themselves, because the
 * display format is a TENANT SETTING (Instellingen → Regionale instellingen), not a
 * hardcoded locale.
 *
 * Storage stays normalised (ISO in the API/database); this module is presentation and
 * input-parsing only. The active format is initialised once per session from
 * GET /api/company-settings/display (see DisplayPreferencesProvider) and defaults to the
 * Belgian dd/MM/yyyy until that call resolves.
 *
 * The format catalog is CLOSED and mirrors the backend whitelist
 * (CompanySettingsService.SupportedDateFormats) — adding an option means extending both
 * sides plus the tests, never accepting arbitrary pattern strings. The same closed-catalog
 * approach is the growth path for future regional options (time notation, week start,
 * separators): add a preference field + a formatter entry, not per-component logic.
 *
 * NOTE: the customer-portal keeps its own locale-driven formatters in
 * `src/i18n/formatters.ts` — portal dates follow the CUSTOMER's language, not the internal
 * tenant preference. That split is deliberate.
 */

import { getActiveLocale } from '../i18n/activeLocale'

export type DateFormatPreference = 'dd/MM/yyyy' | 'MM/dd/yyyy' | 'yyyy-MM-dd' | 'dd-MM-yyyy'

export const DATE_FORMAT_OPTIONS: readonly DateFormatPreference[] =
  ['dd/MM/yyyy', 'MM/dd/yyyy', 'yyyy-MM-dd', 'dd-MM-yyyy']

const DEFAULT_FORMAT: DateFormatPreference = 'dd/MM/yyyy'

let activeFormat: DateFormatPreference = DEFAULT_FORMAT

export function isDateFormatPreference(value: unknown): value is DateFormatPreference {
  return typeof value === 'string' && (DATE_FORMAT_OPTIONS as readonly string[]).includes(value)
}

/** Set once at app bootstrap (and after saving settings); unknown values are ignored. */
export function setDateFormatPreference(value: string | null | undefined): void {
  if (isDateFormatPreference(value)) activeFormat = value
}

export function getDateFormatPreference(): DateFormatPreference {
  return activeFormat
}

/** Test-only escape hatch so suites can reset the module state between cases. */
export function resetDateFormatPreferenceForTests(): void {
  activeFormat = DEFAULT_FORMAT
}

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

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

function renderDate(date: Date, format: DateFormatPreference): string {
  const dd = pad(date.getDate())
  const mm = pad(date.getMonth() + 1)
  const yyyy = String(date.getFullYear())
  switch (format) {
    case 'MM/dd/yyyy': return `${mm}/${dd}/${yyyy}`
    case 'yyyy-MM-dd': return `${yyyy}-${mm}-${dd}`
    case 'dd-MM-yyyy': return `${dd}-${mm}-${yyyy}`
    default: return `${dd}/${mm}/${yyyy}`
  }
}

export function formatDate(value: string | null | undefined): string {
  const date = parseIsoDate(value)
  if (!date || Number.isNaN(date.getTime())) return ''
  return renderDate(date, activeFormat)
}

export function formatDateTime(value: string | null | undefined): string {
  const date = parseIsoDate(value)
  if (!date || Number.isNaN(date.getTime())) return ''
  return `${renderDate(date, activeFormat)} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}

const LONG_DATE_LOCALE_TAGS = { nl: 'nl-BE', fr: 'fr-BE', en: 'en-GB' } as const

export function formatDateLong(value: string | null | undefined): string {
  const date = parseIsoDate(value)
  if (!date || Number.isNaN(date.getTime())) return ''
  // Long form is language-driven (weekday/month names follow the UI language, i18n-wave);
  // the tenant's numeric pattern is irrelevant here.
  return date.toLocaleDateString(LONG_DATE_LOCALE_TAGS[getActiveLocale()], {
    weekday: 'long', year: 'numeric', month: 'long', day: 'numeric',
  })
}

/** Live example for the settings screen: what "12/08/2026" looks like per option. */
export function formatExample(format: DateFormatPreference): string {
  return renderDate(new Date(2026, 7, 12), format)
}

/**
 * Time-of-day of a backend timestamp, 24h "HH:mm" (Belgian convention, matches
 * formatDateTime). The de-facto ISO-substring trick spread across features consolidates
 * here: one implementation, timezone-correct via parseIsoDate.
 */
export function formatTime(value: string | null | undefined): string {
  const date = parseIsoDate(value)
  if (!date || Number.isNaN(date.getTime())) return ''
  return `${pad(date.getHours())}:${pad(date.getMinutes())}`
}

/**
 * Duration in minutes → hour notation: NL 468 → "7u48" (Belgian "u"), FR/EN → "7h48".
 * The single duration formatter for attendance/planning surfaces (consolidates the
 * employee-planning `formatMinutes` convention); the unit letter follows the UI language.
 */
export function formatDurationMinutes(minutes: number | null | undefined): string {
  if (minutes == null || Number.isNaN(minutes)) return ''
  const unit = getActiveLocale() === 'nl' ? 'u' : 'h'
  const total = Math.max(0, Math.round(minutes))
  const hours = Math.floor(total / 60)
  const rest = total % 60
  return rest === 0 ? `${hours}${unit}` : `${hours}${unit}${String(rest).padStart(2, '0')}`
}

/** Signed variant for deviations: +19 → "+0u19", -30 → "-0u30", 0 → "0u" (unit follows the UI language). */
export function formatSignedDurationMinutes(minutes: number | null | undefined): string {
  if (minutes == null || Number.isNaN(minutes)) return ''
  if (minutes === 0) return `0${getActiveLocale() === 'nl' ? 'u' : 'h'}`
  const sign = minutes > 0 ? '+' : '-'
  return `${sign}${formatDurationMinutes(Math.abs(minutes))}`
}

/**
 * Parses USER-TYPED date text according to the ACTIVE preference, strictly and without
 * ambiguity: "03/04/2026" is 3 April under dd/MM/yyyy and 4 March under MM/dd/yyyy —
 * exactly what the tenant configured, never a locale guess. Native `<input type="date">`
 * fields (the norm in this app) bypass this entirely (they emit ISO); this exists for the
 * few free-text entry points. Returns the ISO date (yyyy-MM-dd) or null when invalid.
 */
export function parseDisplayDate(text: string | null | undefined): string | null {
  if (!text) return null
  const trimmed = text.trim()
  const digits = trimmed.match(/^(\d{1,4})[/\-.](\d{1,2})[/\-.](\d{1,4})$/)
  if (!digits) return null

  let year: number
  let month: number
  let day: number
  const [, a, b, c] = digits
  if (activeFormat === 'yyyy-MM-dd') {
    if (a.length !== 4) return null
    year = Number(a); month = Number(b); day = Number(c)
  } else if (activeFormat === 'MM/dd/yyyy') {
    if (c.length !== 4) return null
    month = Number(a); day = Number(b); year = Number(c)
  } else {
    if (c.length !== 4) return null
    day = Number(a); month = Number(b); year = Number(c)
  }

  if (month < 1 || month > 12 || day < 1 || day > 31) return null
  const candidate = new Date(year, month - 1, day)
  // Reject overflow like 31/02: Date silently rolls over, which would corrupt input.
  if (candidate.getFullYear() !== year || candidate.getMonth() !== month - 1 || candidate.getDate() !== day) {
    return null
  }

  return `${year}-${pad(month)}-${pad(day)}`
}
