import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { Pagination } from '../../../components/ui/Pagination'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { tableSortOf, useUrlListState } from '../../../hooks/useUrlListState'
import { DensityToggle } from '../../../components/ui/DensityToggle'
import { useListDensity } from '../../../components/ui/listDensity'
import { TruncatedText } from '../../../components/ui/TruncatedText'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { searchCustomers } from '../api/customersApi'
import { CustomerImportDialog } from '../components/CustomerImportDialog'
import type { CustomerListItem } from '../types'
import '../components/customers.css'

/** B26: list state in the URL (refresh, Back and shared links keep the view). */
const LIST_STATE = {
  defaults: { search: '', active: '', sort: '', dir: '' },
  allowed: { active: ['true', 'false'], sort: ['name', 'number', 'city'], dir: ['asc', 'desc'] },
}
const DESC_BY_DEFAULT: string[] = []

export function CustomersPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const { t } = useLocale()
  const { state, apply, reset, isFiltered } = useUrlListState(LIST_STATE)
  const { search, page } = state
  const activeFilter = state.active === '' ? undefined : state.active === 'true'
  const [density, setDensity] = useListDensity('customers')
  const [showImportDialog, setShowImportDialog] = useState(false)

  const { items, totalCount, pageSize, isLoading, error, reload } = usePagedQuery<CustomerListItem>(
    (args) => searchCustomers({ ...args, sort: state.sort, dir: state.dir as 'asc' | 'desc' | '' }),
    { search, isActive: activeFilter, page, errorMessage: t('customers.list.loadFailed'), extra: { sort: state.sort, dir: state.dir } },
  )

  const columns: Column<CustomerListItem>[] = [
    { key: 'number', header: t('customers.list.columnNumber'), width: '130px', sortKey: 'number', render: (row) => (
      <Link to={`/customers/${row.id}`} className="ui-id-link" onClick={(event) => event.stopPropagation()}>
        {row.customerNumber}
      </Link>
    ) },
    { key: 'name', header: t('customers.fields.name'), sortKey: 'name', render: (row) => <TruncatedText tooltip={row.name}>{row.name}</TruncatedText> },
    { key: 'city', header: t('customers.list.columnCity'), width: '180px', sortKey: 'city', render: (row) => (row.city ? <TruncatedText tooltip={row.city}>{row.city}</TruncatedText> : '—') },
    { key: 'category', header: t('customers.list.columnCategory'), width: '180px', render: (row) => (row.categoryName ? <TruncatedText tooltip={row.categoryName}>{row.categoryName}</TruncatedText> : '—') },
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
        onSearchChange={(value) => apply({ search: value }, { replace: true })}
        searchPlaceholder={t('customers.list.searchPlaceholder')}
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
        emptyMessage={t('customers.list.empty')}
        emptyAction={hasPermission('customers.create') ? <Button onClick={() => navigate('/customers/new')}>{t('customers.list.newCustomer')}</Button> : undefined}
        onRetry={reload}
        loadingMessage={t('customers.list.loading')}
        onRowClick={(row) => navigate(`/customers/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={(next) => apply({ page: next })} />
      {showImportDialog && <CustomerImportDialog onClose={() => setShowImportDialog(false)} onImported={reload} />}
    </div>
  )
}
