/** Pure date/time math behind the planning board timeline. */

import { getActiveLocale } from '../../../i18n/activeLocale'

const LOCALE_TAGS = { nl: 'nl-BE', fr: 'fr-BE', en: 'en-GB' } as const

function localeTag(): string {
  return LOCALE_TAGS[getActiveLocale()]
}

export type BoardViewDays = 1 | 3 | 7

export const DAY_START_HOUR = 5
export const DAY_END_HOUR = 22

/** ISO dates (yyyy-MM-dd) of the visible columns, starting at the anchor. */
export function viewDates(anchorIso: string, days: BoardViewDays): string[] {
  const anchor = new Date(`${anchorIso}T00:00:00`)
  return Array.from({ length: days }, (_, i) => {
    const date = new Date(anchor)
    date.setDate(anchor.getDate() + i)
    return toIsoDate(date)
  })
}

export function toIsoDate(date: Date): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

/** Monday of the week that contains the date (the Week view always shows Ma … Zo). */
export function startOfWeek(iso: string): string {
  const date = new Date(`${iso}T00:00:00`)
  const offsetFromMonday = (date.getDay() + 6) % 7
  date.setDate(date.getDate() - offsetFromMonday)
  return toIsoDate(date)
}

export function shiftAnchor(anchorIso: string, days: BoardViewDays, direction: 1 | -1): string {
  const anchor = new Date(`${anchorIso}T00:00:00`)
  anchor.setDate(anchor.getDate() + direction * days)
  return toIsoDate(anchor)
}

/**
 * Vertical position of a trip block inside its day column, as percentages of the
 * 05:00–22:00 grid. Returns null when the trip has no planned times (the block then
 * stacks in the day's "unscheduled" section instead).
 */
export function blockPosition(
  plannedStart: string | null,
  plannedEnd: string | null,
): { topPct: number; heightPct: number } | null {
  if (!plannedStart) return null
  const start = new Date(plannedStart)
  if (Number.isNaN(start.getTime())) return null
  const end = plannedEnd ? new Date(plannedEnd) : null

  const gridMinutes = (DAY_END_HOUR - DAY_START_HOUR) * 60
  const startMinutes = clamp(start.getHours() * 60 + start.getMinutes() - DAY_START_HOUR * 60, 0, gridMinutes)
  const rawEndMinutes = end && !Number.isNaN(end.getTime())
    ? end.getHours() * 60 + end.getMinutes() - DAY_START_HOUR * 60
    : startMinutes + 120 // no end time: draw a 2-hour block
  const endMinutes = clamp(Math.max(rawEndMinutes, startMinutes + 30), 0, gridMinutes)

  return {
    topPct: (startMinutes / gridMinutes) * 100,
    heightPct: Math.max(((endMinutes - startMinutes) / gridMinutes) * 100, 3),
  }
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max)
}

/** Day-column heading in the active UI language (e.g. "ma 20 jul" / "lun 20 juil" / "Mon 20 Jul"). */
export function formatDayHeading(iso: string): string {
  const date = new Date(`${iso}T00:00:00`)
  return date.toLocaleDateString(localeTag(), { weekday: 'short', day: 'numeric', month: 'short' })
}

export function formatTimeRange(plannedStart: string | null, plannedEnd: string | null): string | null {
  if (!plannedStart) return null
  const fmt = (value: string) =>
    new Date(value).toLocaleTimeString(localeTag(), { hour: '2-digit', minute: '2-digit' })
  return plannedEnd ? `${fmt(plannedStart)}–${fmt(plannedEnd)}` : fmt(plannedStart)
}

/** Grid hour labels rendered along the timeline axis. */
export function gridHours(): number[] {
  const hours: number[] = []
  for (let h = DAY_START_HOUR; h <= DAY_END_HOUR; h += 2) hours.push(h)
  return hours
}

// ---------------------------------------------------------------------------
// B08: width-aware board. The day columns share the measured width of the timeline; below a
// readable minimum they stop shrinking and the timeline (never the page) scrolls sideways.
// ---------------------------------------------------------------------------

export type BoardDensity = 'compact' | 'normal'

/** The narrowest column that still reads (number, time, driver): tighter in compact density. */
export const MIN_DAY_WIDTH: Record<BoardDensity, number> = { compact: 104, normal: 132 }

/** Width one day column gets: an even share of the timeline, never below the readable minimum. */
export function dayColumnWidth(availablePx: number, days: BoardViewDays, density: BoardDensity): number {
  if (!Number.isFinite(availablePx) || availablePx <= 0) return MIN_DAY_WIDTH[density]
  return Math.max(MIN_DAY_WIDTH[density], Math.floor(availablePx / days))
}

/** True when all columns fit the timeline at their minimum width (no horizontal scrolling). */
export function daysFitWithoutScroll(availablePx: number, days: BoardViewDays, density: BoardDensity): boolean {
  return MIN_DAY_WIDTH[density] * days <= availablePx
}

/**
 * How much a trip card can show at this column width: everything (full), without vehicle and
 * route (medium), or only number · time · driver with a conflict dot (narrow).
 */
export type DayColumnLevel = 'full' | 'medium' | 'narrow'

export function dayColumnLevel(widthPx: number): DayColumnLevel {
  if (widthPx >= 200) return 'full'
  if (widthPx >= 140) return 'medium'
  return 'narrow'
}

/** Short day heading for narrow columns: weekday + day number only ("ma 20"). */
export function formatDayHeadingShort(iso: string): string {
  const date = new Date(`${iso}T00:00:00`)
  return date.toLocaleDateString(localeTag(), { weekday: 'short', day: 'numeric' })
}
