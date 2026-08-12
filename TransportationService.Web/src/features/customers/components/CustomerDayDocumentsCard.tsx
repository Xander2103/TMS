import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
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

/** Beslissing/reden-label per orderregel in de voorvertoning. */
function rowDecision(row: CustomerDayDocumentRow): string {
  if (row.usesCustomerDocument) return `Klantdocument — ${row.reason}`
  if (row.noneRequired) return `Geen document — ${row.reason}`
  if (row.undecided) return `Nog te beslissen — ${row.reason}`
  const kind = row.kind === 'Cmr' ? 'CMR' : 'Leveringsbon'
  return `${kind} — ${row.reason}`
}

/**
 * "Documenten per dag": voorvertoning van de leveringsbonnen/CMR's die voor één leveringsdag
 * gegenereerd worden, plus de batch-download per documentsoort. De telling volgt de
 * documentstrategie-resolver — klantdocumenten en "geen document nodig" tellen niet mee.
 */
export function CustomerDayDocumentsCard({ customerId }: CustomerDayDocumentsCardProps) {
  const { showError } = useToast()
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
      setLoadError(describeApiError(err, 'De voorvertoning kon niet worden geladen.').message)
    } finally {
      setLoading(false)
    }
  }

  async function handleDownload(kind: 'delivery-note' | 'cmr') {
    setDownloading(kind)
    try {
      await downloadCustomerDayDocuments(customerId, kind, date)
    } catch {
      showError(
        kind === 'cmr'
          ? "De CMR's konden niet worden gegenereerd."
          : 'De leveringsbonnen konden niet worden gegenereerd.',
      )
    } finally {
      setDownloading(null)
    }
  }

  return (
    <div className="customer-summary customer-day-documents">
      <h3>Documenten per dag</h3>
      <div className="customer-day-documents-toolbar">
        <FormField label="Leveringsdag" htmlFor="day-docs-date">
          <input id="day-docs-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        </FormField>
        <Button variant="secondary" onClick={() => void loadPreview()} disabled={loading || !date}>
          {loading ? 'Laden…' : 'Voorvertoning'}
        </Button>
        <Button
          variant="secondary"
          onClick={() => void handleDownload('delivery-note')}
          disabled={downloading !== null || (preview?.ownDeliveryNotes ?? 0) === 0}
        >
          {downloading === 'delivery-note' ? 'Genereren…' : 'Download leveringsbonnen'}
        </Button>
        <Button
          variant="secondary"
          onClick={() => void handleDownload('cmr')}
          disabled={downloading !== null || (preview?.ownCmrs ?? 0) === 0}
        >
          {downloading === 'cmr' ? 'Genereren…' : "Download CMR's"}
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
            {preview.totalOrders} leveringen · {preview.ownDeliveryNotes} eigen leveringsbonnen · {preview.ownCmrs}{' '}
            CMR&apos;s · {preview.customerDocuments} klantdocumenten · {preview.noneRequired} zonder document ·{' '}
            {preview.undecided} nog te beslissen
          </p>
          {preview.rows.length === 0 ? (
            <p className="placeholder-text">Geen leveringen voor deze klant op {preview.date}.</p>
          ) : (
            <table className="issued-items-table">
              <thead>
                <tr>
                  <th>Ordernummer</th>
                  <th>Losstad</th>
                  <th>Beslissing</th>
                </tr>
              </thead>
              <tbody>
                {preview.rows.map((row) => (
                  <tr key={row.orderId}>
                    <td>{row.orderNumber}</td>
                    <td>{row.unloadingCity ?? '—'}</td>
                    <td>{rowDecision(row)}</td>
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
