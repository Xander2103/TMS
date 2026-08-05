import type { LocationOpeningInterval } from '../../locations/types'

/**
 * Tiny client-side mirror of the backend opening-hours evaluator (Phase 7): gives the order
 * form an IMMEDIATE non-blocking hint when a planned time falls outside a location's
 * structured hours. The backend detail DTO's `warnings` stays the source of truth after save.
 *
 * Semantics match the backend: day-of-week is ISO (1 = maandag .. 7 = zondag); an interval's
 * start is inclusive and its end exclusive (at 17:00 a 07:00–17:00 site is closing).
 */
export type OpeningHoursVerdict = 'noData' | 'inside' | 'outside' | 'closedDay'

export interface OpeningHoursCheck {
  verdict: OpeningHoursVerdict
  /** The day's windows as display text ("08:00–12:00, 13:00–17:00"); '' when there are none. */
  dayHours: string
}

/** "yyyy-MM-dd" → ISO day-of-week 1..7, or null for an unparsable date. */
export function isoDayOfWeek(date: string): number | null {
  const parsed = new Date(`${date}T00:00:00`)
  if (Number.isNaN(parsed.getTime())) return null
  const jsDay = parsed.getDay() // 0 = Sunday
  return jsDay === 0 ? 7 : jsDay
}

/** "HH:mm" → minutes since midnight, or null when malformed. */
function minutesOf(time: string): number | null {
  const match = /^(\d{2}):(\d{2})/.exec(time)
  if (!match) return null
  const hours = Number(match[1])
  const minutes = Number(match[2])
  if (hours > 23 || minutes > 59) return null
  return hours * 60 + minutes
}

export function checkOpeningHours(
  intervals: LocationOpeningInterval[] | null | undefined,
  dayOfWeek: number,
  time: string,
): OpeningHoursCheck {
  if (!intervals || intervals.length === 0) return { verdict: 'noData', dayHours: '' }

  const moment = minutesOf(time)
  if (moment === null) return { verdict: 'noData', dayHours: '' }

  const dayIntervals = intervals
    .filter((interval) => interval.dayOfWeek === dayOfWeek)
    .slice()
    .sort((a, b) => a.fromTime.localeCompare(b.fromTime))
  if (dayIntervals.length === 0) return { verdict: 'closedDay', dayHours: '' }

  const dayHours = dayIntervals.map((interval) => `${interval.fromTime}–${interval.toTime}`).join(', ')
  const inside = dayIntervals.some((interval) => {
    const from = minutesOf(interval.fromTime)
    const to = minutesOf(interval.toTime)
    return from !== null && to !== null && moment >= from && moment < to
  })
  return { verdict: inside ? 'inside' : 'outside', dayHours }
}

const DUTCH_DAY_NAMES = ['maandag', 'dinsdag', 'woensdag', 'donderdag', 'vrijdag', 'zaterdag', 'zondag']

/**
 * Dutch advisory line for the order form, or null when nothing is wrong (or nothing can be
 * concluded). `activity` reads "laadtijd" / "lostijd"; `time` is "HH:mm".
 */
export function openingHoursWarning(
  intervals: LocationOpeningInterval[] | null | undefined,
  date: string,
  time: string,
  activity: 'laadtijd' | 'lostijd',
  locationName: string,
): string | null {
  const day = isoDayOfWeek(date)
  if (day === null) return null
  const check = checkOpeningHours(intervals, day, time)
  switch (check.verdict) {
    case 'outside':
      return `De geplande ${activity} van ${time} valt buiten de openingsuren (${check.dayHours}) van ${locationName}.`
    case 'closedDay':
      return `De geplande ${activity} op ${DUTCH_DAY_NAMES[day - 1]} valt op een sluitingsdag van ${locationName}.`
    default:
      return null
  }
}
