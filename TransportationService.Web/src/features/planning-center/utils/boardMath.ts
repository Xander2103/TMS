/** Pure date/time math behind the planning board timeline. */

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

export function formatDayHeading(iso: string): string {
  const date = new Date(`${iso}T00:00:00`)
  return date.toLocaleDateString('nl-BE', { weekday: 'short', day: 'numeric', month: 'short' })
}

export function formatTimeRange(plannedStart: string | null, plannedEnd: string | null): string | null {
  if (!plannedStart) return null
  const fmt = (value: string) =>
    new Date(value).toLocaleTimeString('nl-BE', { hour: '2-digit', minute: '2-digit' })
  return plannedEnd ? `${fmt(plannedStart)}–${fmt(plannedEnd)}` : fmt(plannedStart)
}

/** Grid hour labels rendered along the timeline axis. */
export function gridHours(): number[] {
  const hours: number[] = []
  for (let h = DAY_START_HOUR; h <= DAY_END_HOUR; h += 2) hours.push(h)
  return hours
}
