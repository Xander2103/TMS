import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { Pagination } from '../../../components/ui/Pagination'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { searchExceptions } from '../api/exceptionsApi'
import {
  EXCEPTION_SEVERITIES,
  EXCEPTION_SEVERITY_ICONS,
  EXCEPTION_SEVERITY_LABELS,
  EXCEPTION_SEVERITY_TONE,
  EXCEPTION_STATUSES,
  EXCEPTION_STATUS_ICONS,
  EXCEPTION_STATUS_LABELS,
  EXCEPTION_STATUS_TONE,
  EXCEPTION_TYPES,
  EXCEPTION_TYPE_LABELS,
  type ExceptionListItem,
  type ExceptionSeverity,
  type ExecutionExceptionStatus,
  type ExecutionExceptionType,
} from '../types'
import { formatDateTime } from '../../../utils/dates'
import '../components/exceptions.css'

const PAGE_SIZE = 25

export function ExceptionsPage() {
  const navigate = useNavigate()
  const { user } = useAuth()
  const { t } = useLocale()

  const [page, setPage] = useState(1)
  const [statusFilter, setStatusFilter] = useState<ExecutionExceptionStatus | ''>('Open')
  const [typeFilter, setTypeFilter] = useState<ExecutionExceptionType | ''>('')
  const [severityFilter, setSeverityFilter] = useState<ExceptionSeverity | ''>('')
  const [packagesOnly, setPackagesOnly] = useState(false)
  const [mineOnly, setMineOnly] = useState(false)

  // Same request-key idiom as usePagedQuery: state only mutates in async callbacks,
  // loading is derived from whether the loaded result matches the current request.
  const [state, setState] = useState<{
    items: ExceptionListItem[]
    totalCount: number
    error: string | null
    loadedKey: string
  }>({ items: [], totalCount: 0, error: null, loadedKey: '' })
  const requestKey = JSON.stringify({ statusFilter, typeFilter, severityFilter, packagesOnly, mineOnly, page })

  useEffect(() => {
    let mounted = true
    searchExceptions({
      status: statusFilter || undefined,
      type: typeFilter || undefined,
      severity: severityFilter || undefined,
      packagesOnly: packagesOnly || undefined,
      assignedToUserId: mineOnly ? (user?.id ?? undefined) : undefined,
      page,
      pageSize: PAGE_SIZE,
    })
      .then((result) => {
        if (!mounted) return
        setState({ items: result.items, totalCount: result.totalCount, error: null, loadedKey: requestKey })
      })
      .catch(() => {
        if (!mounted) return
        setState((current) => ({ ...current, error: t('exceptions.list.loadError'), loadedKey: requestKey }))
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey])

  const { items, totalCount, error } = state
  const isLoading = state.loadedKey !== requestKey

  const columns: Column<ExceptionListItem>[] = [
    {
      key: 'occurredAt',
      header: t('exceptions.list.colReported'),
      width: '140px',
      render: (r) => formatDateTime(r.occurredAt),
    },
    {
      key: 'type',
      header: t('exceptions.list.colType'),
      render: (r) => t(EXCEPTION_TYPE_LABELS[r.type]),
    },
    {
      key: 'severity',
      header: t('exceptions.list.colSeverity'),
      render: (r) => (
        <Badge tone={EXCEPTION_SEVERITY_TONE[r.severity]}>
          {EXCEPTION_SEVERITY_ICONS[r.severity]} {t(EXCEPTION_SEVERITY_LABELS[r.severity])}
        </Badge>
      ),
    },
    {
      key: 'status',
      header: t('exceptions.list.colStatus'),
      render: (r) => (
        <Badge tone={EXCEPTION_STATUS_TONE[r.status]}>
          {EXCEPTION_STATUS_ICONS[r.status]} {t(EXCEPTION_STATUS_LABELS[r.status])}
        </Badge>
      ),
    },
    {
      key: 'context',
      header: t('exceptions.list.colContext'),
      render: (r) => (
        <span>
          <code>{r.tripNumber}</code>
          {r.orderNumber && (
            <>
              {' · '}
              <code>{r.orderNumber}</code>
            </>
          )}
          {r.packageNumber && (
            <>
              {' · '}
              <code>{r.packageNumber}</code>
            </>
          )}
          {r.stopLabel && ` · ${r.stopLabel}`}
        </span>
      ),
    },
    {
      key: 'description',
      header: t('exceptions.list.colDescription'),
      render: (r) => <span className="exc-description-cell">{r.description}</span>,
    },
    {
      key: 'assignee',
      header: t('exceptions.list.colAssignee'),
      render: (r) => <span>{r.assignedToName ?? '—'}</span>,
    },
    {
      key: 'meta',
      header: t('exceptions.list.colInfo'),
      render: (r) => (
        <span className="exc-meta-cell">
          {r.reportedByName ?? '—'}
          {r.photoCount > 0 && ` · 📷 ${r.photoCount}`}
          {r.customerVisible && ` · 👁 ${t('exceptions.list.customerVisibleShort')}`}
        </span>
      ),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: t('exceptions.list.title') }]} />
      <PageHeader
        title={t('exceptions.list.title')}
        subtitle={t('exceptions.list.subtitle')}
      />

      <div className="exc-filters">
        <label>
          {t('exceptions.list.filterStatus')}{' '}
          <select
            value={statusFilter}
            onChange={(e) => {
              setStatusFilter(e.target.value as ExecutionExceptionStatus | '')
              setPage(1)
            }}
          >
            <option value="">{t('exceptions.list.all')}</option>
            {EXCEPTION_STATUSES.map((s) => (
              <option key={s} value={s}>
                {t(EXCEPTION_STATUS_LABELS[s])}
              </option>
            ))}
          </select>
        </label>
        <label>
          {t('exceptions.list.filterType')}{' '}
          <select
            value={typeFilter}
            onChange={(e) => {
              setTypeFilter(e.target.value as ExecutionExceptionType | '')
              setPage(1)
            }}
          >
            <option value="">{t('exceptions.list.all')}</option>
            {EXCEPTION_TYPES.map((type) => (
              <option key={type} value={type}>
                {t(EXCEPTION_TYPE_LABELS[type])}
              </option>
            ))}
          </select>
        </label>
        <label>
          {t('exceptions.list.filterSeverity')}{' '}
          <select
            value={severityFilter}
            onChange={(e) => {
              setSeverityFilter(e.target.value as ExceptionSeverity | '')
              setPage(1)
            }}
          >
            <option value="">{t('exceptions.list.all')}</option>
            {EXCEPTION_SEVERITIES.map((s) => (
              <option key={s} value={s}>
                {t(EXCEPTION_SEVERITY_LABELS[s])}
              </option>
            ))}
          </select>
        </label>
        <label className="exc-filter-check">
          <input
            type="checkbox"
            checked={packagesOnly}
            onChange={(e) => {
              setPackagesOnly(e.target.checked)
              setPage(1)
            }}
          />
          {t('exceptions.list.packagesOnly')}
        </label>
        <label className="exc-filter-check">
          <input
            type="checkbox"
            checked={mineOnly}
            onChange={(e) => {
              setMineOnly(e.target.checked)
              setPage(1)
            }}
          />
          {t('exceptions.list.mineOnly')}
        </label>
      </div>

      <DataTable
        columns={columns}
        rows={items}
        rowKey={(r) => r.id}
        isLoading={isLoading}
        error={error}
        emptyMessage={t('exceptions.list.empty')}
        onRowClick={(r) => navigate(`/exceptions/${r.id}`)}
      />
      <Pagination page={page} pageSize={PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
