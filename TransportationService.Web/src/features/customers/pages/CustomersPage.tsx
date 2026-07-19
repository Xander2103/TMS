import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { useAuth } from '../../auth/authContextValue'
import { searchCustomers } from '../api/customersApi'
import type { CustomerListItem } from '../types'
import '../components/customers.css'

export function CustomersPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const [search, setSearch] = useState('')
  const [activeFilter, setActiveFilter] = useState<boolean | undefined>(undefined)
  const [page, setPage] = useState(1)

  const { items, totalCount, pageSize, isLoading, error } = usePagedQuery<CustomerListItem>(
    (args) => searchCustomers(args),
    { search, isActive: activeFilter, page, errorMessage: 'Klanten konden niet worden geladen.' },
  )

  const columns: Column<CustomerListItem>[] = [
    { key: 'number', header: 'Nummer', width: '130px', render: (row) => <code>{row.customerNumber}</code> },
    { key: 'name', header: 'Naam', render: (row) => row.name },
    { key: 'city', header: 'Plaats', render: (row) => row.city ?? '—' },
    { key: 'category', header: 'Categorie', render: (row) => row.categoryName ?? '—' },
    {
      key: 'status',
      header: 'Status',
      width: '190px',
      render: (row) => (
        <span className="customer-status-badges">
          {row.isActive ? <Badge tone="success">Actief</Badge> : <Badge tone="neutral">Inactief</Badge>}
          {row.isBlocked && <Badge tone="danger">Geblokkeerd</Badge>}
        </span>
      ),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Klanten' }]} />
      <PageHeader
        title="Klanten"
        action={
          hasPermission('customers.create') && <Button onClick={() => navigate('/customers/new')}>Nieuwe klant</Button>
        }
      />
      <FilterBar
        search={search}
        onSearchChange={(value) => {
          setSearch(value)
          setPage(1)
        }}
        searchPlaceholder="Zoeken op nummer, naam, BTW of plaats..."
        activeFilter={activeFilter}
        onActiveFilterChange={(value) => {
          setActiveFilter(value)
          setPage(1)
        }}
      />
      <DataTable
        columns={columns}
        rows={items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage="Nog geen klanten."
        loadingMessage="Klanten laden..."
        onRowClick={(row) => navigate(`/customers/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
