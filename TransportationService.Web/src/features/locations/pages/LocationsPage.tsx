import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { useAuth } from '../../auth/authContextValue'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { duplicateLocation, searchLocations, setLocationActive } from '../api/locationsApi'
import { LOCATION_TYPE_LABELS, LOCATION_TYPES, type LocationListItem, type LocationType } from '../types'
import './locations.css'

export function LocationsPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const [search, setSearch] = useState('')
  const [activeFilter, setActiveFilter] = useState<boolean | undefined>(undefined)
  const [typeFilter, setTypeFilter] = useState<LocationType | ''>('')
  const [countryFilter, setCountryFilter] = useState<string | null>(null)
  const [postalCodeFilter, setPostalCodeFilter] = useState('')
  const [page, setPage] = useState(1)
  const [busyRowId, setBusyRowId] = useState<string | null>(null)

  const { items, totalCount, pageSize, isLoading, error, reload } = usePagedQuery<LocationListItem>(
    (args) =>
      searchLocations({
        ...args,
        type: typeFilter || undefined,
        country: countryFilter ?? undefined,
        postalCode: postalCodeFilter || undefined,
      }),
    { search, isActive: activeFilter, page, errorMessage: 'Locaties konden niet worden geladen.' },
  )

  // Type/country/postcode aren't part of usePagedQuery's own dependency key, so trigger a
  // reload explicitly whenever one of them changes.
  useEffect(() => {
    reload()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [typeFilter, countryFilter, postalCodeFilter])

  const canCreate = hasPermission('locations.create')
  const canEdit = hasPermission('locations.edit')

  async function handleDuplicate(row: LocationListItem) {
    setBusyRowId(row.id)
    try {
      const copy = await duplicateLocation(row.id)
      showSuccess('Locatie gedupliceerd.')
      navigate(`/locations/${copy.id}`)
    } catch {
      showError('Locatie kon niet worden gedupliceerd.')
      setBusyRowId(null)
    }
  }

  async function handleToggleActive(row: LocationListItem) {
    setBusyRowId(row.id)
    try {
      await setLocationActive(row.id, !row.isActive)
      showSuccess(row.isActive ? 'Locatie gedeactiveerd.' : 'Locatie geactiveerd.')
      reload()
    } catch {
      showError('Status kon niet worden gewijzigd.')
    } finally {
      setBusyRowId(null)
    }
  }

  const columns: Column<LocationListItem>[] = [
    { key: 'code', header: 'Code', width: '120px', render: (row) => <code>{row.code}</code> },
    { key: 'name', header: 'Naam', render: (row) => row.name },
    { key: 'type', header: 'Type', width: '150px', render: (row) => LOCATION_TYPE_LABELS[row.type] },
    { key: 'city', header: 'Plaats', render: (row) => row.city ?? '—' },
    { key: 'customer', header: 'Klant', render: (row) => row.customerName ?? '—' },
    {
      key: 'status',
      header: 'Status',
      width: '110px',
      render: (row) => (row.isActive ? <Badge tone="success">Actief</Badge> : <Badge tone="neutral">Inactief</Badge>),
    },
  ]

  if (canCreate || canEdit) {
    columns.push({
      key: 'actions',
      header: 'Acties',
      align: 'right',
      render: (row) => (
        <div className="locations-row-actions" onClick={(event) => event.stopPropagation()}>
          {canCreate && (
            <Button variant="secondary" onClick={() => void handleDuplicate(row)} disabled={busyRowId === row.id}>
              Dupliceren
            </Button>
          )}
          {canEdit && (
            <Button variant="secondary" onClick={() => void handleToggleActive(row)} disabled={busyRowId === row.id}>
              {row.isActive ? 'Deactiveren' : 'Activeren'}
            </Button>
          )}
        </div>
      ),
    })
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Locaties' }]} />
      <PageHeader
        title="Locaties"
        action={canCreate ? <Button onClick={() => navigate('/locations/new')}>Nieuwe locatie</Button> : undefined}
      />
      <div className="locations-filters">
        <FilterBar
          search={search}
          onSearchChange={(value) => {
            setSearch(value)
            setPage(1)
          }}
          searchPlaceholder="Zoeken op code, naam of plaats..."
          activeFilter={activeFilter}
          onActiveFilterChange={(value) => {
            setActiveFilter(value)
            setPage(1)
          }}
        />
        <select
          value={typeFilter}
          onChange={(e) => {
            setTypeFilter(e.target.value as LocationType | '')
            setPage(1)
          }}
          className="locations-type-filter"
          aria-label="Type"
        >
          <option value="">Alle types</option>
          {LOCATION_TYPES.map((t) => (
            <option key={t} value={t}>
              {LOCATION_TYPE_LABELS[t]}
            </option>
          ))}
        </select>
        <div className="locations-country-filter">
          <CountryCombobox
            id="locations-country-filter"
            value={countryFilter}
            onChange={(code) => {
              setCountryFilter(code)
              setPage(1)
            }}
            placeholder="Land"
          />
        </div>
        <input
          className="locations-postal-filter"
          value={postalCodeFilter}
          onChange={(e) => {
            setPostalCodeFilter(e.target.value)
            setPage(1)
          }}
          placeholder="Postcode"
          aria-label="Postcode"
        />
      </div>
      <DataTable
        columns={columns}
        rows={items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage="Nog geen locaties."
        loadingMessage="Locaties laden..."
        onRowClick={(row) => navigate(`/locations/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
