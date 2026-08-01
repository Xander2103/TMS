import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useLocale } from '../../../i18n/localeContext'
import { downloadPortalDocument, listPortalDocuments, type PortalDocument } from '../api/customerPortalApi'
import { documentSourceLabel } from './portalStatusLabels'
import './customer-portal-pages.css'

/** Documents aggregated across the customer's own orders and invoices. */
export function CustomerPortalDocumentsPage() {
  const { t, formatDate } = useLocale()
  const [documents, setDocuments] = useState<PortalDocument[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [downloadError, setDownloadError] = useState(false)

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
        setError(t('documents.loadError'))
        setLoaded(true)
      })
    return () => {
      mounted = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function handleDownload(doc: PortalDocument) {
    setDownloadError(false)
    try {
      await downloadPortalDocument(doc.source, doc.id, doc.fileName ?? doc.title)
    } catch {
      setDownloadError(true)
    }
  }

  const columns: Column<PortalDocument>[] = [
    {
      key: 'title',
      header: t('documents.columns.document'),
      render: (row) => (
        <button type="button" className="link-button" onClick={() => void handleDownload(row)}>
          {row.title}
        </button>
      ),
    },
    {
      key: 'type',
      header: t('documents.columns.type'),
      render: (row) => <Badge tone="info">{documentSourceLabel(t, row.source)}</Badge>,
    },
    {
      key: 'order',
      header: t('documents.columns.order'),
      render: (row) => (row.orderId ? <Link to={`/klantportaal/orders/${row.orderId}`}>{row.orderNumber}</Link> : '—'),
    },
    {
      key: 'invoice',
      header: t('documents.columns.invoice'),
      render: (row) => (row.invoiceId ? <Link to={`/klantportaal/facturen/${row.invoiceId}`}>{row.invoiceNumber}</Link> : '—'),
    },
    { key: 'date', header: t('documents.columns.date'), render: (row) => formatDate(row.createdAt) },
  ]

  return (
    <div>
      <Breadcrumbs items={[{ label: t('navigation.portalName'), to: '/klantportaal' }, { label: t('documents.title') }]} />
      <PageHeader title={t('documents.title')} subtitle={t('navigation.portalName')} />
      {downloadError && <p className="placeholder-text" role="alert">{t('errors.documentDownload')}</p>}
      <DataTable
        columns={columns}
        rows={documents}
        rowKey={(row) => `${row.source}-${row.id}`}
        isLoading={!loaded}
        error={error}
        emptyMessage={t('documents.empty')}
        loadingMessage={t('documents.loading')}
      />
    </div>
  )
}
