/**
 * Contract end-date presets for the Dienstverband section (HR maturity wave, task 10): quick
 * "N months from the start date" buttons next to the end-date input.
 */

/** Preset button durations, in months, always offered on the end-date field. */
export const CONTRACT_END_DATE_PRESETS: { months: number; label: string }[] = [
  { months: 1, label: '1 m' },
  { months: 3, label: '3 m' },
  { months: 6, label: '6 m' },
  { months: 12, label: '12 m' },
]

function toIsoDate(year: number, monthIndex0: number, day: number): string {
  const mm = String(monthIndex0 + 1).padStart(2, '0')
  const dd = String(day).padStart(2, '0')
  return `${year}-${mm}-${dd}`
}

/** Today's calendar date as a local (not UTC-shifted) "yyyy-MM-dd" string. */
export function todayIsoDate(): string {
  const now = new Date()
  return toIsoDate(now.getFullYear(), now.getMonth(), now.getDate())
}

/**
 * Computes `addMonths(start, months) - 1 day`, the conventional "contract runs for N months"
 * end date: a contract starting on the 1st of a month and running one month ends the day
 * before the same day next month (Jan 1 + 1m → Jan 31, not Feb 1).
 *
 * When the start day doesn't exist in the target month (e.g. 31 Jan + 1 month has no "31 Feb"),
 * the add-months step clamps to the target month's last day — and that clamped date is already
 * the correct end date, so the trailing "-1 day" is skipped (31 Jan + 1m → 28/29 Feb, not
 * 27/28 Feb).
 */
export function addContractEndDate(startIso: string, months: number): string {
  const [year, month, day] = startIso.split('-').map(Number)
  const targetIndex = (month - 1) + months
  const targetYear = year + Math.floor(targetIndex / 12)
  const targetMonth = ((targetIndex % 12) + 12) % 12
  const daysInTargetMonth = new Date(targetYear, targetMonth + 1, 0).getDate()

  if (day > daysInTargetMonth) {
    // Clamped to the end of the target month — already the correct boundary, no further -1.
    return toIsoDate(targetYear, targetMonth, daysInTargetMonth)
  }

  const candidate = new Date(targetYear, targetMonth, day)
  candidate.setDate(candidate.getDate() - 1)
  return toIsoDate(candidate.getFullYear(), candidate.getMonth(), candidate.getDate())
}
