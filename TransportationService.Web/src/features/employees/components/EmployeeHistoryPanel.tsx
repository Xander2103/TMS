import { useCallback, useEffect, useState } from 'react'
import { HistoryTimeline, type HistoryItem } from '../../../components/ui/HistoryTimeline'
import { getEmployeeHistory, type EmployeeHistoryPage } from '../api/employeeHistoryApi'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'

const PAGE_SIZE = 25

/**
 * Chip order: "all" (no filter) first, then every dossier section in nav order. The values are
 * the API's stable category CODES — used both as the `category` query parameter and as the
 * translation key suffix (employees.history.categories.<code>).
 */
const CATEGORY_FILTERS = [
  'all',
  'profile',
  'qualifications',
  'documents',
  'notes',
  'issued_items',
  'absences',
  'leave_balance',
  'driver_profile',
] as const

/**
 * Categories the server only returns with an HR permission (HR acceptance B2): without it the
 * chip would always be empty, so it is not offered at all.
 */
const CATEGORY_PERMISSION: Partial<Record<(typeof CATEGORY_FILTERS)[number], string>> = {
  notes: 'employee_notes.view',
  leave_balance: 'leave_balances.view',
}

/** Category chip tones (keyed by category code) — informational only, no strict semantics. */
const CATEGORY_TONES: Record<string, 'info' | 'neutral' | 'success' | 'warning'> = {
  profile: 'info',
  qualifications: 'success',
  documents: 'neutral',
  notes: 'info',
  absences: 'warning',
  leave_balance: 'warning',
  issued_items: 'neutral',
  driver_profile: 'info',
}

/**
 * Complete readable personnel history (corrections wave §4): one card per audited save, newest
 * first, collapsed by default to a single summary line — the actor, the dossier section
 * and (on demand) the full field/before/after table. Old partial audit entries render through
 * the same endpoint with fewer field rows; unmapped/legacy id fields resolve to names
 * server-side. Category filtering and display key off the API's stable `categoryCode`.
 */
export function EmployeeHistoryPanel({ employeeId }: { employeeId: string }) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const visibleFilters = CATEGORY_FILTERS.filter((filter) => {
    const required = CATEGORY_PERMISSION[filter]
    return !required || hasPermission(required)
  })
  const [data, setData] = useState<EmployeeHistoryPage | null>(null)
  const [page, setPage] = useState(1)
  const [category, setCategory] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(() => {
    getEmployeeHistory(employeeId, page, PAGE_SIZE, category)
      .then((result) => {
        setData(result)
        setError(null)
      })
      .catch(() => setError('employees.history.loadFailed'))
  }, [employeeId, page, category])

  useEffect(() => {
    reload()
  }, [reload])

  const selectCategory = (next: string) => {
    setCategory(next === 'all' ? null : next)
    setPage(1)
  }

  // B25: the shared timeline (who · when · what, details on demand); categories stay this panel's own.
  const items: HistoryItem[] | null = data
    ? data.items.map((entry) => ({
        id: entry.id,
        timestamp: entry.timestamp,
        actorName: entry.userName,
        action: entry.actionLabel,
        summary: entry.summary,
        category: { label: t(`employees.history.categories.${entry.categoryCode}`), tone: CATEGORY_TONES[entry.categoryCode] ?? 'neutral' },
        changes: entry.changes,
      }))
    : null

  return (
    <HistoryTimeline
      items={error ? [] : items}
      error={error ? t(error) : null}
      emptyMessage={t('employees.history.empty')}
      loadingMessage={t('employees.history.loading')}
      categories={{
        label: t('employees.history.filterLabel'),
        value: category ?? 'all',
        options: visibleFilters.map((filter) => ({ value: filter, label: t(`employees.history.categories.${filter}`) })),
        onChange: selectCategory,
      }}
      pagination={data ? { page: data.page, pageSize: data.pageSize, totalCount: data.totalCount, onPageChange: setPage } : undefined}
    />
  )
}
