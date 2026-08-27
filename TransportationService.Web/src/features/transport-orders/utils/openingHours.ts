import { getActiveLocale } from '../../../i18n/activeLocale'
import { translate } from '../../../i18n/translations'
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

/** Vertaalsleutels voor voluit geschreven dagnamen, index = ISO-dag - 1 (1 = maandag .. 7 = zondag). */
const DAY_NAME_KEYS = [
  'transportOrders.openingHours.days.mon',
  'transportOrders.openingHours.days.tue',
  'transportOrders.openingHours.days.wed',
  'transportOrders.openingHours.days.thu',
  'transportOrders.openingHours.days.fri',
  'transportOrders.openingHours.days.sat',
  'transportOrders.openingHours.days.sun',
] as const

/**
 * Locale-aware advisory line for the order form, or null when nothing is wrong (or nothing
 * can be concluded). Runs outside React — translated via the module-level active locale.
 * `activity` is the stable code 'loading' | 'unloading'; `time` is "HH:mm".
 */
export function openingHoursWarning(
  intervals: LocationOpeningInterval[] | null | undefined,
  date: string,
  time: string,
  activity: 'loading' | 'unloading',
  locationName: string,
): string | null {
  const day = isoDayOfWeek(date)
  if (day === null) return null
  const check = checkOpeningHours(intervals, day, time)
  const locale = getActiveLocale()
  const activityLabel = translate(
    locale,
    activity === 'unloading' ? 'transportOrders.openingHours.activityUnloading' : 'transportOrders.openingHours.activityLoading',
  )
  switch (check.verdict) {
    case 'outside':
      return translate(locale, 'transportOrders.openingHours.outside', {
        activity: activityLabel,
        time,
        hours: check.dayHours,
        locationName,
      })
    case 'closedDay':
      return translate(locale, 'transportOrders.openingHours.closedDay', {
        activity: activityLabel,
        day: translate(locale, DAY_NAME_KEYS[day - 1]),
        locationName,
      })
    default:
      return null
  }
}
