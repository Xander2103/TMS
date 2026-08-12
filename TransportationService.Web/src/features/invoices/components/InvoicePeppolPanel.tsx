import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import {
  downloadInvoicePeppolXml,
  getInvoicePeppolPreview,
  listInvoicePeppolTransmissions,
  peppolStatusLabel,
  peppolStatusTone,
  sendInvoicePeppol,
  validateInvoicePeppol,
  PEPPOL_KIND_LABELS,
  type PeppolInvoicePreview,
  type PeppolInvoiceValidationResult,
  type PeppolTransmission,
} from '../../peppol/api/peppolApi'
import { euro, type InvoiceStatus } from '../types'
import { formatDateTime } from '../../../utils/dates'
import '../../peppol/pages/peppol.css'

interface InvoicePeppolPanelProps {
  invoiceId: string
  invoiceNumber: string
  invoiceStatus: InvoiceStatus
}

/**
 * Peppol-paneel op het factuurdetail: laatste verzendstatus, validatie, gestructureerd
 * voorbeeld, XML-download en verzenden — plus de volledige transmissiehistoriek met events.
 */
export function InvoicePeppolPanel({ invoiceId, invoiceNumber, invoiceStatus }: InvoicePeppolPanelProps) {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()

  const canValidate = hasPermission('peppol.validate')
  const canView = hasPermission('peppol.view')
  const canSend = hasPermission('peppol.send')

  const [transmissions, setTransmissions] = useState<PeppolTransmission[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [validation, setValidation] = useState<PeppolInvoiceValidationResult | null>(null)
  const [preview, setPreview] = useState<PeppolInvoicePreview | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    listInvoicePeppolTransmissions(invoiceId)
      .then((rows) => {
        setTransmissions([...rows].sort((a, b) => b.createdAt.localeCompare(a.createdAt)))
        setLoadError(null)
      })
      .catch(() => setLoadError('De Peppol-transmissies konden niet worden geladen.'))
  }, [invoiceId])

  useEffect(() => {
    reload()
  }, [reload])

  const latest = transmissions?.[0] ?? null
  const sendable = invoiceStatus === 'Sent' || invoiceStatus === 'Paid'

  async function handleValidate() {
    setBusy(true)
    try {
      setValidation(await validateInvoicePeppol(invoiceId))
    } catch (err) {
      showError(describeApiError(err, 'De factuur kon niet worden gevalideerd.').message)
    } finally {
      setBusy(false)
    }
  }

  async function handlePreview() {
    setBusy(true)
    try {
      setPreview(await getInvoicePeppolPreview(invoiceId))
    } catch (err) {
      showError(describeApiError(err, 'Het Peppol-voorbeeld kon niet worden geladen.').message)
    } finally {
      setBusy(false)
    }
  }

  async function handleDownloadXml() {
    setBusy(true)
    try {
      await downloadInvoicePeppolXml(invoiceId, invoiceNumber)
    } catch (err) {
      showError(describeApiError(err, 'De Peppol-XML kon niet worden gedownload.').message)
    } finally {
      setBusy(false)
    }
  }

  async function handleSend() {
    setBusy(true)
    try {
      await sendInvoicePeppol(invoiceId)
      showSuccess('Factuur aangeboden voor verzending via Peppol.')
      setValidation(null)
      reload()
    } catch (err) {
      showError(describeApiError(err, 'De factuur kon niet via Peppol worden verstuurd.').message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="inv-section">
      <h3>Peppol</h3>

      {loadError && (
        <p className="placeholder-text" role="alert">
          {loadError}
        </p>
      )}

      <div className="peppol-panel-status">
        {latest ? (
          <>
            <Badge tone={peppolStatusTone(latest.status)}>{peppolStatusLabel(latest.status)}</Badge>
            {latest.providerMessageId && (
              <span>
                Provider-referentie: <code>{latest.providerMessageId}</code>
              </span>
            )}
            {latest.errorDetail && <span className="edi-error" title={latest.errorDetail}>{latest.errorDetail}</span>}
          </>
        ) : (
          transmissions !== null && <span className="peppol-preview-meta">Nog niet via Peppol verzonden.</span>
        )}
      </div>

      <div className="peppol-panel-actions">
        {canValidate && (
          <Button variant="secondary" onClick={() => void handleValidate()} disabled={busy}>
            Valideren
          </Button>
        )}
        {(canValidate || canView) && (
          <Button variant="secondary" onClick={() => void handlePreview()} disabled={busy}>
            Voorbeeld
          </Button>
        )}
        {canValidate && (
          <Button variant="secondary" onClick={() => void handleDownloadXml()} disabled={busy}>
            XML downloaden
          </Button>
        )}
        {canSend && (
          <Button
            onClick={() => void handleSend()}
            disabled={busy || !sendable}
            title={sendable ? undefined : 'Alleen verzonden of betaalde facturen kunnen via Peppol verstuurd worden.'}
          >
            Versturen via Peppol
          </Button>
        )}
      </div>

      {validation &&
        (validation.isValid ? (
          <p className="peppol-validation-ok" role="status">
            Klaar voor verzending
          </p>
        ) : (
          <ul className="peppol-validation-issues" role="alert">
            {validation.issues.map((issue) => (
              <li key={issue.code}>{issue.message}</li>
            ))}
          </ul>
        ))}

      {transmissions !== null && transmissions.length > 0 && (
        <ul className="peppol-history">
          {transmissions.map((transmission) => (
            <li key={transmission.id} className="peppol-history-item">
              <div className="peppol-history-head">
                <strong>v{transmission.payloadVersion}</strong>
                <Badge tone={peppolStatusTone(transmission.status)}>{peppolStatusLabel(transmission.status)}</Badge>
                <span>{PEPPOL_KIND_LABELS[transmission.documentKind] ?? transmission.documentKind}</span>
                <span>{formatDateTime(transmission.createdAt)}</span>
                {transmission.providerMessageId && <code>{transmission.providerMessageId}</code>}
                {transmission.retryCount > 0 && <span>({transmission.retryCount + 1}× aangeboden)</span>}
              </div>
              {transmission.errorDetail && <p className="peppol-preview-meta">{transmission.errorDetail}</p>}
              {transmission.events.length > 0 && (
                <ul className="peppol-history-events">
                  {transmission.events.map((event, index) => (
                    <li key={index}>
                      {formatDateTime(event.timestamp)} — {peppolStatusLabel(event.status)}
                      {event.detail ? `: ${event.detail}` : ''}
                    </li>
                  ))}
                </ul>
              )}
            </li>
          ))}
        </ul>
      )}

      {preview && (
        <Modal title={`Peppol-voorbeeld — ${preview.invoiceNumber}`} onClose={() => setPreview(null)}>
          <div className="peppol-preview-parties">
            <div className="peppol-preview-party">
              <h4>Verzender</h4>
              <p>
                {preview.seller.name}
                {preview.seller.vatNumber && (
                  <>
                    <br />
                    BTW: {preview.seller.vatNumber}
                  </>
                )}
                {preview.seller.participant && (
                  <>
                    <br />
                    Peppol: {preview.seller.participant}
                  </>
                )}
              </p>
            </div>
            <div className="peppol-preview-party">
              <h4>Ontvanger</h4>
              <p>
                {preview.buyer.name}
                {preview.buyer.vatNumber && (
                  <>
                    <br />
                    BTW: {preview.buyer.vatNumber}
                  </>
                )}
                {preview.buyer.participant && (
                  <>
                    <br />
                    Peppol: {preview.buyer.participant}
                  </>
                )}
              </p>
            </div>
          </div>

          <table className="inv-lines-table">
            <thead>
              <tr>
                <th>#</th>
                <th>Omschrijving</th>
                <th>Aantal</th>
                <th>Eenheid</th>
                <th>Prijs</th>
                <th>Btw %</th>
                <th>Bedrag</th>
              </tr>
            </thead>
            <tbody>
              {preview.lines.map((line) => (
                <tr key={line.sequence}>
                  <td>{line.sequence}</td>
                  <td>{line.description}</td>
                  <td>{line.quantity.toLocaleString('nl-BE')}</td>
                  <td>{line.unitCode}</td>
                  <td>{euro(line.unitPrice, preview.currency)}</td>
                  <td>
                    {line.vatCategoryCode} {line.vatRatePercent.toLocaleString('nl-BE')}%
                  </td>
                  <td>{euro(line.lineTotal, preview.currency)}</td>
                </tr>
              ))}
            </tbody>
          </table>

          {preview.vatGroups.length > 0 && (
            <table className="inv-lines-table">
              <thead>
                <tr>
                  <th>Btw-categorie</th>
                  <th>Tarief</th>
                  <th>Grondslag</th>
                  <th>Btw</th>
                </tr>
              </thead>
              <tbody>
                {preview.vatGroups.map((group) => (
                  <tr key={`${group.vatCategoryCode}-${group.vatRatePercent}`}>
                    <td>{group.vatCategoryCode}</td>
                    <td>{group.vatRatePercent.toLocaleString('nl-BE')}%</td>
                    <td>{euro(group.taxableAmount, preview.currency)}</td>
                    <td>{euro(group.vatAmount, preview.currency)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          <p className="peppol-preview-totals">
            Subtotaal: {euro(preview.subtotal, preview.currency)} · Btw: {euro(preview.vatAmount, preview.currency)} ·{' '}
            <strong>Totaal: {euro(preview.total, preview.currency)}</strong>
          </p>
          <p className="peppol-preview-meta">
            Type: {PEPPOL_KIND_LABELS[preview.kind as keyof typeof PEPPOL_KIND_LABELS] ?? preview.kind} · Datum:{' '}
            {preview.invoiceDate}
            {preview.buyerReference && <> · Kopersreferentie: {preview.buyerReference}</>}
            {preview.purchaseOrderNumber && <> · PO-nummer: {preview.purchaseOrderNumber}</>}
            {preview.creditedInvoiceNumber && <> · Crediteert factuur: {preview.creditedInvoiceNumber}</>}
          </p>
        </Modal>
      )}
    </section>
  )
}
