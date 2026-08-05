import type { LocationOpeningInterval } from './types'

/** Short Dutch day labels, index = dayOfWeek - 1 (ISO: 1 = maandag .. 7 = zondag). */
export const OPENING_DAY_LABELS = ['Ma', 'Di', 'Wo', 'Do', 'Vr', 'Za', 'Zo'] as const

export const OPENING_DAYS = [1, 2, 3, 4, 5, 6, 7] as const

function isCompleteTime(time: string): boolean {
  return /^\d{2}:\d{2}$/.test(time)
}

/**
 * Per-interval Dutch validation messages, aligned by index with `value`.
 * Undefined = interval is fine.
 */
export function computeOpeningIntervalErrors(value: LocationOpeningInterval[]): (string | undefined)[] {
  const errors: (string | undefined)[] = value.map(() => undefined)

  value.forEach((interval, index) => {
    if (!isCompleteTime(interval.fromTime) || !isCompleteTime(interval.toTime)) {
      errors[index] = 'Vul start- en eindtijd in.'
    } else if (interval.toTime <= interval.fromTime) {
      errors[index] = 'Eindtijd moet na starttijd liggen.'
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
    if (overlaps) errors[i] = 'Tijdvakken overlappen.'
  })

  return errors
}

export function openingIntervalsValid(value: LocationOpeningInterval[]): boolean {
  return computeOpeningIntervalErrors(value).every((error) => !error)
}

/** Compact read-only summary lines, e.g. "Ma 08:00–12:00, 13:00–17:00". Empty array = no structured hours. */
export function formatOpeningIntervals(value: LocationOpeningInterval[]): string[] {
  return OPENING_DAYS.filter((day) => value.some((i) => i.dayOfWeek === day)).map((day) => {
    const windows = value
      .filter((i) => i.dayOfWeek === day)
      .sort((a, b) => a.fromTime.localeCompare(b.fromTime))
      .map((i) => `${i.fromTime}–${i.toTime}${i.note ? ` (${i.note})` : ''}`)
    return `${OPENING_DAY_LABELS[day - 1]} ${windows.join(', ')}`
  })
}
