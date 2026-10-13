import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { BackButton } from '../../../components/ui/BackButton'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { useLocale } from '../../../i18n/localeContext'
import { INVOICE_STATUS_TONE, type InvoiceStatus } from '../../invoices/types'
import {
  downloadPortalInvoiceAttachment,
  downloadPortalInvoicePdf,
  getPortalInvoice,
  type PortalInvoiceDetail,
} from '../api/customerPortalApi'
import { invoiceStatusLabel, peppolStatusLabel } from './portalStatusLabels'
import './customer-portal-pages.css'

export function CustomerPortalInvoiceDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const { t, formatDate, formatCurrency } = useLocale()
  const [invoice, setInvoice] = useState<PortalInvoiceDetail | null>(null)
  const [error, setError] = useState(false)
  const [downloadError, setDownloadError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    getPortalInvoice(id)
      .then((data) => {
        if (mounted) setInvoice(data)
      })
      .catch(() => {
        if (mounted) setError(true)
      })
    return () => {
      mounted = false
    }
  }, [id])

  if (error) return <ErrorState message={t('invoices.detail.loadError')} />
  if (!invoice) return <LoadingState message={t('invoices.detail.loading')} />

  async function handleDownloadPdf() {
    if (!invoice) return
    setDownloadError(null)
    try {
      await downloadPortalInvoicePdf(invoice.id, invoice.invoiceNumber)
    } catch {
      setDownloadError(t('invoices.detail.pdfError'))
    }
  }

  return (
    <div>
      <Breadcrumbs
        items={[
          { label: t('navigation.portalName'), to: '/klantportaal' },
          { label: t('invoices.list.title'), to: '/klantportaal/facturen' },
          { label: invoice.invoiceNumber },
        ]}
      />
      <BackButton to="/klantportaal/facturen" label={t('invoices.detail.back')} />
      <PageHeader
        title={invoice.invoiceNumber}
        subtitle={t('invoices.detail.subtitle', { date: formatDate(invoice.invoiceDate), dueDate: formatDate(invoice.dueDate) })}
        action={
          <>
            {invoice.kind === 'CreditNote' && <Badge tone="warning">{t('invoices.creditNote')}</Badge>}{' '}
            <Badge tone={INVOICE_STATUS_TONE[invoice.status as InvoiceStatus] ?? 'neutral'}>
              {invoiceStatusLabel(t, invoice.status)}
            </Badge>{' '}
            <Button onClick={() => void handleDownloadPdf()}>{t('invoices.detail.downloadPdf')}</Button>
          </>
        }
      />
      {downloadError && <p className="placeholder-text" role="alert">{downloadError}</p>}
      {invoice.peppolStatus && (
        <p className="cpp-peppol-status">
          {t('invoices.detail.peppolLabel', { status: peppolStatusLabel(t, invoice.peppolStatus) })}
        </p>
      )}
      {invoice.purchaseOrderNumber && <p>{t('invoices.detail.poNumber', { number: invoice.purchaseOrderNumber })}</p>}

      <section className="cpp-panel">
        <h2>{t('invoices.detail.linesTitle')}</h2>
        <table className="cpp-table">
          <thead>
            <tr>
              <th>{t('invoices.detail.lineColumns.description')}</th>
              <th>{t('invoices.detail.lineColumns.quantity')}</th>
              <th>{t('invoices.detail.lineColumns.price')}</th>
              <th>{t('invoices.detail.lineColumns.vat')}</th>
              <th>{t('invoices.detail.lineColumns.total')}</th>
            </tr>
          </thead>
          <tbody>
            {invoice.lines.map((line, index) => (
              <tr key={index}>
                <td>{line.description}</td>
                <td>{line.quantity}</td>
                <td>{formatCurrency(line.unitPrice, invoice.currency)}</td>
                <td>{line.vatRatePercent}%</td>
                <td>{formatCurrency(line.lineTotal, invoice.currency)}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <p>
          {t('invoices.detail.subtotal')}: {formatCurrency(invoice.subtotal, invoice.currency)} · {t('invoices.detail.vatAmount')}:{' '}
          {formatCurrency(invoice.vatAmount, invoice.currency)} ·{' '}
          <strong>
            {t('invoices.detail.total')}: {formatCurrency(invoice.total, invoice.currency)}
          </strong>
        </p>
      </section>

      {invoice.attachments.length > 0 && (
        <section className="cpp-panel">
          <h2>{t('invoices.detail.attachmentsTitle')}</h2>
          <ul>
            {invoice.attachments.map((a) => (
              <li key={a.id}>
                <button
                  type="button"
                  className="link-button"
                  onClick={() =>
                    void downloadPortalInvoiceAttachment(invoice.id, a.id, a.fileName).catch(() =>
                      setDownloadError(t('invoices.detail.attachmentError')),
                    )
                  }
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
