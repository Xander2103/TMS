import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { Pagination } from '../../../components/ui/Pagination'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { describeApiError } from '../../../api/problemDetails'
import { downloadAccountingExport } from '../../accounting/api/accountingApi'
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
  const { hasPermission, hasAnyPermission } = useAuth()
  const { showError, showSuccess } = useToast()
  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<InvoiceStatus | ''>('')
  const [page, setPage] = useState(1)
  const [exportOpen, setExportOpen] = useState(false)
  const [exportFrom, setExportFrom] = useState('')
  const [exportTo, setExportTo] = useState('')
  const [exportBusy, setExportBusy] = useState(false)

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
          <>
            {hasAnyPermission(['accounting.view', 'accounting.manage']) && (
              <Button variant="secondary" onClick={() => setExportOpen(true)}>
                Boekhoudexport
              </Button>
            )}
            {hasPermission('invoices.create') && (
              <Button onClick={() => navigate('/invoices/new')}>Nieuwe factuur</Button>
            )}
          </>
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

      {exportOpen && (
        <Modal
          title="Boekhoudexport"
          onClose={() => setExportOpen(false)}
          busy={exportBusy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setExportOpen(false)} disabled={exportBusy}>
                Annuleren
              </Button>
              <Button
                onClick={async () => {
                  if (!exportFrom || !exportTo) {
                    showError('Kies een begin- en einddatum.')
                    return
                  }
                  setExportBusy(true)
                  try {
                    await downloadAccountingExport(exportFrom, exportTo)
                    showSuccess('Boekhoudexport gedownload.')
                    setExportOpen(false)
                  } catch (err) {
                    showError(describeApiError(err, 'De boekhoudexport kon niet worden gemaakt.').message)
                  } finally {
                    setExportBusy(false)
                  }
                }}
                disabled={exportBusy}
              >
                {exportBusy ? 'Bezig…' : 'Exporteren'}
              </Button>
            </>
          }
        >
          <p className="placeholder-text">
            Exporteert alle verzonden en betaalde factuurlijnen (factuurdatum in de periode) met hun vastgelegde
            grootboekrekening. Ontbreekt er een rekening, dan wordt de export geblokkeerd met de betrokken facturen.
          </p>
          <FormField label="Van (factuurdatum)" htmlFor="exp-from" required>
            <input id="exp-from" type="date" value={exportFrom} onChange={(e) => setExportFrom(e.target.value)} />
          </FormField>
          <FormField label="Tot en met" htmlFor="exp-to" required>
            <input id="exp-to" type="date" value={exportTo} onChange={(e) => setExportTo(e.target.value)} />
          </FormField>
        </Modal>
      )}
    </div>
  )
}
