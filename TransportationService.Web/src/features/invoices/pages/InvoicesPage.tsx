import { useState } from 'react'
import { formatDate } from '../../../utils/dates'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { FilterBar } from '../../../components/ui/FilterBar'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { Pagination } from '../../../components/ui/Pagination'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { usePagedQuery } from '../../../hooks/usePagedQuery'
import { tableSortOf, useUrlListState } from '../../../hooks/useUrlListState'
import { DensityToggle } from '../../../components/ui/DensityToggle'
import { useListDensity } from '../../../components/ui/listDensity'
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
import { TruncatedText } from '../../../components/ui/TruncatedText'

/** B26: list state in the URL (refresh, Back and shared links keep the view). */
const LIST_STATE = {
  defaults: { search: '', status: '', sort: '', dir: '' },
  allowed: { status: INVOICE_STATUSES, sort: ['number', 'date', 'due', 'customer', 'status'], dir: ['asc', 'desc'] },
}
const DESC_BY_DEFAULT = ['date', 'due']

export function InvoicesPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const { hasPermission, hasAnyPermission } = useAuth()
  const { showError, showSuccess } = useToast()
  const { state, apply, reset, isFiltered } = useUrlListState(LIST_STATE)
  const { search, page } = state
  const statusFilter = state.status as InvoiceStatus | ''
  const [density, setDensity] = useListDensity('invoices')
  const [exportOpen, setExportOpen] = useState(false)
  const [exportFrom, setExportFrom] = useState('')
  const [exportTo, setExportTo] = useState('')
  const [exportBusy, setExportBusy] = useState(false)

  const { items, totalCount, pageSize, isLoading, error, reload } = usePagedQuery<InvoiceListItem>(
    (args) => searchInvoices({ ...args, status: statusFilter || undefined, sort: state.sort, dir: state.dir as 'asc' | 'desc' | '' }),
    { search, page, errorMessage: t('invoices.internalList.loadError'), extra: { status: statusFilter, sort: state.sort, dir: state.dir } },
  )

  const columns: Column<InvoiceListItem>[] = [
    { key: 'number', header: t('invoices.internalList.columns.number'), width: '170px', sortKey: 'number', render: (row) => (
      <Link to={`/invoices/${row.id}`} className="ui-id-link" onClick={(event) => event.stopPropagation()}>
        {row.invoiceNumber}
      </Link>
    ) },
    { key: 'date', header: t('invoices.internalList.columns.date'), width: '112px', sortKey: 'date', render: (row) => <span className="ui-nowrap">{formatDate(row.invoiceDate)}</span> },
    { key: 'due', header: t('invoices.internalList.columns.due'), width: '112px', sortKey: 'due', render: (row) => <span className="ui-nowrap">{formatDate(row.dueDate)}</span> },
    { key: 'customer', header: t('invoices.internalList.columns.customer'), sortKey: 'customer', render: (row) => <TruncatedText tooltip={row.customerName}>{row.customerName}</TruncatedText> },
    { key: 'total', header: t('invoices.internalList.columns.total'), width: '140px', align: 'right', render: (row) => <span className="ui-nowrap ui-amount">{euro(row.total, row.currency)}</span> },
    {
      key: 'status',
      header: t('invoices.internalList.columns.status'),
      width: '130px',
      sortKey: 'status',
      render: (row) => <Badge tone={INVOICE_STATUS_TONE[row.status]}>{t(INVOICE_STATUS_LABELS[row.status])}</Badge>,
    },
  ]

  return (
    <div>
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
      <FilterBar
        search={search}
        onSearchChange={(value) => apply({ search: value }, { replace: true })}
        searchPlaceholder={t('invoices.internalList.searchPlaceholder')}
        onClear={isFiltered ? reset : undefined}
        trailing={<DensityToggle value={density} onChange={setDensity} />}
      >
        <select
          value={statusFilter}
          onChange={(e) => apply({ status: e.target.value })}
          className="ui-filter-select"
          aria-label={t('ui.filter.statusLabel')}
        >
          <option value="">{t('ui.filter.allStatuses')}</option>
          {INVOICE_STATUSES.map((status) => (
            <option key={status} value={status}>
              {t(INVOICE_STATUS_LABELS[status])}
            </option>
          ))}
        </select>
      </FilterBar>
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
        emptyMessage={t('invoices.internalList.empty')}
        emptyAction={undefined}
        onRetry={reload}
        loadingMessage={t('invoices.list.loading')}
        onRowClick={(row) => navigate(`/invoices/${row.id}`)}
      />
      <Pagination page={page} pageSize={pageSize} totalCount={totalCount} onPageChange={(next) => apply({ page: next })} />

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
