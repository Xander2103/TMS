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
 * tenant preference. That split is deliberate. It covers the date *format* only: the wall
 * CLOCK is the carrier's (the tenant's) everywhere, portal included — a stop window is an
 * appointment at the carrier's dock, so `formatters.formatDateTime` renders in the tenant zone
 * too (see C-03 below).
 *
 * ── C-03: THE transport-time convention ────────────────────────────────────────────────────
 * storage  the API keeps every timestamp as a UTC instant (`timestamp with time zone`, Npgsql
 *          hands EF Core `DateTimeKind.Utc` values; no legacy timestamp behaviour is enabled).
 * wire     System.Text.Json therefore serialises ISO-8601 ending in `Z`. A few hand-built
 *          responses can still lack the offset; `parseIsoDate` appends the `Z` so an
 *          offset-less timestamp is read as UTC — never as browser-local.
 * parse    `parseIsoDate` / `fromWireDateTime` turn the wire value into an instant.
 * render   `formatDate`/`formatDateTime`/`formatTime` project that instant onto the TENANT
 *          time zone (TenantSettings.Timezone, GET /api/company-settings/display → `timezone`,
 *          default Europe/Amsterdam). NEVER the browser/OS zone: a dispatcher in Warsaw and one
 *          in Lisbon must read the same 08:00 for the same stop.
 * input    `<input type="date">` + `<input type="time">` (or `datetime-local`) values are
 *          tenant WALL-CLOCK text — the browser attaches no zone to them.
 * wire     `toWireDateTime` / `fromDateTimeLocalInput` convert that wall clock back to a UTC
 *          instant with `Intl.DateTimeFormat`'s zone data (no date library, by design).
 * Only date-ONLY values ("2026-07-20") escape all of this: they are calendar dates and must
 * never shift a day through any conversion.
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

// --- Tenant time zone (C-03) ---

/** Matches the backend default in MasterDataSeeder (TenantSettings.Timezone). */
const DEFAULT_TIME_ZONE = 'Europe/Amsterdam'

let activeTimeZone: string = DEFAULT_TIME_ZONE

/**
 * IANA zone ids are NOT a closed catalog (unlike the date formats): the backend stores a free
 * string, so validity is decided by the runtime's own tz database instead of a whitelist that
 * would silently rot.
 */
export function isSupportedTimeZone(value: unknown): value is string {
  if (typeof value !== 'string' || value.trim() === '') return false
  try {
    new Intl.DateTimeFormat('en-US', { timeZone: value })
    return true
  } catch {
    return false
  }
}

/**
 * Set once at app bootstrap from GET /api/company-settings/display (`timezone`); unknown or
 * unsupported values are ignored so a bad setting degrades to Europe/Amsterdam rather than
 * breaking every timestamp in the app.
 */
export function setTimeZonePreference(value: string | null | undefined): void {
  if (isSupportedTimeZone(value)) activeTimeZone = value
}

export function getTimeZonePreference(): string {
  return activeTimeZone
}

/** Test-only escape hatch so suites can reset the module state between cases. */
export function resetTimeZonePreferenceForTests(): void {
  activeTimeZone = DEFAULT_TIME_ZONE
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

/** A wall-clock reading (year/month/day + time of day) in some zone — never an instant. */
interface WallClock {
  year: number
  month: number
  day: number
  hour: number
  minute: number
  second: number
}

// Intl.DateTimeFormat construction is expensive and every table row formats a handful of
// timestamps, so the per-zone formatter is built once.
const partsFormatters = new Map<string, Intl.DateTimeFormat>()

function partsFormatter(timeZone: string): Intl.DateTimeFormat {
  let formatter = partsFormatters.get(timeZone)
  if (!formatter) {
    formatter = new Intl.DateTimeFormat('en-US', {
      timeZone,
      hourCycle: 'h23',
      year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit', second: '2-digit',
    })
    partsFormatters.set(timeZone, formatter)
  }
  return formatter
}

/** The wall clock an instant shows on in `timeZone` (the tenant zone by default). */
function wallClockOf(instant: Date, timeZone: string = activeTimeZone): WallClock {
  const parts = partsFormatter(timeZone).formatToParts(instant)
  const read = (type: Intl.DateTimeFormatPartTypes): number =>
    Number(parts.find((part) => part.type === type)?.value ?? '0')
  return {
    year: read('year'), month: read('month'), day: read('day'),
    hour: read('hour'), minute: read('minute'), second: read('second'),
  }
}

/** Zone offset (ms east of UTC) in effect at `utcMs`, derived from the runtime tz database. */
function zoneOffsetMs(utcMs: number, timeZone: string): number {
  const whole = Math.floor(utcMs / 1000) * 1000
  const wall = wallClockOf(new Date(whole), timeZone)
  return Date.UTC(wall.year, wall.month - 1, wall.day, wall.hour, wall.minute, wall.second) - whole
}

const DAY_MS = 86_400_000

/**
 * Tenant wall clock → UTC instant. DST makes this a search, not an arithmetic shift: the offset
 * on either side of the day is probed and the candidate that actually reads back as the wanted
 * wall clock wins. Two candidates read back (the hour repeated by the autumn switch) → the
 * EARLIER one; none reads back (the hour skipped by the spring switch) → the later candidate,
 * i.e. the input shifts forward by the gap. Same disambiguation as Temporal's "compatible".
 */
function instantFromWallClock(
  year: number, month: number, day: number, hour: number, minute: number, timeZone: string,
): number {
  const naive = Date.UTC(year, month - 1, day, hour, minute, 0)
  const offsetBefore = zoneOffsetMs(naive - DAY_MS, timeZone)
  const offsetAfter = zoneOffsetMs(naive + DAY_MS, timeZone)
  const candidates = offsetBefore === offsetAfter
    ? [naive - offsetBefore]
    : [naive - offsetBefore, naive - offsetAfter]
  const matching = candidates
    .filter((ts) => {
      const wall = wallClockOf(new Date(ts), timeZone)
      return wall.year === year && wall.month === month && wall.day === day
        && wall.hour === hour && wall.minute === minute
    })
    .sort((a, b) => a - b)
  return matching.length > 0 ? matching[0] : Math.max(...candidates)
}

/**
 * Tenant wall clock (an ISO date plus optional "HH:mm") → the UTC instant on the wire, e.g.
 * 2026-07-15 08:00 Europe/Amsterdam → "2026-07-15T06:00:00Z". A missing time means midnight
 * tenant time — the wire encoding of a date-only stop (§14). Null when the date is absent or
 * not an ISO date.
 */
export function toWireDateTime(
  date: string | null | undefined, time?: string | null,
): string | null {
  if (!date || !DATE_ONLY.test(date)) return null
  const [year, month, day] = date.split('-').map(Number)
  const clock = time && /^\d{1,2}:\d{2}/.test(time) ? time : '00:00'
  const [hour, minute] = clock.split(':').map(Number)
  const instant = instantFromWallClock(year, month, day, hour, minute, activeTimeZone)
  if (Number.isNaN(instant)) return null
  return new Date(instant).toISOString().replace(/\.\d{3}Z$/, 'Z')
}

/**
 * The inverse: a wire timestamp → the tenant-zone date and time-of-day the user sees and edits.
 * Date-only values stay calendar dates at midnight (no conversion at all). Null when absent or
 * unparseable.
 */
export function fromWireDateTime(
  value: string | null | undefined,
): { date: string; time: string } | null {
  if (!value) return null
  if (DATE_ONLY.test(value)) return { date: value, time: '00:00' }
  const instant = parseIsoDate(value)
  if (!instant || Number.isNaN(instant.getTime())) return null
  const wall = wallClockOf(instant)
  return {
    date: `${wall.year}-${pad(wall.month)}-${pad(wall.day)}`,
    time: `${pad(wall.hour)}:${pad(wall.minute)}`,
  }
}

/** Wire timestamp → value for `<input type="datetime-local">`, in tenant wall-clock time. */
export function toDateTimeLocalInput(value: string | null | undefined): string {
  const parts = fromWireDateTime(value)
  return parts ? `${parts.date}T${parts.time}` : ''
}

/** `<input type="datetime-local">` value (tenant wall clock) → wire timestamp, null when empty. */
export function fromDateTimeLocalInput(value: string | null | undefined): string | null {
  if (!value) return null
  const [date, time] = value.split('T')
  return toWireDateTime(date, time)
}

function renderDate(wall: Pick<WallClock, 'year' | 'month' | 'day'>, format: DateFormatPreference): string {
  const dd = pad(wall.day)
  const mm = pad(wall.month)
  const yyyy = String(wall.year)
  switch (format) {
    case 'MM/dd/yyyy': return `${mm}/${dd}/${yyyy}`
    case 'yyyy-MM-dd': return `${yyyy}-${mm}-${dd}`
    case 'dd-MM-yyyy': return `${dd}-${mm}-${yyyy}`
    default: return `${dd}/${mm}/${yyyy}`
  }
}

/**
 * Calendar day of a value: the day itself for a date-only string, otherwise the day the instant
 * falls on IN THE TENANT ZONE (23:00Z on the 15th is already the 16th in Amsterdam).
 */
function calendarOf(value: string | null | undefined): Pick<WallClock, 'year' | 'month' | 'day'> | null {
  if (!value) return null
  if (DATE_ONLY.test(value)) {
    const [year, month, day] = value.split('-').map(Number)
    return { year, month, day }
  }
  const instant = parseIsoDate(value)
  if (!instant || Number.isNaN(instant.getTime())) return null
  return wallClockOf(instant)
}

export function formatDate(value: string | null | undefined): string {
  const wall = calendarOf(value)
  return wall ? renderDate(wall, activeFormat) : ''
}

export function formatDateTime(value: string | null | undefined): string {
  const time = formatTime(value)
  const wall = calendarOf(value)
  if (!wall) return ''
  return `${renderDate(wall, activeFormat)} ${time || '00:00'}`
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
  return renderDate({ year: 2026, month: 8, day: 12 }, format)
}

/**
 * Time-of-day of a backend timestamp, 24h "HH:mm" (Belgian convention, matches
 * formatDateTime), read in the TENANT zone. The de-facto `slice(11, 16)` trick that used to be
 * spread across features consolidates here: one implementation, and one that does not silently
 * report UTC (or the dispatcher's laptop zone) as the operational time.
 */
export function formatTime(value: string | null | undefined): string {
  if (!value) return ''
  if (DATE_ONLY.test(value)) return '00:00'
  const instant = parseIsoDate(value)
  if (!instant || Number.isNaN(instant.getTime())) return ''
  const wall = wallClockOf(instant)
  return `${pad(wall.hour)}:${pad(wall.minute)}`
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
