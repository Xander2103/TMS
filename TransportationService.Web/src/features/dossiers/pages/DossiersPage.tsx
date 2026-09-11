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
import { euro } from '../../invoices/types'
import { listDossiers } from '../api/dossiersApi'
import { DOSSIER_STATUS_LABELS, DOSSIER_STATUS_TONE, type DossierListItem, type DossierStatus } from '../types'
import './dossiers.css'

const EMPTY = '—'

/** Tekstcel die bij overloop afkapt met een ellipsis en de volledige waarde als tooltip toont. */
function TruncatedCell({ value }: { value: string | null }) {
  if (!value) return <>{EMPTY}</>
  return (
    <span className="dossier-list-truncate" title={value}>
      {value}
    </span>
  )
}

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
    {
      key: 'number',
      header: t('dossiers.list.columns.number'),
      width: '7rem',
      render: (row) => <code>{row.dossierNumber}</code>,
    },
    {
      key: 'reference',
      header: t('dossiers.list.columns.reference'),
      width: '14rem',
      render: (row) => <TruncatedCell value={row.customerReference} />,
    },
    {
      key: 'customer',
      header: t('dossiers.list.columns.customer'),
      render: (row) => <TruncatedCell value={row.customerName} />,
    },
    {
      key: 'customerNumber',
      header: t('dossiers.list.columns.customerNumber'),
      width: '9rem',
      render: (row) => row.customerNumber ?? EMPTY,
    },
    {
      key: 'price',
      header: t('dossiers.list.columns.price'),
      width: '8rem',
      align: 'right',
      // `null` = nog niet geprijsd; een echte € 0,00 (geprijsd op nul) blijft zichtbaar. Een
      // gedeeltelijk geprijsd dossier (niet elke opdracht heeft een prijs) krijgt een marker:
      // het bedrag is een som over de geprijsde opdrachten, geen dossiertotaal.
      render: (row) =>
        row.agreedPriceTotal === null ? (
          EMPTY
        ) : (
          <span className="dossier-list-amount">
            {euro(row.agreedPriceTotal)}
            {row.pricedOrderCount < row.orderCount && (
              <span
                className="dossier-list-partial"
                title={t('dossierSheet.list.partialPricedTitle', { priced: row.pricedOrderCount, total: row.orderCount })}
              >
                {t('dossierSheet.list.partialPriced', { priced: row.pricedOrderCount, total: row.orderCount })}
              </span>
            )}
          </span>
        ),
    },
    {
      key: 'responsible',
      header: t('dossiers.list.columns.responsible'),
      width: '12rem',
      render: (row) => <TruncatedCell value={row.responsibleName} />,
    },
    {
      key: 'orders',
      header: t('dossiers.list.columns.orders'),
      width: '6rem',
      align: 'right',
      render: (row) => <span className="dossier-list-amount">{row.orderCount}</span>,
    },
    {
      key: 'incidents',
      header: t('dossiers.list.columns.openIncidents'),
      width: '6rem',
      align: 'right',
      render: (row) =>
        row.openIncidentCount > 0 ? (
          <Badge tone="warning">{row.openIncidentCount}</Badge>
        ) : (
          <span className="dossier-list-amount">0</span>
        ),
    },
    {
      key: 'status',
      header: t('dossiers.list.columns.status'),
      width: '6rem',
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
        <div className="dossier-list">
          <DataTable columns={columns} rows={dossiers} rowKey={(row) => row.id} onRowClick={(row) => navigate(`/dossiers/${row.id}`)} />
        </div>
      )}
    </div>
  )
}
