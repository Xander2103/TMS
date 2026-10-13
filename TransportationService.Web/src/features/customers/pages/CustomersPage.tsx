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
import { useLocale } from '../../../i18n/localeContext'
import { searchCustomers } from '../api/customersApi'
import { CustomerImportDialog } from '../components/CustomerImportDialog'
import type { CustomerListItem } from '../types'
import '../components/customers.css'

export function CustomersPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const { t } = useLocale()
  const [search, setSearch] = useState('')
  const [activeFilter, setActiveFilter] = useState<boolean | undefined>(undefined)
  const [page, setPage] = useState(1)
  const [showImportDialog, setShowImportDialog] = useState(false)

  const { items, totalCount, pageSize, isLoading, error, reload } = usePagedQuery<CustomerListItem>(
    (args) => searchCustomers(args),
    { search, isActive: activeFilter, page, errorMessage: t('customers.list.loadFailed') },
  )

  const columns: Column<CustomerListItem>[] = [
    { key: 'number', header: t('customers.list.columnNumber'), width: '130px', render: (row) => <code>{row.customerNumber}</code> },
    { key: 'name', header: t('customers.fields.name'), render: (row) => row.name },
    { key: 'city', header: t('customers.list.columnCity'), render: (row) => row.city ?? '—' },
    { key: 'category', header: t('customers.list.columnCategory'), render: (row) => row.categoryName ?? '—' },
    {
      key: 'status',
      header: t('customers.list.columnStatus'),
      width: '190px',
      render: (row) => (
        <span className="customer-status-badges">
          {row.isActive ? (
            <Badge tone="success">{t('ui.statusBadges.active')}</Badge>
          ) : (
            <Badge tone="neutral">{t('ui.statusBadges.inactive')}</Badge>
          )}
          {row.isBlocked && <Badge tone="danger">{t('ui.statusBadges.blocked')}</Badge>}
        </span>
      ),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.menu.customers') }]} />
      <PageHeader
        title={t('navigation.menu.customers')}
        action={
          <div className="customer-detail-toolbar">
            {hasPermission('customers.import') && (
              <Button variant="secondary" onClick={() => setShowImportDialog(true)}>
                {t('customers.list.importAction')}
              </Button>
            )}
            {hasPermission('customers.create') && (
              <Button onClick={() => navigate('/customers/new')}>{t('customers.list.newCustomer')}</Button>
            )}
          </div>
        }
      />
      <FilterBar
        search={search}
        onSearchChange={(value) => {
          setSearch(value)
          setPage(1)
        }}
        searchPlaceholder={t('customers.list.searchPlaceholder')}
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
        emptyMessage={t('customers.list.empty')}
        loadingMessage={t('customers.list.loading')}
        onRowClick={(row) => navigate(`/customers/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
      {showImportDialog && <CustomerImportDialog onClose={() => setShowImportDialog(false)} onImported={reload} />}
    </div>
  )
}
