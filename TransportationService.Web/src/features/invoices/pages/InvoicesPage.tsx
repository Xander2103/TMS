import { useEffect, useState } from 'react'
import { formatDate } from '../../../utils/dates'
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
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { localizeApiError } from '../../../api/problemDetails'
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
  const { t } = useLocale()
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
    { search, page, errorMessage: t('invoices.internalList.loadError') },
  )

  // The status filter isn't part of usePagedQuery's own dependency key, so trigger a reload
  // explicitly whenever it changes.
  useEffect(() => {
    reload()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [statusFilter])

  const columns: Column<InvoiceListItem>[] = [
    { key: 'number', header: t('invoices.internalList.columns.number'), width: '110px', render: (row) => <code>{row.invoiceNumber}</code> },
    { key: 'date', header: t('invoices.internalList.columns.date'), width: '110px', render: (row) => formatDate(row.invoiceDate) },
    { key: 'due', header: t('invoices.internalList.columns.due'), width: '110px', render: (row) => formatDate(row.dueDate) },
    { key: 'customer', header: t('invoices.internalList.columns.customer'), render: (row) => row.customerName },
    { key: 'total', header: t('invoices.internalList.columns.total'), width: '150px', render: (row) => euro(row.total, row.currency) },
    {
      key: 'status',
      header: t('invoices.internalList.columns.status'),
      width: '130px',
      render: (row) => <Badge tone={INVOICE_STATUS_TONE[row.status]}>{t(INVOICE_STATUS_LABELS[row.status])}</Badge>,
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: t('invoices.list.title') }]} />
      <PageHeader
        title={t('invoices.list.title')}
        action={
          <>
            {hasAnyPermission(['accounting.view', 'accounting.manage']) && (
              <Button variant="secondary" onClick={() => setExportOpen(true)}>
                {t('invoices.export.title')}
              </Button>
            )}
            {hasPermission('invoices.create') && (
              <Button onClick={() => navigate('/invoices/new')}>{t('invoices.internalList.newInvoice')}</Button>
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
          searchPlaceholder={t('invoices.internalList.searchPlaceholder')}
        />
        <select
          value={statusFilter}
          onChange={(e) => {
            setStatusFilter(e.target.value as InvoiceStatus | '')
            setPage(1)
          }}
          className="inv-status-filter"
          aria-label={t('ui.filter.statusLabel')}
        >
          <option value="">{t('ui.filter.allStatuses')}</option>
          {INVOICE_STATUSES.map((status) => (
            <option key={status} value={status}>
              {t(INVOICE_STATUS_LABELS[status])}
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
        emptyMessage={t('invoices.internalList.empty')}
        loadingMessage={t('invoices.list.loading')}
        onRowClick={(row) => navigate(`/invoices/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={setPage} />

      {exportOpen && (
        <Modal
          title={t('invoices.export.title')}
          onClose={() => setExportOpen(false)}
          busy={exportBusy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setExportOpen(false)} disabled={exportBusy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button
                onClick={async () => {
                  if (!exportFrom || !exportTo) {
                    showError(t('invoices.export.missingDates'))
                    return
                  }
                  setExportBusy(true)
                  try {
                    await downloadAccountingExport(exportFrom, exportTo)
                    showSuccess(t('invoices.export.success'))
                    setExportOpen(false)
                  } catch (err) {
                    showError(localizeApiError(t, err, t('invoices.export.error')))
                  } finally {
                    setExportBusy(false)
                  }
                }}
                disabled={exportBusy}
              >
                {exportBusy ? t('invoices.common.busy') : t('invoices.export.action')}
              </Button>
            </>
          }
        >
          <p className="placeholder-text">{t('invoices.export.description')}</p>
          <FormField label={t('invoices.export.from')} htmlFor="exp-from" required>
            <input id="exp-from" type="date" value={exportFrom} onChange={(e) => setExportFrom(e.target.value)} />
          </FormField>
          <FormField label={t('invoices.export.to')} htmlFor="exp-to" required>
            <input id="exp-to" type="date" value={exportTo} onChange={(e) => setExportTo(e.target.value)} />
          </FormField>
        </Modal>
      )}
    </div>
  )
}
