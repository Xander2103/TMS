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
import { PEPPOL_STATUS_LABEL_KEYS, PEPPOL_STATUS_TONE, type PeppolTransmissionStatus } from '../../peppol/api/peppolApi'
import { searchInvoices } from '../api/invoicesApi'
import {
  euro,
  INVOICE_PAID_FILTERS,
  INVOICE_STATUS_LABELS,
  INVOICE_STATUS_TONE,
  INVOICE_STATUSES,
  type InvoiceListItem,
  type InvoicePaidFilter,
  type InvoiceStatus,
} from '../types'
import './invoices.css'
import { TruncatedText } from '../../../components/ui/TruncatedText'

/** Peppol statuses a document can actually be in after a send (Draft/Validated are reserved). */
const PEPPOL_FILTER_STATUSES = (Object.keys(PEPPOL_STATUS_LABEL_KEYS) as PeppolTransmissionStatus[]).filter(
  (status) => status !== 'Draft' && status !== 'Validated',
)

/** B26 / ACC-M10: the whole list state lives in the URL (refresh, Back and shared links keep the view). */
const LIST_STATE = {
  defaults: { search: '', status: '', sort: '', dir: '', dateFrom: '', dateTo: '', paid: '', peppolStatus: '' },
  allowed: {
    status: INVOICE_STATUSES,
    sort: ['number', 'date', 'due', 'customer', 'status'],
    dir: ['asc', 'desc'],
    paid: INVOICE_PAID_FILTERS,
    peppolStatus: PEPPOL_FILTER_STATUSES,
  },
}
const DESC_BY_DEFAULT = ['date', 'due']

/** True for a well-formed ISO calendar date; anything else is ignored rather than sent to the API. */
function isIsoDate(value: string): boolean {
  return /^\d{4}-\d{2}-\d{2}$/.test(value)
}

export function InvoicesPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const { hasPermission, hasAnyPermission } = useAuth()
  const { showError, showSuccess } = useToast()
  const { state, apply, reset, isFiltered } = useUrlListState(LIST_STATE)
  const { search, page } = state
  const statusFilter = state.status as InvoiceStatus | ''
  const paidFilter = state.paid as InvoicePaidFilter | ''
  const peppolFilter = state.peppolStatus as PeppolTransmissionStatus | ''
  const dateFrom = isIsoDate(state.dateFrom) ? state.dateFrom : ''
  const dateTo = isIsoDate(state.dateTo) ? state.dateTo : ''
  const [density, setDensity] = useListDensity('invoices')
  const [exportOpen, setExportOpen] = useState(false)
  const [exportFrom, setExportFrom] = useState('')
  const [exportTo, setExportTo] = useState('')
  const [exportBusy, setExportBusy] = useState(false)

  const { items, totalCount, pageSize, isLoading, error, errorCause, reload } = usePagedQuery<InvoiceListItem>(
    (args) =>
      searchInvoices({
        ...args,
        status: statusFilter || undefined,
        sort: state.sort,
        dir: state.dir as 'asc' | 'desc' | '',
        dateFrom: dateFrom || undefined,
        dateTo: dateTo || undefined,
        paid: paidFilter || undefined,
        peppolStatus: peppolFilter || undefined,
      }),
    {
      search,
      page,
      errorMessage: t('invoices.internalList.loadError'),
      extra: { status: statusFilter, sort: state.sort, dir: state.dir, dateFrom, dateTo, paid: paidFilter, peppolStatus: peppolFilter },
    },
  )

  /** ACC-H3: a draft has no legal number yet — show its draft reference with a "Concept" badge. */
  function numberCell(row: InvoiceListItem) {
    const label = row.displayNumber || row.invoiceNumber || '—'
    const isDraft = row.status === 'Draft' || !row.invoiceNumber
    return (
      <span className="inv-list-number">
        <Link to={`/invoices/${row.id}`} className="ui-id-link" onClick={(event) => event.stopPropagation()}>
          {label}
        </Link>
        {isDraft && <Badge tone="neutral">{t('invoices.internalList.draft')}</Badge>}
        {row.kind === 'CreditNote' && <Badge tone="warning">{t('invoices.creditNote')}</Badge>}
      </span>
    )
  }

  const columns: Column<InvoiceListItem>[] = [
    { key: 'number', header: t('invoices.internalList.columns.number'), width: '200px', sortKey: 'number', render: numberCell },
    { key: 'date', header: t('invoices.internalList.columns.date'), width: '112px', sortKey: 'date', render: (row) => <span className="ui-nowrap">{formatDate(row.invoiceDate)}</span> },
    { key: 'due', header: t('invoices.internalList.columns.due'), width: '112px', sortKey: 'due', render: (row) => <span className="ui-nowrap">{formatDate(row.dueDate)}</span> },
    {
      key: 'customer',
      header: t('invoices.internalList.columns.customer'),
      sortKey: 'customer',
      render: (row) => (
        <TruncatedText tooltip={row.customerNumber ? `${row.customerName} · ${row.customerNumber}` : row.customerName}>
          {row.customerName}
          {row.customerNumber ? <span className="inv-list-muted"> · {row.customerNumber}</span> : null}
        </TruncatedText>
      ),
    },
    { key: 'total', header: t('invoices.internalList.columns.total'), width: '140px', align: 'right', render: (row) => <span className="ui-nowrap ui-amount">{euro(row.total, row.currency)}</span> },
    {
      key: 'status',
      header: t('invoices.internalList.columns.status'),
      width: '130px',
      sortKey: 'status',
      render: (row) => <Badge tone={INVOICE_STATUS_TONE[row.status]}>{t(INVOICE_STATUS_LABELS[row.status])}</Badge>,
    },
    {
      key: 'peppol',
      header: t('invoices.internalList.columns.peppol'),
      width: '150px',
      render: (row) => {
        const status = row.peppolStatus as PeppolTransmissionStatus | null | undefined
        return status && PEPPOL_STATUS_LABEL_KEYS[status] ? (
          <Badge tone={PEPPOL_STATUS_TONE[status]}>{t(PEPPOL_STATUS_LABEL_KEYS[status])}</Badge>
        ) : (
          <span className="inv-list-muted">—</span>
        )
      },
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
        searchLabel={t('invoices.internalList.searchLabel')}
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
        <select
          value={paidFilter}
          onChange={(e) => apply({ paid: e.target.value })}
          className="ui-filter-select"
          aria-label={t('invoices.internalList.filters.paidLabel')}
        >
          <option value="">{t('invoices.internalList.filters.paidAll')}</option>
          <option value="paid">{t('invoices.internalList.filters.paid')}</option>
          <option value="unpaid">{t('invoices.internalList.filters.unpaid')}</option>
        </select>
        <input
          type="date"
          className="ui-filter-select"
          aria-label={t('invoices.internalList.filters.dateFrom')}
          title={t('invoices.internalList.filters.dateFrom')}
          value={dateFrom}
          onChange={(e) => apply({ dateFrom: e.target.value })}
        />
        <input
          type="date"
          className="ui-filter-select"
          aria-label={t('invoices.internalList.filters.dateTo')}
          title={t('invoices.internalList.filters.dateTo')}
          value={dateTo}
          onChange={(e) => apply({ dateTo: e.target.value })}
        />
        <select
          value={peppolFilter}
          onChange={(e) => apply({ peppolStatus: e.target.value })}
          className="ui-filter-select"
          aria-label={t('invoices.internalList.filters.peppolLabel')}
        >
          <option value="">{t('invoices.internalList.filters.peppolAll')}</option>
          {PEPPOL_FILTER_STATUSES.map((status) => (
            <option key={status} value={status}>
              {t(PEPPOL_STATUS_LABEL_KEYS[status])}
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
        errorCause={errorCause}
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
