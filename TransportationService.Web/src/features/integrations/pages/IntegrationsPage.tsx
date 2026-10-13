import { useEffect, useState } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Pagination } from '../../../components/ui/Pagination'
import { useToast } from '../../../components/ui/toastContext'
import { apiClient, ApiError } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import { useLocale } from '../../../i18n/localeContext'
import { formatDateTime } from '../../../utils/dates'

type SyncStatus = 'Pending' | 'Synced' | 'Failed' | 'Cancelled'
type SyncOperation = 'Upsert' | 'Cancel'

interface SyncRow {
  id: string
  eventType: string
  employeeName: string
  startDate: string
  endDate: string
  title: string
  operation: SyncOperation
  status: SyncStatus
  externalEventId: string | null
  attemptCount: number
  lastSyncAt: string | null
  errorDetail: string | null
  createdAt: string
}

const STATUS_TONE: Record<SyncStatus, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Pending: 'info',
  Synced: 'success',
  Failed: 'danger',
  Cancelled: 'neutral',
}

/** Translation keys per sync status; render via t(STATUS_LABELS[status]). */
const STATUS_LABELS: Record<SyncStatus, string> = {
  Pending: 'integrations.status.Pending',
  Synced: 'integrations.status.Synced',
  Failed: 'integrations.status.Failed',
  Cancelled: 'integrations.status.Cancelled',
}

/** Translation keys per event type; unknown types render their raw code. */
const EVENT_LABELS: Record<string, string> = {
  leave_approved: 'integrations.event.leave_approved',
  shift_confirmed: 'integrations.event.shift_confirmed',
}

const PAGE_SIZE = 25

/** Outlook/Exchange sync queue: what the TMS wants mirrored to external calendars. */
export function IntegrationsPage() {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()

  const [statusFilter, setStatusFilter] = useState<SyncStatus | ''>('')
  const [page, setPage] = useState(1)
  const [reloadToken, setReloadToken] = useState(0)
  const [items, setItems] = useState<PagedResult<SyncRow> | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let mounted = true
    const query = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
    if (statusFilter) query.set('status', statusFilter)
    apiClient
      .getJson<PagedResult<SyncRow>>(`/api/integrations/calendar-sync?${query.toString()}`)
      .then((data) => {
        if (mounted) setItems(data)
      })
      .catch(() => {
        if (mounted) showError(t('integrations.page.loadFailed'))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, statusFilter, reloadToken])

  async function retry(id: string) {
    setBusy(true)
    try {
      await apiClient.postJson(`/api/integrations/calendar-sync/${id}/retry`, {})
      showSuccess(t('integrations.page.retried'))
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('integrations.page.retryFailed'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: t('integrations.page.title') }]} />
      <PageHeader title={t('integrations.page.title')} subtitle={t('integrations.page.subtitle')} />

      <div className="edi-filters">
        <select
          value={statusFilter}
          onChange={(e) => {
            setStatusFilter(e.target.value as SyncStatus | '')
            setPage(1)
          }}
          aria-label={t('integrations.page.statusAria')}
        >
          <option value="">{t('integrations.page.allStatuses')}</option>
          {(Object.keys(STATUS_LABELS) as SyncStatus[]).map((status) => (
            <option key={status} value={status}>
              {t(STATUS_LABELS[status])}
            </option>
          ))}
        </select>
      </div>

      <table className="to-stops-table">
        <thead>
          <tr>
            <th>{t('integrations.columns.created')}</th>
            <th>{t('integrations.columns.kind')}</th>
            <th>{t('integrations.columns.employee')}</th>
            <th>{t('integrations.columns.period')}</th>
            <th>{t('integrations.columns.title')}</th>
            <th>{t('integrations.columns.status')}</th>
            <th>{t('integrations.columns.externalEvent')}</th>
            <th>{t('integrations.columns.error')}</th>
            <th aria-label={t('integrations.columns.actionsAria')} />
          </tr>
        </thead>
        <tbody>
          {(items?.items ?? []).map((row) => (
            <tr key={row.id}>
              <td>{formatDateTime(row.createdAt)}</td>
              <td>
                {EVENT_LABELS[row.eventType] ? t(EVENT_LABELS[row.eventType]) : row.eventType}
                {row.operation === 'Cancel' && ` ${t('integrations.page.cancellationSuffix')}`}
              </td>
              <td>{row.employeeName}</td>
              <td>
                {row.startDate === row.endDate ? row.startDate : `${row.startDate} – ${row.endDate}`}
              </td>
              <td>{row.title}</td>
              <td>
                <Badge tone={STATUS_TONE[row.status]}>{t(STATUS_LABELS[row.status])}</Badge>
                {row.attemptCount > 1 && ` (${row.attemptCount}×)`}
              </td>
              <td>{row.externalEventId ? <code>{row.externalEventId}</code> : '—'}</td>
              <td title={row.errorDetail ?? undefined}>{row.errorDetail ?? '—'}</td>
              <td>
                {row.status === 'Failed' && (
                  <Button variant="ghost" onClick={() => void retry(row.id)} disabled={busy}>
                    {t('integrations.page.retry')}
                  </Button>
                )}
              </td>
            </tr>
          ))}
          {items !== null && items.items.length === 0 && (
            <tr>
              <td colSpan={9}>{t('integrations.page.empty')}</td>
            </tr>
          )}
        </tbody>
      </table>
      {items && <Pagination page={page} pageSize={PAGE_SIZE} totalCount={items.totalCount} onPageChange={setPage} />}
    </div>
  )
}
