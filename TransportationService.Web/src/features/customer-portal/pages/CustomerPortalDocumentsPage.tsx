import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { downloadPortalDocument, listPortalDocuments, type PortalDocument } from '../api/customerPortalApi'
import './customer-portal-pages.css'

const SOURCE_LABELS: Record<PortalDocument['source'], string> = {
  OrderDocument: 'Opdrachtdocument',
  Pod: 'Afleverbewijs',
  InvoiceAttachment: 'Factuurbijlage',
}

function formatDate(iso: string): string {
  const date = new Date(iso.endsWith('Z') || iso.includes('+') ? iso : `${iso}Z`)
  return date.toLocaleDateString('nl-BE')
}

/** Documents aggregated across the customer's own orders and invoices. */
export function CustomerPortalDocumentsPage() {
  const [documents, setDocuments] = useState<PortalDocument[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [downloadError, setDownloadError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    listPortalDocuments()
      .then((rows) => {
        if (!mounted) return
        setDocuments(rows)
        setLoaded(true)
      })
      .catch(() => {
        if (!mounted) return
        setError('De documenten konden niet worden geladen.')
        setLoaded(true)
      })
    return () => {
      mounted = false
    }
  }, [])

  async function handleDownload(doc: PortalDocument) {
    setDownloadError(null)
    try {
      await downloadPortalDocument(doc.source, doc.id, doc.fileName ?? doc.title)
    } catch {
      setDownloadError('Het document kon niet worden gedownload.')
    }
  }

  const columns: Column<PortalDocument>[] = [
    {
      key: 'title',
      header: 'Document',
      render: (row) => (
        <button type="button" className="link-button" onClick={() => void handleDownload(row)}>
          {row.title}
        </button>
      ),
    },
    { key: 'type', header: 'Type', render: (row) => <Badge tone="info">{SOURCE_LABELS[row.source]}</Badge> },
    {
      key: 'order',
      header: 'Opdracht',
      render: (row) => (row.orderId ? <Link to={`/klantportaal/orders/${row.orderId}`}>{row.orderNumber}</Link> : '—'),
    },
    {
      key: 'invoice',
      header: 'Factuur',
      render: (row) => (row.invoiceId ? <Link to={`/klantportaal/facturen/${row.invoiceId}`}>{row.invoiceNumber}</Link> : '—'),
    },
    { key: 'date', header: 'Datum', render: (row) => formatDate(row.createdAt) },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Klantportaal', to: '/klantportaal' }, { label: 'Documenten' }]} />
      <PageHeader title="Documenten" subtitle="Klantportaal" />
      {downloadError && <p className="placeholder-text" role="alert">{downloadError}</p>}
      <DataTable
        columns={columns}
        rows={documents}
        rowKey={(row) => `${row.source}-${row.id}`}
        isLoading={!loaded}
        error={error}
        emptyMessage="Nog geen documenten beschikbaar."
        loadingMessage="Documenten laden..."
      />
    </div>
  )
}
