import { getActiveLocale } from '../../i18n/activeLocale'
import { translate } from '../../i18n/translations'
import type { LocationOpeningInterval } from './types'

/**
 * Vertaalsleutels voor korte daglabels, index = dayOfWeek - 1 (ISO: 1 = maandag .. 7 =
 * zondag) — renderen als t(OPENING_DAY_LABEL_KEYS[day - 1]).
 */
export const OPENING_DAY_LABEL_KEYS = [
  'locations.days.mon',
  'locations.days.tue',
  'locations.days.wed',
  'locations.days.thu',
  'locations.days.fri',
  'locations.days.sat',
  'locations.days.sun',
] as const

export const OPENING_DAYS = [1, 2, 3, 4, 5, 6, 7] as const

function isCompleteTime(time: string): boolean {
  return /^\d{2}:\d{2}$/.test(time)
}

/**
 * Per-interval validation message KEYS (locations.openingHours.error*), aligned by index
 * with `value` — render via t(). Undefined = interval is fine.
 */
export function computeOpeningIntervalErrors(value: LocationOpeningInterval[]): (string | undefined)[] {
  const errors: (string | undefined)[] = value.map(() => undefined)

  value.forEach((interval, index) => {
    if (!isCompleteTime(interval.fromTime) || !isCompleteTime(interval.toTime)) {
      errors[index] = 'locations.openingHours.errorIncomplete'
    } else if (interval.toTime <= interval.fromTime) {
      errors[index] = 'locations.openingHours.errorEndBeforeStart'
    }
  })

  // Overlap only makes sense between intervals that are individually valid ("HH:mm" strings
  // compare correctly lexicographically). Touching boundaries (12:00–13:00 vs 13:00–17:00) are ok.
  // Checked against the pre-overlap snapshot so BOTH members of an overlapping pair get flagged.
  const baseErrors = [...errors]
  value.forEach((a, i) => {
    if (baseErrors[i]) return
    const overlaps = value.some(
      (b, j) => j !== i && !baseErrors[j] && b.dayOfWeek === a.dayOfWeek && a.fromTime < b.toTime && b.fromTime < a.toTime,
    )
    if (overlaps) errors[i] = 'locations.openingHours.errorOverlap'
  })

  return errors
}

export function openingIntervalsValid(value: LocationOpeningInterval[]): boolean {
  return computeOpeningIntervalErrors(value).every((error) => !error)
}

/** Compact read-only summary lines, e.g. "Ma 08:00–12:00, 13:00–17:00". Empty array = no structured hours. */
export function formatOpeningIntervals(value: LocationOpeningInterval[]): string[] {
  const locale = getActiveLocale()
  return OPENING_DAYS.filter((day) => value.some((i) => i.dayOfWeek === day)).map((day) => {
    const windows = value
      .filter((i) => i.dayOfWeek === day)
      .sort((a, b) => a.fromTime.localeCompare(b.fromTime))
      .map((i) => `${i.fromTime}–${i.toTime}${i.note ? ` (${i.note})` : ''}`)
    return `${translate(locale, OPENING_DAY_LABEL_KEYS[day - 1])} ${windows.join(', ')}`
  })
}
