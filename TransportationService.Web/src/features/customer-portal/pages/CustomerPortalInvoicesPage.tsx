import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { euro, INVOICE_STATUS_LABELS, INVOICE_STATUS_TONE, type InvoiceStatus } from '../../invoices/types'
import { listPortalInvoices, type PortalInvoiceListItem } from '../api/customerPortalApi'

/** Customer-portal invoice overview: own customer's non-Draft invoices only. */
export function CustomerPortalInvoicesPage() {
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
        setError('De facturen konden niet worden geladen.')
        setLoaded(true)
      })
    return () => {
      mounted = false
    }
  }, [])

  const columns: Column<PortalInvoiceListItem>[] = [
    {
      key: 'number',
      header: 'Factuur',
      render: (row) => <Link to={`/klantportaal/facturen/${row.id}`}>{row.invoiceNumber}</Link>,
    },
    { key: 'date', header: 'Datum', render: (row) => row.invoiceDate },
    { key: 'due', header: 'Vervaldatum', render: (row) => row.dueDate },
    { key: 'amount', header: 'Bedrag', render: (row) => euro(row.total, row.currency) },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <Badge tone={INVOICE_STATUS_TONE[row.status as InvoiceStatus] ?? 'neutral'}>
          {INVOICE_STATUS_LABELS[row.status as InvoiceStatus] ?? row.status}
        </Badge>
      ),
    },
    {
      key: 'peppol',
      header: 'Peppol',
      // TODO(Phase 13): real Peppol transmission status; the backend field is a placeholder for now.
      render: (row) => (row.peppolStatus ? <Badge tone="info">{row.peppolStatus}</Badge> : '—'),
    },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Klantportaal', to: '/klantportaal' }, { label: 'Facturen' }]} />
      <PageHeader title="Facturen" subtitle="Klantportaal" />
      <DataTable
        columns={columns}
        rows={invoices}
        rowKey={(row) => row.id}
        isLoading={!loaded}
        error={error}
        emptyMessage="Nog geen facturen beschikbaar."
        loadingMessage="Facturen laden..."
      />
    </div>
  )
}
