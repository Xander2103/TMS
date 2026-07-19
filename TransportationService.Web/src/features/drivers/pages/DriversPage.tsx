import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { StatusBadges } from '../../../components/ui/StatusBadges'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { useAuth } from '../../auth/authContextValue'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { searchDrivers } from '../api/driversApi'
import { AVAILABILITY_LABELS, type DriverAvailabilityStatus, type DriverListItem } from '../types'
import './driver-detail.css'

export function DriversPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const [search, setSearch] = useState('')
  const [activeFilter, setActiveFilter] = useState<boolean | undefined>(undefined)
  const [categoryId, setCategoryId] = useState('')
  const [availability, setAvailability] = useState<DriverAvailabilityStatus | ''>('')
  const [expiryFilter, setExpiryFilter] = useState<'' | '30' | '60' | '90' | 'expired'>('')
  const [eligibleOnly, setEligibleOnly] = useState(false)
  const [page, setPage] = useState(1)

  const categories = useLookupOptions('/api/driver-categories')

  const { items, totalCount, pageSize, isLoading, error, reload } = usePagedQuery<DriverListItem>(
    (args) =>
      searchDrivers({
        ...args,
        categoryId: categoryId || undefined,
        availabilityStatus: availability || undefined,
        expiringWithinDays: expiryFilter && expiryFilter !== 'expired' ? Number(expiryFilter) : undefined,
        hasExpired: expiryFilter === 'expired' || undefined,
        eligibleOnly: eligibleOnly || undefined,
      }),
    { search, isActive: activeFilter, page, errorMessage: 'Chauffeurs konden niet worden geladen.' },
  )

  // Extra filters are not part of the paged-query key: trigger an explicit reload.
  useEffect(() => {
    reload()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [categoryId, availability, expiryFilter, eligibleOnly])

  const columns: Column<DriverListItem>[] = [
    { key: 'number', header: 'Nummer', width: '130px', render: (row) => <code>{row.driverNumber}</code> },
    { key: 'name', header: 'Naam', render: (row) => row.fullName },
    { key: 'employee', header: 'Pers.nr', width: '120px', render: (row) => <code>{row.employeeNumber}</code> },
    { key: 'category', header: 'Categorie', render: (row) => row.categoryName ?? '—' },
    {
      key: 'availability',
      header: 'Beschikbaarheid',
      width: '150px',
      render: (row) => <Badge tone={row.availabilityStatus === 'Available' ? 'success' : 'neutral'}>{AVAILABILITY_LABELS[row.availabilityStatus]}</Badge>,
    },
    {
      key: 'status',
      header: 'Status',
      width: '180px',
      render: (row) => <StatusBadges active={row.isActive} blocked={{ isBlocked: row.isBlocked }} />,
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Chauffeurs' }]} />
      <PageHeader
        title="Chauffeurs"
        action={hasPermission('drivers.create') ? <Button onClick={() => navigate('/drivers/new')}>Nieuwe chauffeur</Button> : undefined}
      />
      <FilterBar
        search={search}
        onSearchChange={(value) => {
          setSearch(value)
          setPage(1)
        }}
        searchPlaceholder="Zoeken op nummer, naam of personeelsnummer..."
        activeFilter={activeFilter}
        onActiveFilterChange={(value) => {
          setActiveFilter(value)
          setPage(1)
        }}
      >
        <select
          aria-label="Filter op categorie"
          value={categoryId}
          onChange={(e) => {
            setCategoryId(e.target.value)
            setPage(1)
          }}
        >
          <option value="">Alle categorieën</option>
          {categories.options.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>
        <select
          aria-label="Filter op beschikbaarheid"
          value={availability}
          onChange={(e) => {
            setAvailability(e.target.value as DriverAvailabilityStatus | '')
            setPage(1)
          }}
        >
          <option value="">Alle beschikbaarheden</option>
          {(Object.keys(AVAILABILITY_LABELS) as DriverAvailabilityStatus[]).map((status) => (
            <option key={status} value={status}>
              {AVAILABILITY_LABELS[status]}
            </option>
          ))}
        </select>
        <select
          aria-label="Filter op kwalificatievervaldatum"
          value={expiryFilter}
          onChange={(e) => {
            setExpiryFilter(e.target.value as typeof expiryFilter)
            setPage(1)
          }}
        >
          <option value="">Alle vervaldata</option>
          <option value="30">Vervalt binnen 30 dagen</option>
          <option value="60">Vervalt binnen 60 dagen</option>
          <option value="90">Vervalt binnen 90 dagen</option>
          <option value="expired">Met verlopen kwalificatie</option>
        </select>
        <label className="driver-filter-checkbox">
          <input
            type="checkbox"
            checked={eligibleOnly}
            onChange={(e) => {
              setEligibleOnly(e.target.checked)
              setPage(1)
            }}
          />
          Alleen inzetbaar
        </label>
      </FilterBar>
      <DataTable
        columns={columns}
        rows={items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage="Nog geen chauffeurs."
        loadingMessage="Chauffeurs laden..."
        onRowClick={(row) => navigate(`/drivers/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
