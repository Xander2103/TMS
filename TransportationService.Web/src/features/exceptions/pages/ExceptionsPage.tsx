import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { Pagination } from '../../../components/ui/Pagination'
import { DensityToggle } from '../../../components/ui/DensityToggle'
import { useListDensity } from '../../../components/ui/listDensity'
import { tableSortOf, useUrlListState } from '../../../hooks/useUrlListState'
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
import { TruncatedText } from '../../../components/ui/TruncatedText'

const PAGE_SIZE = 25

/**
 * B26: list state in the URL. The default view is the open exceptions, so "every status" is an
 * explicit `status=all` (a clean URL keeps meaning "open").
 */
const LIST_STATE = {
  defaults: { status: 'Open', type: '', severity: '', packages: '', mine: '', sort: '', dir: '' },
  allowed: {
    status: [...EXCEPTION_STATUSES, 'all'],
    type: EXCEPTION_TYPES,
    severity: EXCEPTION_SEVERITIES,
    packages: ['1'],
    mine: ['1'],
    sort: ['occurred', 'severity'],
    dir: ['asc', 'desc'],
  },
}
const DESC_BY_DEFAULT = ['occurred', 'severity']

export function ExceptionsPage() {
  const navigate = useNavigate()
  const { user } = useAuth()
  const { t } = useLocale()

  const { state: list, apply, reset, isFiltered } = useUrlListState(LIST_STATE)
  const page = list.page
  const statusFilter = (list.status === 'all' ? '' : list.status) as ExecutionExceptionStatus | ''
  const typeFilter = list.type as ExecutionExceptionType | ''
  const severityFilter = list.severity as ExceptionSeverity | ''
  const packagesOnly = list.packages === '1'
  const mineOnly = list.mine === '1'
  const [density, setDensity] = useListDensity('exceptions')

  // Same request-key idiom as usePagedQuery: state only mutates in async callbacks,
  // loading is derived from whether the loaded result matches the current request.
  const [state, setState] = useState<{
    items: ExceptionListItem[]
    totalCount: number
    error: string | null
    loadedKey: string
  }>({ items: [], totalCount: 0, error: null, loadedKey: '' })
  const requestKey = JSON.stringify({ statusFilter, typeFilter, severityFilter, packagesOnly, mineOnly, page, sort: list.sort, dir: list.dir })

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
      sort: list.sort,
      dir: list.dir as 'asc' | 'desc' | '',
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
      width: '136px',
      sortKey: 'occurred',
      render: (r) => <span className="ui-nowrap">{formatDateTime(r.occurredAt)}</span>,
    },
    {
      key: 'type',
      width: '140px',
      header: t('exceptions.list.colType'),
      render: (r) => t(EXCEPTION_TYPE_LABELS[r.type]),
    },
    {
      key: 'severity',
      width: '110px',
      sortKey: 'severity',
      header: t('exceptions.list.colSeverity'),
      render: (r) => (
        <Badge tone={EXCEPTION_SEVERITY_TONE[r.severity]}>
          {EXCEPTION_SEVERITY_ICONS[r.severity]} {t(EXCEPTION_SEVERITY_LABELS[r.severity])}
        </Badge>
      ),
    },
    {
      key: 'status',
      width: '110px',
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
      width: '200px',
      // B14: the context ids are links that never wrap; whatever does not fit is in the tooltip.
      render: (r) => {
        const text = [r.tripNumber, r.orderNumber, r.packageNumber, r.stopLabel].filter(Boolean).join(' · ')
        return (
          <TruncatedText tooltip={text}>
            <Link to={`/exceptions/${r.id}`} className="ui-id-link" onClick={(event) => event.stopPropagation()}>
              {r.tripNumber}
            </Link>
            {r.orderNumber && <span className="exc-context-part"> · {r.orderNumber}</span>}
            {r.packageNumber && <span className="exc-context-part"> · {r.packageNumber}</span>}
            {r.stopLabel && <span className="exc-context-part"> · {r.stopLabel}</span>}
          </TruncatedText>
        )
      },
    },
    {
      key: 'description',
      header: t('exceptions.list.colDescription'),
      render: (r) => <TruncatedText tooltip={r.description} className="exc-description-cell">{r.description}</TruncatedText>,
    },
    {
      key: 'assignee',
      width: '130px',
      header: t('exceptions.list.colAssignee'),
      render: (r) => (r.assignedToName ? <TruncatedText tooltip={r.assignedToName}>{r.assignedToName}</TruncatedText> : '—'),
    },
    {
      key: 'meta',
      width: '110px',
      header: t('exceptions.list.colInfo'),
      render: (r) => {
        const text = [r.reportedByName ?? '—', r.photoCount > 0 ? `📷 ${r.photoCount}` : null, r.customerVisible ? `👁 ${t('exceptions.list.customerVisibleShort')}` : null]
          .filter(Boolean)
          .join(' · ')
        return <TruncatedText tooltip={text}>{text}</TruncatedText>
      },
    },
  ]

  return (
    <div>
      <PageHeader
        title={t('exceptions.list.title')}
        subtitle={t('exceptions.list.subtitle')}
      />

      <div className="exc-filters">
        <select
          className="ui-filter-select"
          aria-label={t('exceptions.list.filterStatus')}
          value={list.status}
          onChange={(e) => apply({ status: e.target.value })}
        >
          <option value="all">{t('exceptions.list.allStatuses')}</option>
          {EXCEPTION_STATUSES.map((s) => (
            <option key={s} value={s}>
              {t(EXCEPTION_STATUS_LABELS[s])}
            </option>
          ))}
        </select>
        <select
          className="ui-filter-select"
          aria-label={t('exceptions.list.filterType')}
          value={typeFilter}
          onChange={(e) => apply({ type: e.target.value })}
        >
          <option value="">{t('exceptions.list.allTypes')}</option>
          {EXCEPTION_TYPES.map((type) => (
            <option key={type} value={type}>
              {t(EXCEPTION_TYPE_LABELS[type])}
            </option>
          ))}
        </select>
        <select
          className="ui-filter-select"
          aria-label={t('exceptions.list.filterSeverity')}
          value={severityFilter}
          onChange={(e) => apply({ severity: e.target.value })}
        >
          <option value="">{t('exceptions.list.allSeverities')}</option>
          {EXCEPTION_SEVERITIES.map((s) => (
            <option key={s} value={s}>
              {t(EXCEPTION_SEVERITY_LABELS[s])}
            </option>
          ))}
        </select>
        <label className="exc-filter-check">
          <input type="checkbox" checked={packagesOnly} onChange={(e) => apply({ packages: e.target.checked ? '1' : '' })} />
          {t('exceptions.list.packagesOnly')}
        </label>
        <label className="exc-filter-check">
          <input type="checkbox" checked={mineOnly} onChange={(e) => apply({ mine: e.target.checked ? '1' : '' })} />
          {t('exceptions.list.mineOnly')}
        </label>
        {isFiltered && (
          <button type="button" className="ui-filter-clear" onClick={reset}>
            {t('ui.filter.clear')}
          </button>
        )}
        <div className="ui-filter-trailing">
          <DensityToggle value={density} onChange={setDensity} />
        </div>
      </div>

      <DataTable
        fixedLayout
        density={density}
        sort={tableSortOf(list.sort, list.dir, DESC_BY_DEFAULT)}
        onSortChange={(next) => apply({ sort: next.key, dir: next.dir })}
        columns={columns}
        rows={items}
        rowKey={(r) => r.id}
        isLoading={isLoading}
        error={error}
        emptyMessage={t('exceptions.list.empty')}
        onRowClick={(r) => navigate(`/exceptions/${r.id}`)}
      />
      <Pagination page={page} pageSize={PAGE_SIZE} totalCount={totalCount} onPageChange={(next) => apply({ page: next })} />
    </div>
  )
}
