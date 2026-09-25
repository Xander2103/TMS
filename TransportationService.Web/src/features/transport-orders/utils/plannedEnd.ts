import type { StopFormRow } from '../components/sections/orderFormState'

/**
 * Planned end of an on-site job (master sprint 2026-09-21, D2): einde = start + geplande duur.
 *
 * Pure tenant WALL-CLOCK arithmetic — "08:30 + 1,5 u = 10:00", "22:00 + 4 u = 02:00 the NEXT
 * day" — so the end DATE moves with it. The server stays authoritative (it recomputes the end of
 * every non-manual site stop on save); this only keeps the form honest while the planner types.
 *
 * Applies to SITE stops only. Loading/unloading windows and time requirements ("leveren vóór
 * 08:00") are never touched by anything in this file.
 */

export interface PlannedEnd {
  /** ISO date (yyyy-MM-dd) of the end — later than the start date when the job crosses midnight. */
  date: string
  /** "HH:mm", 24h. */
  time: string
}

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/
const CLOCK = /^(\d{1,2}):(\d{2})$/
const pad2 = (value: number) => String(value).padStart(2, '0')

/** "1,5" / "1.5" → 1.5; empty, non-numeric or negative → null. */
export function parseDurationHours(text: string): number | null {
  if (text.trim() === '') return null
  const parsed = Number(text.trim().replace(',', '.'))
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null
}

/**
 * Start date + start time + decimal hours → end date and time; null when any part is missing
 * or the duration is not a positive number (an end equal to the start says nothing).
 */
export function computePlannedEnd(date: string, fromTime: string, durationHours: number | null): PlannedEnd | null {
  const dateMatch = ISO_DATE.exec(date)
  const clockMatch = CLOCK.exec(fromTime)
  if (!dateMatch || !clockMatch) return null
  if (durationHours === null || !Number.isFinite(durationHours) || durationHours <= 0) return null
  const minutes = Math.round(durationHours * 60)
  // Date.UTC is used as a plain calendar: no zone, no DST gap — exactly wall-clock arithmetic.
  const end = new Date(
    Date.UTC(Number(dateMatch[1]), Number(dateMatch[2]) - 1, Number(dateMatch[3]), Number(clockMatch[1]), Number(clockMatch[2])) +
      minutes * 60_000,
  )
  return {
    date: `${end.getUTCFullYear()}-${pad2(end.getUTCMonth() + 1)}-${pad2(end.getUTCDate())}`,
    time: `${pad2(end.getUTCHours())}:${pad2(end.getUTCMinutes())}`,
  }
}

/** A non-manual site stop gets its end from start + duration (cleared when it cannot be computed). */
export function withAutoPlannedEnd(stop: StopFormRow, durationHours: number | null): StopFormRow {
  if (stop.stopType !== 'Site' || stop.plannedToIsManual) return stop
  const end = computePlannedEnd(stop.date, stop.fromTime, durationHours)
  const toDate = end?.date ?? ''
  const toTime = end?.time ?? ''
  return stop.toDate === toDate && stop.toTime === toTime ? stop : { ...stop, toDate, toTime }
}

/**
 * End date implied by a hand-typed end TIME: the start date when the end lies after the start,
 * the next day when it does not (20:00 → 01:00 crosses midnight); '' without an end time.
 */
function impliedManualEndDate(stop: StopFormRow): string {
  if (!stop.toTime || !ISO_DATE.test(stop.date)) return stop.toTime ? stop.date : ''
  const from = CLOCK.exec(stop.fromTime)
  const to = CLOCK.exec(stop.toTime)
  if (!from || !to) return stop.date
  const crossesMidnight = Number(to[1]) * 60 + Number(to[2]) <= Number(from[1]) * 60 + Number(from[2])
  // 24 h after the start date's midnight, read back as a plain calendar date (no zone involved).
  return crossesMidnight ? (computePlannedEnd(stop.date, '00:00', 24)?.date ?? stop.date) : stop.date
}

/**
 * The ONE way an editor applies a field patch to a stop row. For loading/unloading stops it is a
 * plain merge. For a site stop it also runs the predictable end-time mode:
 *  - editing Tot (time or end date) makes the end MANUAL — it is never overwritten afterwards;
 *  - `{ plannedToIsManual: false }` ("Automatisch berekenen") returns to automatic;
 *  - while automatic, the end follows every start/duration change.
 * One synchronous derivation, no effects — so there is nothing that could update in a circle.
 */
export function applyStopPatch(stop: StopFormRow, patch: Partial<StopFormRow>, durationHours: number | null): StopFormRow {
  const next: StopFormRow = { ...stop, ...patch }
  if (next.stopType !== 'Site') return next
  if (!('plannedToIsManual' in patch) && ('toTime' in patch || 'toDate' in patch)) {
    // Typing a TIME over a computed end must not inherit the computed end DATE: after
    // "22:00 + 4 u = 02:00 the next day", a typed 23:30 means 23:30 the SAME evening. Only while
    // the end date was never chosen by hand (leaving automatic, or still empty).
    if (!('toDate' in patch) && (!stop.plannedToIsManual || stop.toDate === '')) {
      next.toDate = impliedManualEndDate(next)
    }
    next.plannedToIsManual = true
  }
  if (!next.plannedToIsManual) return withAutoPlannedEnd(next, durationHours)
  // Manual end on the same day as the start: the end DATE rides along with a moved start date;
  // the entered end TIME is left alone.
  if ('date' in patch && !('toDate' in patch) && (stop.toDate === '' || stop.toDate === stop.date)) {
    next.toDate = next.toTime ? next.date : ''
  }
  return next
}
