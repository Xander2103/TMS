import { useCallback, useEffect, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import {
  downloadInvoicePeppolXml,
  getInvoicePeppolPreview,
  listInvoicePeppolTransmissions,
  peppolStatusLabel,
  peppolStatusTone,
  sendInvoicePeppol,
  validateInvoicePeppol,
  PEPPOL_KIND_LABEL_KEYS,
  type PeppolDocumentKind,
  type PeppolInvoicePreview,
  type PeppolInvoiceValidationResult,
  type PeppolTransmission,
} from '../../peppol/api/peppolApi'
import { euro, type InvoiceStatus } from '../types'
import { formatDate, formatDateTime } from '../../../utils/dates'
import { formatQuantity } from '../../../utils/numbers'
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
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()

  const canValidate = hasPermission('peppol.validate')
  const canView = hasPermission('peppol.view')
  const canSend = hasPermission('peppol.send')

  const [transmissions, setTransmissions] = useState<PeppolTransmission[] | null>(null)
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
  const [validation, setValidation] = useState<PeppolInvoiceValidationResult | null>(null)
  const [preview, setPreview] = useState<PeppolInvoicePreview | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    listInvoicePeppolTransmissions(invoiceId)
      .then((rows) => {
        setTransmissions([...rows].sort((a, b) => b.createdAt.localeCompare(a.createdAt)))
        setLoadErrorKey(null)
      })
      .catch(() => setLoadErrorKey('invoices.peppolPanel.loadError'))
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
      showError(localizeApiError(t, err, t('invoices.peppolPanel.validateError')))
    } finally {
      setBusy(false)
    }
  }

  async function handlePreview() {
    setBusy(true)
    try {
      setPreview(await getInvoicePeppolPreview(invoiceId))
    } catch (err) {
      showError(localizeApiError(t, err, t('invoices.peppolPanel.previewError')))
    } finally {
      setBusy(false)
    }
  }

  async function handleDownloadXml() {
    setBusy(true)
    try {
      await downloadInvoicePeppolXml(invoiceId, invoiceNumber)
    } catch (err) {
      showError(localizeApiError(t, err, t('invoices.peppolPanel.xmlError')))
    } finally {
      setBusy(false)
    }
  }

  async function handleSend() {
    setBusy(true)
    try {
      await sendInvoicePeppol(invoiceId)
      showSuccess(t('invoices.peppolPanel.sent'))
      setValidation(null)
      reload()
    } catch (err) {
      showError(localizeApiError(t, err, t('invoices.peppolPanel.sendError')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="inv-section">
      <h3>{t('invoices.peppolPanel.title')}</h3>

      {loadErrorKey && (
        <p className="placeholder-text" role="alert">
          {t(loadErrorKey)}
        </p>
      )}

      <div className="peppol-panel-status">
        {latest ? (
          <>
            <Badge tone={peppolStatusTone(latest.status)}>{peppolStatusLabel(t, latest.status)}</Badge>
            {latest.providerMessageId && (
              <span>
                {t('invoices.peppolPanel.providerRef')} <code>{latest.providerMessageId}</code>
              </span>
            )}
            {latest.errorDetail && <span className="edi-error" title={latest.errorDetail}>{latest.errorDetail}</span>}
          </>
        ) : (
          transmissions !== null && <span className="peppol-preview-meta">{t('invoices.peppolPanel.notSent')}</span>
        )}
      </div>

      <div className="peppol-panel-actions">
        {canValidate && (
          <Button variant="secondary" onClick={() => void handleValidate()} disabled={busy}>
            {t('invoices.peppolPanel.validate')}
          </Button>
        )}
        {(canValidate || canView) && (
          <Button variant="secondary" onClick={() => void handlePreview()} disabled={busy}>
            {t('invoices.peppolPanel.preview')}
          </Button>
        )}
        {canValidate && (
          <Button variant="secondary" onClick={() => void handleDownloadXml()} disabled={busy}>
            {t('invoices.peppolPanel.downloadXml')}
          </Button>
        )}
        {canSend && (
          <Button
            onClick={() => void handleSend()}
            disabled={busy || !sendable}
            title={sendable ? undefined : t('invoices.peppolPanel.sendableHint')}
          >
            {t('invoices.peppolPanel.send')}
          </Button>
        )}
      </div>

      {validation &&
        (validation.isValid ? (
          <p className="peppol-validation-ok" role="status">
            {t('invoices.peppolPanel.readyToSend')}
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
                <Badge tone={peppolStatusTone(transmission.status)}>{peppolStatusLabel(t, transmission.status)}</Badge>
                <span>
                  {PEPPOL_KIND_LABEL_KEYS[transmission.documentKind as PeppolDocumentKind]
                    ? t(PEPPOL_KIND_LABEL_KEYS[transmission.documentKind as PeppolDocumentKind])
                    : transmission.documentKind}
                </span>
                <span>{formatDateTime(transmission.createdAt)}</span>
                {transmission.providerMessageId && <code>{transmission.providerMessageId}</code>}
                {transmission.retryCount > 0 && (
                  <span>{t('invoices.peppolPanel.offeredTimes', { times: transmission.retryCount + 1 })}</span>
                )}
              </div>
              {transmission.errorDetail && <p className="peppol-preview-meta">{transmission.errorDetail}</p>}
              {transmission.events.length > 0 && (
                <ul className="peppol-history-events">
                  {transmission.events.map((event, index) => (
                    <li key={index}>
                      {formatDateTime(event.timestamp)} — {peppolStatusLabel(t, event.status)}
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
        <Modal title={t('invoices.peppolPanel.previewTitle', { number: preview.invoiceNumber })} onClose={() => setPreview(null)}>
          <div className="peppol-preview-parties">
            <div className="peppol-preview-party">
              <h4>{t('invoices.peppolPanel.seller')}</h4>
              <p>
                {preview.seller.name}
                {preview.seller.vatNumber && (
                  <>
                    <br />
                    {t('invoices.peppolPanel.vat', { number: preview.seller.vatNumber })}
                  </>
                )}
                {preview.seller.participant && (
                  <>
                    <br />
                    {t('invoices.peppolPanel.participant', { id: preview.seller.participant })}
                  </>
                )}
              </p>
            </div>
            <div className="peppol-preview-party">
              <h4>{t('invoices.peppolPanel.buyer')}</h4>
              <p>
                {preview.buyer.name}
                {preview.buyer.vatNumber && (
                  <>
                    <br />
                    {t('invoices.peppolPanel.vat', { number: preview.buyer.vatNumber })}
                  </>
                )}
                {preview.buyer.participant && (
                  <>
                    <br />
                    {t('invoices.peppolPanel.participant', { id: preview.buyer.participant })}
                  </>
                )}
              </p>
            </div>
          </div>

          <table className="inv-lines-table">
            <thead>
              <tr>
                <th>#</th>
                <th>{t('invoices.internalLines.columns.description')}</th>
                <th>{t('invoices.internalLines.columns.quantity')}</th>
                <th>{t('invoices.peppolPanel.unitColumn')}</th>
                <th>{t('invoices.internalLines.columns.price')}</th>
                <th>{t('invoices.internalLines.columns.vat')}</th>
                <th>{t('invoices.internalLines.columns.amount')}</th>
              </tr>
            </thead>
            <tbody>
              {preview.lines.map((line) => (
                <tr key={line.sequence}>
                  <td>{line.sequence}</td>
                  <td>{line.description}</td>
                  <td>{formatQuantity(line.quantity)}</td>
                  <td>{line.unitCode}</td>
                  <td>{euro(line.unitPrice, preview.currency)}</td>
                  <td>
                    {line.vatCategoryCode} {formatQuantity(line.vatRatePercent)}%
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
                  <th>{t('invoices.peppolPanel.vatColumns.category')}</th>
                  <th>{t('invoices.peppolPanel.vatColumns.rate')}</th>
                  <th>{t('invoices.peppolPanel.vatColumns.base')}</th>
                  <th>{t('invoices.peppolPanel.vatColumns.vat')}</th>
                </tr>
              </thead>
              <tbody>
                {preview.vatGroups.map((group) => (
                  <tr key={`${group.vatCategoryCode}-${group.vatRatePercent}`}>
                    <td>{group.vatCategoryCode}</td>
                    <td>{formatQuantity(group.vatRatePercent)}%</td>
                    <td>{euro(group.taxableAmount, preview.currency)}</td>
                    <td>{euro(group.vatAmount, preview.currency)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          <p className="peppol-preview-totals">
            {t('invoices.detail.subtotal')}: {euro(preview.subtotal, preview.currency)} · {t('invoices.internalDetail.vat')}: {euro(preview.vatAmount, preview.currency)} ·{' '}
            <strong>{t('invoices.detail.total')}: {euro(preview.total, preview.currency)}</strong>
          </p>
          <p className="peppol-preview-meta">
            {t('invoices.peppolPanel.type')}: {PEPPOL_KIND_LABEL_KEYS[preview.kind as PeppolDocumentKind]
              ? t(PEPPOL_KIND_LABEL_KEYS[preview.kind as PeppolDocumentKind])
              : preview.kind} · {t('invoices.peppolPanel.date')}:{' '}
            {formatDate(preview.invoiceDate)}
            {preview.buyerReference && <> · {t('invoices.peppolPanel.buyerReference')}: {preview.buyerReference}</>}
            {preview.purchaseOrderNumber && <> · {t('invoices.detail.poNumber', { number: preview.purchaseOrderNumber })}</>}
            {preview.creditedInvoiceNumber && <> · {t('invoices.peppolPanel.creditsInvoice')}: {preview.creditedInvoiceNumber}</>}
          </p>
        </Modal>
      )}
    </section>
  )
}
