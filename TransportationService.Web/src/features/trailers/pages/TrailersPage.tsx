import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { Badge, type BadgeTone } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { tableSortOf, useUrlListState } from '../../../hooks/useUrlListState'
import { DensityToggle } from '../../../components/ui/DensityToggle'
import { useListDensity } from '../../../components/ui/listDensity'
import { TruncatedText } from '../../../components/ui/TruncatedText'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { blockedWhileAvailable } from '../../fleet-readiness/readiness'
import { searchTrailers } from '../api/trailersApi'
import { TRAILER_STATUS_LABELS, TRAILER_STATUS_TONES, type TrailerListItem } from '../types'

const STATUS_TONE: Record<TrailerListItem['operationalStatus'], BadgeTone> = TRAILER_STATUS_TONES

/** B26: list state in the URL (refresh, Back and shared links keep the view). */
const LIST_STATE = {
  defaults: { search: '', active: '', sort: '', dir: '' },
  allowed: { active: ['true', 'false'], sort: ['number', 'plate'], dir: ['asc', 'desc'] },
}
const DESC_BY_DEFAULT: string[] = []

export function TrailersPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { state, apply, reset, isFiltered } = useUrlListState(LIST_STATE)
  const { search, page } = state
  const activeFilter = state.active === '' ? undefined : state.active === 'true'
  const [density, setDensity] = useListDensity('trailers')

  const { items, totalCount, pageSize, isLoading, error, errorCause, reload } = usePagedQuery<TrailerListItem>(
    (args) => searchTrailers({ ...args, sort: state.sort, dir: state.dir as 'asc' | 'desc' | '' }),
    { search, isActive: activeFilter, page, errorMessage: t('trailers.list.loadFailed'), extra: { sort: state.sort, dir: state.dir } },
  )

  const columns: Column<TrailerListItem>[] = [
    { key: 'number', header: t('fleet.list.colNumber'), width: '120px', sortKey: 'number', render: (row) => (
      <Link to={`/trailers/${row.id}`} className="ui-id-link" onClick={(event) => event.stopPropagation()}>
        {row.internalNumber}
      </Link>
    ) },
    { key: 'plate', header: t('fleet.list.colPlate'), width: '130px', sortKey: 'plate', render: (row) => <code>{row.licensePlate}</code> },
    { key: 'brand', header: t('fleet.list.colBrandModel'), render: (row) => {
      const text = [row.brand, row.model].filter(Boolean).join(' ')
      return text ? <TruncatedText tooltip={text}>{text}</TruncatedText> : '—'
    } },
    { key: 'category', header: t('fleet.list.colCategory'), render: (row) => row.categoryName ?? '—' },
    {
      key: 'status',
      header: t('fleet.list.colStatus'),
      width: '150px',
      render: (row) => blockedWhileAvailable(row.operationalStatus, row.readiness)
        ? <span title={row.readinessSummary ?? undefined}><Badge tone="danger">{t('fleet.readiness.level.Blocked')}</Badge></span>
        : <Badge tone={STATUS_TONE[row.operationalStatus]}>{t(TRAILER_STATUS_LABELS[row.operationalStatus])}</Badge>,
    },
    {
      key: 'active',
      header: t('fleet.list.colActive'),
      width: '90px',
      render: (row) => (row.isActive ? <Badge tone="success">{t('fleet.common.yes')}</Badge> : <Badge tone="neutral">{t('fleet.common.no')}</Badge>),
    },
  ]

  return (
    <div>
      <PageHeader
        title={t('navigation.menu.trailers')}
        action={hasPermission('trailers.create') ? <Button onClick={() => navigate('/trailers/new')}>{t('trailers.list.new')}</Button> : undefined}
      />
      <FilterBar
        search={search}
        onSearchChange={(value) => apply({ search: value }, { replace: true })}
        searchPlaceholder={t('fleet.list.searchPlaceholder')}
        activeFilter={activeFilter}
        onActiveFilterChange={(value) => apply({ active: value === undefined ? '' : String(value) })}
        onClear={isFiltered ? reset : undefined}
        trailing={<DensityToggle value={density} onChange={setDensity} />}
      />
      <DataTable
        fixedLayout
        density={density}
        sort={tableSortOf(state.sort, state.dir, DESC_BY_DEFAULT)}
        onSortChange={(next) => apply({ sort: next.key, dir: next.dir })}
        columns={columns}
        rows={items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        errorCause={errorCause}
        emptyMessage={t('trailers.list.empty')}
        emptyAction={hasPermission('trailers.create') ? <Button onClick={() => navigate('/trailers/new')}>{t('trailers.list.new')}</Button> : undefined}
        onRetry={reload}
        loadingMessage={t('trailers.list.loading')}
        onRowClick={(row) => navigate(`/trailers/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={(next) => apply({ page: next })} />
    </div>
  )
}
