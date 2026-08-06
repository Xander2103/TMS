import type { BadgeTone } from '../../../components/ui/Badge'
import { parseIsoDate } from '../../../utils/dates'
import type { EmployeeListItem } from '../types/employee'

/**
 * Badge tone for the personnel list's "Dossier" completeness column (HR maturity wave §9).
 * Pulled out of EmployeesPage.tsx (not exported from there) so it stays testable without
 * tripping the fast-refresh "only export components" lint rule on a page module.
 */
export function completenessTone(percentage: number): BadgeTone {
  if (percentage < 60) return 'danger'
  if (percentage < 100) return 'warning'
  return 'success'
}

/**
 * Contract-end badge next to the status badge: an upcoming end date (0-30 days out) warns ahead
 * of time, a past end date on a still-active employee flags a dossier that needs closing out.
 */
export function contractEndBadge(
  row: Pick<EmployeeListItem, 'employmentEndDate' | 'isActive'>,
  today: Date = new Date(),
): { tone: BadgeTone; label: string } | null {
  const end = parseIsoDate(row.employmentEndDate)
  if (!end) return null

  const endDay = new Date(end.getFullYear(), end.getMonth(), end.getDate())
  const todayDay = new Date(today.getFullYear(), today.getMonth(), today.getDate())
  const diffDays = Math.round((endDay.getTime() - todayDay.getTime()) / 86_400_000)

  if (diffDays >= 0 && diffDays <= 30) {
    return { tone: 'warning', label: `Uit dienst over ${diffDays} d` }
  }
  if (diffDays < 0 && row.isActive) {
    return { tone: 'danger', label: 'Einddatum verstreken' }
  }
  return null
}
