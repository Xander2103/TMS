import { useCallback, useEffect, useState } from 'react'
import { HistoryTimeline, type HistoryItem } from '../../../components/ui/HistoryTimeline'
import { getCustomerHistory, type CustomerHistoryPage } from '../api/customerHistoryApi'
import { useLocale } from '../../../i18n/localeContext'

const PAGE_SIZE = 25

/**
 * Chip order: "all" (no filter) first, then every backend history category. The values are the
 * API's stable category CODES — used both as the `category` query parameter and as the
 * translation key suffix (customers.history.categories.<code>). The endpoint also still
 * accepts the legacy Dutch labels, but new code always sends the code.
 */
const CATEGORY_FILTERS = ['all', 'customer', 'contacts', 'locations', 'billing', 'communication'] as const

/** Category chip tones (keyed by category code) — informational only, no strict semantics. */
const CATEGORY_TONES: Record<string, 'info' | 'neutral' | 'success' | 'warning'> = {
  customer: 'info',
  contacts: 'success',
  locations: 'neutral',
  billing: 'warning',
  communication: 'info',
}

/**
 * Readable customer history (mirrors EmployeeHistoryPanel): one card per audited change,
 * newest first, collapsed to a summary line — the actor, the category and (on demand)
 * the full field/before/after table. Ids are resolved to names server-side; nothing raw is
 * shown. Category filtering and display key off the API's stable `categoryCode`.
 */
export function CustomerHistoryPanel({ customerId }: { customerId: string }) {
  const { t } = useLocale()
  const [data, setData] = useState<CustomerHistoryPage | null>(null)
  const [page, setPage] = useState(1)
  const [category, setCategory] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(() => {
    getCustomerHistory(customerId, page, PAGE_SIZE, category)
      .then((result) => {
        setData(result)
        setError(null)
      })
      .catch(() => setError('customers.history.loadFailed'))
  }, [customerId, page, category])

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
        category: { label: t(`customers.history.categories.${entry.categoryCode}`), tone: CATEGORY_TONES[entry.categoryCode] ?? 'neutral' },
        changes: entry.changes,
      }))
    : null

  return (
    <HistoryTimeline
      items={error ? [] : items}
      error={error ? t(error) : null}
      emptyMessage={t('customers.history.empty')}
      loadingMessage={t('customers.history.loading')}
      categories={{
        label: t('customers.history.filterLabel'),
        value: category ?? 'all',
        options: CATEGORY_FILTERS.map((filter) => ({ value: filter, label: t(`customers.history.categories.${filter}`) })),
        onChange: selectCategory,
      }}
      pagination={data ? { page: data.page, pageSize: data.pageSize, totalCount: data.totalCount, onPageChange: setPage } : undefined}
    />
  )
}
