import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { EmptyState } from '../../../components/ui/EmptyState'
import { FilterBar } from '../../../components/ui/FilterBar'
import { useAuth } from '../../auth/authContextValue'
import { listDossiers } from '../api/dossiersApi'
import { DOSSIER_STATUS_LABELS, DOSSIER_STATUS_TONE, type DossierListItem, type DossierStatus } from '../types'

/** Dossiers: bundels van activiteiten, opdrachten, incidenten en gerelateerde dossiers. */
export function DossiersPage() {
  const navigate = useNavigate()
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
        setError('De dossiers konden niet worden geladen.')
        setLoaded(true)
      })
  }, [search, statusFilter])

  useEffect(() => {
    const timer = window.setTimeout(reload, 250)
    return () => window.clearTimeout(timer)
  }, [reload])

  const columns: Column<DossierListItem>[] = [
    { key: 'number', header: 'Nummer', render: (row) => <code>{row.dossierNumber}</code> },
    { key: 'title', header: 'Titel', render: (row) => row.title },
    { key: 'customer', header: 'Klant', render: (row) => row.customerName ?? '—' },
    { key: 'responsible', header: 'Verantwoordelijke', render: (row) => row.responsibleName ?? '—' },
    { key: 'orders', header: 'Opdrachten', render: (row) => String(row.orderCount) },
    {
      key: 'incidents',
      header: 'Open incidenten',
      render: (row) =>
        row.openIncidentCount > 0 ? <Badge tone="warning">{row.openIncidentCount}</Badge> : '0',
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => <Badge tone={DOSSIER_STATUS_TONE[row.status]}>{DOSSIER_STATUS_LABELS[row.status]}</Badge>,
    },
  ]

  return (
    <div>
      <PageHeader
        title="Dossiers"
        subtitle="Bundel activiteiten, opdrachten en incidenten per zaak."
        action={canManage ? <Button onClick={() => navigate('/dossiers/new')}>Nieuw dossier</Button> : undefined}
      />

      <FilterBar search={search} onSearchChange={setSearch} searchPlaceholder="Zoek op nummer of titel...">
        <select
          value={statusFilter}
          onChange={(event) => setStatusFilter(event.target.value as '' | DossierStatus)}
          aria-label="Status"
        >
          <option value="">Alle statussen</option>
          <option value="Open">Open</option>
          <option value="Closed">Gesloten</option>
        </select>
      </FilterBar>

      {error && <p className="placeholder-text">{error}</p>}
      {!error && loaded && dossiers.length === 0 && (
        <EmptyState message="Geen dossiers gevonden. Maak een dossier aan om activiteiten en opdrachten te bundelen." />
      )}
      {!error && dossiers.length > 0 && (
        <DataTable columns={columns} rows={dossiers} rowKey={(row) => row.id} onRowClick={(row) => navigate(`/dossiers/${row.id}`)} />
      )}
    </div>
  )
}
