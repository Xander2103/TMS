import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { EmptyState } from '../../../components/ui/EmptyState'
import { FilterBar } from '../../../components/ui/FilterBar'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { listDossiers } from '../api/dossiersApi'
import { DOSSIER_STATUS_LABELS, DOSSIER_STATUS_TONE, type DossierListItem, type DossierStatus } from '../types'

/** Dossiers: bundels van activiteiten, opdrachten, incidenten en gerelateerde dossiers. */
export function DossiersPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('dossiers.manage')

  const [dossiers, setDossiers] = useState<DossierListItem[]>([])
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<'' | DossierStatus>('')

  const reload = useCallback(() => {
    listDossiers({ search: search || undefined, status: statusFilter || undefined })
      .then((data) => {
        setDossiers(data)
        setError(null)
        setLoaded(true)
      })
      .catch(() => {
        setError(t('dossiers.list.loadFailed'))
        setLoaded(true)
      })
  }, [search, statusFilter, t])

  useEffect(() => {
    const timer = window.setTimeout(reload, 250)
    return () => window.clearTimeout(timer)
  }, [reload])

  const columns: Column<DossierListItem>[] = [
    { key: 'number', header: t('dossiers.list.columns.number'), render: (row) => <code>{row.dossierNumber}</code> },
    { key: 'title', header: t('dossiers.list.columns.title'), render: (row) => row.title },
    { key: 'customer', header: t('dossiers.list.columns.customer'), render: (row) => row.customerName ?? '—' },
    { key: 'responsible', header: t('dossiers.list.columns.responsible'), render: (row) => row.responsibleName ?? '—' },
    { key: 'orders', header: t('dossiers.list.columns.orders'), render: (row) => String(row.orderCount) },
    {
      key: 'incidents',
      header: t('dossiers.list.columns.openIncidents'),
      render: (row) =>
        row.openIncidentCount > 0 ? <Badge tone="warning">{row.openIncidentCount}</Badge> : '0',
    },
    {
      key: 'status',
      header: t('dossiers.list.columns.status'),
      render: (row) => <Badge tone={DOSSIER_STATUS_TONE[row.status]}>{t(DOSSIER_STATUS_LABELS[row.status])}</Badge>,
    },
  ]

  return (
    <div>
      <PageHeader
        title={t('dossiers.list.title')}
        subtitle={t('dossiers.list.subtitle')}
        action={canManage ? <Button onClick={() => navigate('/dossiers/new')}>{t('dossiers.list.new')}</Button> : undefined}
      />

      <FilterBar search={search} onSearchChange={setSearch} searchPlaceholder={t('dossiers.list.searchPlaceholder')}>
        <select
          value={statusFilter}
          onChange={(event) => setStatusFilter(event.target.value as '' | DossierStatus)}
          aria-label={t('dossiers.list.statusAria')}
        >
          <option value="">{t('ui.filter.allStatuses')}</option>
          <option value="Open">{t('dossiers.status.Open')}</option>
          <option value="Closed">{t('dossiers.status.Closed')}</option>
        </select>
      </FilterBar>

      {error && <p className="placeholder-text">{error}</p>}
      {!error && loaded && dossiers.length === 0 && (
        <EmptyState message={t('dossiers.list.empty')} />
      )}
      {!error && dossiers.length > 0 && (
        <DataTable columns={columns} rows={dossiers} rowKey={(row) => row.id} onRowClick={(row) => navigate(`/dossiers/${row.id}`)} />
      )}
    </div>
  )
}
