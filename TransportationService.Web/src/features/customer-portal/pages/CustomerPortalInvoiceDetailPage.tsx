import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { BackButton } from '../../../components/ui/BackButton'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { euro, INVOICE_STATUS_LABELS, INVOICE_STATUS_TONE, type InvoiceStatus } from '../../invoices/types'
import {
  downloadPortalInvoiceAttachment,
  downloadPortalInvoicePdf,
  getPortalInvoice,
  type PortalInvoiceDetail,
} from '../api/customerPortalApi'
import './customer-portal-pages.css'

export function CustomerPortalInvoiceDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const [invoice, setInvoice] = useState<PortalInvoiceDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [downloadError, setDownloadError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    getPortalInvoice(id)
      .then((data) => {
        if (mounted) setInvoice(data)
      })
      .catch(() => {
        if (mounted) setError('De factuur kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [id])

  if (error) return <ErrorState message={error} />
  if (!invoice) return <LoadingState message="Factuur laden..." />

  async function handleDownloadPdf() {
    if (!invoice) return
    setDownloadError(null)
    try {
      await downloadPortalInvoicePdf(invoice.id, invoice.invoiceNumber)
    } catch {
      setDownloadError('De factuur-PDF kon niet worden gedownload.')
    }
  }

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Klantportaal', to: '/klantportaal' }, { label: 'Facturen', to: '/klantportaal/facturen' }, { label: invoice.invoiceNumber }]} />
      <BackButton to="/klantportaal/facturen" label="Terug naar facturen" />
      <PageHeader
        title={invoice.invoiceNumber}
        subtitle={`${invoice.invoiceDate} · vervaldatum ${invoice.dueDate}`}
        action={
          <>
            <Badge tone={INVOICE_STATUS_TONE[invoice.status as InvoiceStatus] ?? 'neutral'}>
              {INVOICE_STATUS_LABELS[invoice.status as InvoiceStatus] ?? invoice.status}
            </Badge>{' '}
            <Button onClick={() => void handleDownloadPdf()}>PDF downloaden</Button>
          </>
        }
      />
      {downloadError && <p className="placeholder-text" role="alert">{downloadError}</p>}
      {invoice.purchaseOrderNumber && <p>PO-nummer: {invoice.purchaseOrderNumber}</p>}

      <section className="cpp-panel">
        <h2>Regels</h2>
        <table className="cpp-table">
          <thead>
            <tr>
              <th>Omschrijving</th>
              <th>Aantal</th>
              <th>Prijs</th>
              <th>BTW%</th>
              <th>Totaal</th>
            </tr>
          </thead>
          <tbody>
            {invoice.lines.map((line, index) => (
              <tr key={index}>
                <td>{line.description}</td>
                <td>{line.quantity}</td>
                <td>{euro(line.unitPrice, invoice.currency)}</td>
                <td>{line.vatRatePercent}%</td>
                <td>{euro(line.lineTotal, invoice.currency)}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <p>
          Subtotaal: {euro(invoice.subtotal, invoice.currency)} · BTW: {euro(invoice.vatAmount, invoice.currency)} ·{' '}
          <strong>Totaal: {euro(invoice.total, invoice.currency)}</strong>
        </p>
      </section>

      {invoice.attachments.length > 0 && (
        <section className="cpp-panel">
          <h2>Bijlagen</h2>
          <ul>
            {invoice.attachments.map((a) => (
              <li key={a.id}>
                <button
                  type="button"
                  className="link-button"
                  onClick={() => void downloadPortalInvoiceAttachment(invoice.id, a.id, a.fileName).catch(() => setDownloadError('De bijlage kon niet worden gedownload.'))}
                >
                  {a.fileName}
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  )
}
