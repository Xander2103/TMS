import { useEffect, useState } from 'react'
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
import { searchInvoices } from '../api/invoicesApi'
import {
  euro,
  INVOICE_STATUS_LABELS,
  INVOICE_STATUS_TONE,
  INVOICE_STATUSES,
  type InvoiceListItem,
  type InvoiceStatus,
} from '../types'
import './invoices.css'

export function InvoicesPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<InvoiceStatus | ''>('')
  const [page, setPage] = useState(1)

  const { items, totalCount, pageSize, isLoading, error, reload } = usePagedQuery<InvoiceListItem>(
    (args) => searchInvoices({ ...args, status: statusFilter || undefined }),
    { search, page, errorMessage: 'Facturen konden niet worden geladen.' },
  )

  // The status filter isn't part of usePagedQuery's own dependency key, so trigger a reload
  // explicitly whenever it changes.
  useEffect(() => {
    reload()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [statusFilter])

  const columns: Column<InvoiceListItem>[] = [
    { key: 'number', header: 'Nummer', width: '110px', render: (row) => <code>{row.invoiceNumber}</code> },
    { key: 'date', header: 'Datum', width: '110px', render: (row) => row.invoiceDate },
    { key: 'due', header: 'Vervaldag', width: '110px', render: (row) => row.dueDate },
    { key: 'customer', header: 'Klant', render: (row) => row.customerName },
    { key: 'total', header: 'Totaal (incl. btw)', width: '150px', render: (row) => euro(row.total, row.currency) },
    {
      key: 'status',
      header: 'Status',
      width: '130px',
      render: (row) => <Badge tone={INVOICE_STATUS_TONE[row.status]}>{INVOICE_STATUS_LABELS[row.status]}</Badge>,
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Facturen' }]} />
      <PageHeader
        title="Facturen"
        action={
          hasPermission('invoices.create') ? (
            <Button onClick={() => navigate('/invoices/new')}>Nieuwe factuur</Button>
          ) : undefined
        }
      />
      <div className="inv-filters">
        <FilterBar
          search={search}
          onSearchChange={(value) => {
            setSearch(value)
            setPage(1)
          }}
          searchPlaceholder="Zoeken op nummer of klant..."
        />
        <select
          value={statusFilter}
          onChange={(e) => {
            setStatusFilter(e.target.value as InvoiceStatus | '')
            setPage(1)
          }}
          className="inv-status-filter"
          aria-label="Statusfilter"
        >
          <option value="">Alle statussen</option>
          {INVOICE_STATUSES.map((status) => (
            <option key={status} value={status}>
              {INVOICE_STATUS_LABELS[status]}
            </option>
          ))}
        </select>
      </div>
      <DataTable
        columns={columns}
        rows={items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        emptyMessage="Nog geen facturen."
        loadingMessage="Facturen laden..."
        onRowClick={(row) => navigate(`/invoices/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />
    </div>
  )
}
