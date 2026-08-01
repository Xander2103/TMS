import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useLocale } from '../../../i18n/localeContext'
import { INVOICE_STATUS_TONE, type InvoiceStatus } from '../../invoices/types'
import { listPortalInvoices, type PortalInvoiceListItem } from '../api/customerPortalApi'
import { invoiceStatusLabel, peppolStatusLabel } from './portalStatusLabels'
import './customer-portal-pages.css'

/** Customer-portal invoice overview: own customer's non-Draft invoices only. */
export function CustomerPortalInvoicesPage() {
  const { t, formatDate, formatCurrency } = useLocale()
  const [invoices, setInvoices] = useState<PortalInvoiceListItem[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loaded, setLoaded] = useState(false)

  useEffect(() => {
    let mounted = true
    listPortalInvoices()
      .then((rows) => {
        if (!mounted) return
        setInvoices(rows)
        setLoaded(true)
      })
      .catch(() => {
        if (!mounted) return
        setError(t('invoices.list.loadError'))
        setLoaded(true)
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const columns: Column<PortalInvoiceListItem>[] = [
    {
      key: 'number',
      header: t('invoices.list.columns.invoice'),
      render: (row) => (
        <>
          <Link to={`/klantportaal/facturen/${row.id}`}>{row.invoiceNumber}</Link>{' '}
          {row.kind === 'CreditNote' && <Badge tone="warning">{t('invoices.creditNote')}</Badge>}
        </>
      ),
    },
    { key: 'date', header: t('invoices.list.columns.date'), render: (row) => formatDate(row.invoiceDate) },
    { key: 'due', header: t('invoices.list.columns.dueDate'), render: (row) => formatDate(row.dueDate) },
    { key: 'amount', header: t('invoices.list.columns.amount'), render: (row) => formatCurrency(row.total, row.currency) },
    {
      key: 'status',
      header: t('invoices.list.columns.status'),
      render: (row) => (
        <Badge tone={INVOICE_STATUS_TONE[row.status as InvoiceStatus] ?? 'neutral'}>
          {invoiceStatusLabel(t, row.status)}
        </Badge>
      ),
    },
    {
      key: 'peppol',
      header: t('invoices.list.columns.peppol'),
      render: (row) =>
        row.peppolStatus ? <span className="cpp-peppol-status">{peppolStatusLabel(t, row.peppolStatus)}</span> : '—',
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.portalName'), to: '/klantportaal' }, { label: t('invoices.list.title') }]} />
      <PageHeader title={t('invoices.list.title')} subtitle={t('navigation.portalName')} />
      <DataTable
        columns={columns}
        rows={invoices}
        rowKey={(row) => row.id}
        isLoading={!loaded}
        error={error}
        emptyMessage={t('invoices.list.empty')}
        loadingMessage={t('invoices.list.loading')}
      />
    </div>
  )
}
