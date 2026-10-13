import { parseIsoDate } from '../../../utils/dates'

/**
 * Full elapsed years between an ISO date and today; null when the date is absent/invalid.
 * Used by the employee detail header (seniority text + read-only age display); eigen module
 * (react-refresh: een componentbestand exporteert alleen componenten).
 */
export function fullYearsSince(iso: string | null | undefined): number | null {
  const start = parseIsoDate(iso)
  if (!start || Number.isNaN(start.getTime())) return null
  const now = new Date()
  let years = now.getFullYear() - start.getFullYear()
  const anniversaryPassed =
    now.getMonth() > start.getMonth() || (now.getMonth() === start.getMonth() && now.getDate() >= start.getDate())
  if (!anniversaryPassed) years -= 1
  return years
}
