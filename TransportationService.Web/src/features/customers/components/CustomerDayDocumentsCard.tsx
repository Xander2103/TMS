import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale, type TranslateFn } from '../../../i18n/localeContext'
import { describeApiError } from '../../../api/problemDetails'
import {
  downloadCustomerDayDocuments,
  getCustomerDayDocumentsPreview,
  type CustomerDayDocumentRow,
  type CustomerDayDocumentsPreview,
} from '../api/customersApi'

interface CustomerDayDocumentsCardProps {
  customerId: string
}

/** yyyy-MM-dd van vandaag in lokale tijd (voor het date-input). */
function todayIso(): string {
  const now = new Date()
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
}

/** Beslissing/reden-label per orderregel in de voorvertoning (reden komt als tekst van de server). */
function rowDecision(t: TranslateFn, row: CustomerDayDocumentRow): string {
  if (row.usesCustomerDocument) return t('customers.dayDocs.decisionCustomerDocument', { reason: row.reason })
  if (row.noneRequired) return t('customers.dayDocs.decisionNone', { reason: row.reason })
  if (row.undecided) return t('customers.dayDocs.decisionUndecided', { reason: row.reason })
  const kind = row.kind === 'Cmr' ? 'CMR' : t('customers.dayDocs.deliveryNote')
  return `${kind} — ${row.reason}`
}

/**
 * "Documenten per dag": voorvertoning van de leveringsbonnen/CMR's die voor één leveringsdag
 * gegenereerd worden, plus de batch-download per documentsoort. De telling volgt de
 * documentstrategie-resolver — klantdocumenten en "geen document nodig" tellen niet mee.
 */
export function CustomerDayDocumentsCard({ customerId }: CustomerDayDocumentsCardProps) {
  const { showError } = useToast()
  const { t } = useLocale()
  const [date, setDate] = useState(todayIso)
  const [preview, setPreview] = useState<CustomerDayDocumentsPreview | null>(null)
  const [loading, setLoading] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [downloading, setDownloading] = useState<'delivery-note' | 'cmr' | null>(null)

  async function loadPreview() {
    setLoading(true)
    setLoadError(null)
    try {
      setPreview(await getCustomerDayDocumentsPreview(customerId, date))
    } catch (err) {
      setPreview(null)
      setLoadError(describeApiError(err, t('customers.dayDocs.previewFailed')).message)
    } finally {
      setLoading(false)
    }
  }

  async function handleDownload(kind: 'delivery-note' | 'cmr') {
    setDownloading(kind)
    try {
      await downloadCustomerDayDocuments(customerId, kind, date)
    } catch {
      showError(kind === 'cmr' ? t('customers.dayDocs.cmrsFailed') : t('customers.dayDocs.deliveryNotesFailed'))
    } finally {
      setDownloading(null)
    }
  }

  return (
    <div className="customer-summary customer-day-documents">
      <h3>{t('customers.dayDocs.title')}</h3>
      <div className="customer-day-documents-toolbar">
        <FormField label={t('customers.dayDocs.dateLabel')} htmlFor="day-docs-date">
          <input id="day-docs-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        </FormField>
        <Button variant="secondary" onClick={() => void loadPreview()} disabled={loading || !date}>
          {loading ? t('customers.dayDocs.loadingBusy') : t('customers.dayDocs.previewAction')}
        </Button>
        <Button
          variant="secondary"
          onClick={() => void handleDownload('delivery-note')}
          disabled={downloading !== null || (preview?.ownDeliveryNotes ?? 0) === 0}
        >
          {downloading === 'delivery-note' ? t('customers.dayDocs.generatingBusy') : t('customers.dayDocs.downloadDeliveryNotes')}
        </Button>
        <Button
          variant="secondary"
          onClick={() => void handleDownload('cmr')}
          disabled={downloading !== null || (preview?.ownCmrs ?? 0) === 0}
        >
          {downloading === 'cmr' ? t('customers.dayDocs.generatingBusy') : t('customers.dayDocs.downloadCmrs')}
        </Button>
      </div>

      {loadError && (
        <p className="ui-form-field-error" role="alert">
          {loadError}
        </p>
      )}

      {preview && (
        <>
          <p className="customer-form-muted">
            {t('customers.dayDocs.previewSummary', {
              totalOrders: preview.totalOrders,
              ownDeliveryNotes: preview.ownDeliveryNotes,
              ownCmrs: preview.ownCmrs,
              customerDocuments: preview.customerDocuments,
              noneRequired: preview.noneRequired,
              undecided: preview.undecided,
            })}
          </p>
          {preview.rows.length === 0 ? (
            <p className="placeholder-text">{t('customers.dayDocs.noDeliveries', { date: preview.date })}</p>
          ) : (
            <table className="issued-items-table">
              <thead>
                <tr>
                  <th>{t('customers.dayDocs.columnOrderNumber')}</th>
                  <th>{t('customers.dayDocs.columnUnloadingCity')}</th>
                  <th>{t('customers.dayDocs.columnDecision')}</th>
                </tr>
              </thead>
              <tbody>
                {preview.rows.map((row) => (
                  <tr key={row.orderId}>
                    <td>{row.orderNumber}</td>
                    <td>{row.unloadingCity ?? '—'}</td>
                    <td>{rowDecision(t, row)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
    </div>
  )
}
