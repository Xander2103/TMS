import { FileText } from 'lucide-react'
import { useEffect, useId, useRef, useState } from 'react'
import { describeApiError } from '../../../api/problemDetails'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import {
  ISSUED_DOCUMENT_KINDS,
  downloadIssuedTransportDocumentPdf,
  issueTransportDocument,
  listIssuedTransportDocuments,
  setIssuedDocumentCustomerVisibility,
  type IssuedTransportDocument,
  type IssuedTransportDocumentKind,
} from '../api/issuedDocumentsApi'
import { getOrderDocumentStrategy } from '../api/transportDocumentsApi'
import { defaultIssueKind } from './issuedDocumentKind'
import './issued-transport-documents.css'

interface IssuedTransportDocumentsBlockProps {
  orderId: string
  /** `craneJobKind` of the order when the host already loaded it; an on-site lifting job gets a work order. */
  craneJobKind?: string | null
  /** Called after a document was issued, so the host can refresh summaries (dossier counts, activity cells). */
  onIssued?: (document: IssuedTransportDocument) => void
}

/** M9: a document is active until the server says it was replaced (older payloads carry no flag). */
const isActive = (doc: IssuedTransportDocument) => doc.isActive !== false

/**
 * "Transportdocumenten" of one order: the issued documents (kind, server-issued number, issued
 * at/by, PDF) and the button that issues a new one. One click intent = ONE `requestId`, reused when
 * that same intent is retried after a failure, so a double click or a retry can never burn a second
 * number. Numbers shown here always come from the server.
 * M9: ONE active document per kind. While one exists, issuing asks for confirmation and a reason and
 * sends the new document as its replacement; the replaced one stays listed as "Vervangen door …".
 */
export function IssuedTransportDocumentsBlock({ orderId, craneJobKind, onIssued }: IssuedTransportDocumentsBlockProps) {
  const { t, formatDateTime } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const fieldId = useId()
  const canView = hasPermission('orders.view') || hasPermission('orders.manage')
  const canIssue = hasPermission('orders.edit') || hasPermission('orders.manage')

  const [documents, setDocuments] = useState<IssuedTransportDocument[] | null>(null)
  const [loadFailed, setLoadFailed] = useState(false)
  const [strategyKind, setStrategyKind] = useState<string | null>(null)
  /** null = follow the default for this order; set once the user picks a kind. */
  const [chosenKind, setChosenKind] = useState<IssuedTransportDocumentKind | null>(null)
  const [externalNumber, setExternalNumber] = useState('')
  const [issuing, setIssuing] = useState(false)
  const [issueError, setIssueError] = useState<string | null>(null)
  /** M9: the active document the user is about to replace (the confirmation dialog is open). */
  const [replacing, setReplacing] = useState<IssuedTransportDocument | null>(null)
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  // A ref, not state: the guard must hold for a second click dispatched before React re-renders.
  const inFlight = useRef(false)
  const intent = useRef<{ key: string; requestId: string } | null>(null)

  useEffect(() => {
    if (!canView) return
    let mounted = true
    listIssuedTransportDocuments(orderId)
      .then((data) => {
        if (mounted) setDocuments(data)
      })
      .catch(() => {
        if (mounted) setLoadFailed(true)
      })
    getOrderDocumentStrategy(orderId)
      .then((strategy) => {
        if (mounted) setStrategyKind(strategy.kind)
      })
      .catch(() => {
        /* the strategy only picks the default kind; issuing keeps working without it */
      })
    return () => {
      mounted = false
    }
  }, [orderId, canView])

  if (!canView) return null

  const kind = chosenKind ?? defaultIssueKind(strategyKind, craneJobKind)
  const activeOfKind = (documents ?? []).find((doc) => doc.kind === kind && isActive(doc)) ?? null

  function handleIssueClick() {
    if (inFlight.current) return
    if (activeOfKind) {
      // M9: never a silent second active document — confirm the replacement and ask why.
      setReplacing(activeOfKind)
      setReason('')
      setReasonError(null)
      return
    }
    void issue(null)
  }

  async function issue(replacement: { document: IssuedTransportDocument; reason: string } | null) {
    if (inFlight.current) return
    const external = externalNumber.trim()
    const key = `${kind}|${external}|${replacement?.document.id ?? ''}|${replacement?.reason ?? ''}`
    // Same kind + same external number (+ same replacement) = the same intent (a retry): keep its requestId.
    if (intent.current?.key !== key) intent.current = { key, requestId: crypto.randomUUID() }
    inFlight.current = true
    setIssuing(true)
    setIssueError(null)
    try {
      const issued = await issueTransportDocument(orderId, {
        kind,
        requestId: intent.current.requestId,
        externalNumber: external || null,
        ...(replacement ? { replacesDocumentId: replacement.document.id, replacementReason: replacement.reason } : {}),
      })
      intent.current = null
      setExternalNumber('')
      setReplacing(null)
      // Idempotent replay returns the record we may already hold — never list it twice; the
      // replaced document stays listed, now as history.
      setDocuments((current) => [
        ...(current ?? [])
          .filter((doc) => doc.id !== issued.id)
          .map((doc) =>
            replacement && doc.id === replacement.document.id
              ? {
                  ...doc,
                  isActive: false,
                  replacedByDocumentId: issued.id,
                  replacedByDocumentNumber: issued.documentNumber,
                  replacementReason: replacement.reason,
                }
              : doc,
          ),
        issued,
      ])
      showSuccess(t('dossierDocuments.issued.success', { number: issued.documentNumber }))
      onIssued?.(issued)
    } catch (err) {
      const message = describeApiError(err, t('dossierDocuments.issued.failed')).message
      if (replacement) setReasonError(t('dossierDocuments.issued.failedRetry', { reason: message }))
      else setIssueError(message)
    } finally {
      inFlight.current = false
      setIssuing(false)
    }
  }

  function confirmReplacement() {
    if (!replacing) return
    const trimmed = reason.trim()
    if (!trimmed) {
      setReasonError(t('dossierDocuments.issued.replace.reasonRequired'))
      return
    }
    void issue({ document: replacing, reason: trimmed })
  }

  async function togglePublication(doc: IssuedTransportDocument) {
    try {
      const updated = await setIssuedDocumentCustomerVisibility(doc.id, !doc.customerVisible)
      setDocuments((current) => (current ?? []).map((d) => (d.id === updated.id ? { ...d, customerVisible: updated.customerVisible } : d)))
      showSuccess(
        updated.customerVisible
          ? t('dossierDocuments.issued.published', { number: updated.documentNumber })
          : t('dossierDocuments.issued.withdrawn', { number: updated.documentNumber }),
      )
    } catch (err) {
      showError(describeApiError(err, t('dossierDocuments.issued.publishFailed')).message)
    }
  }

  function handleDownload(doc: IssuedTransportDocument) {
    downloadIssuedTransportDocumentPdf(doc).catch((err: unknown) =>
      showError(describeApiError(err, t('dossierDocuments.issued.pdfFailed')).message),
    )
  }

  const count = documents?.length ?? 0

  return (
    <section className="itd-block" aria-label={t('dossierDocuments.issued.title')}>
      <header className="itd-header">
        <h3>
          {t('dossierDocuments.issued.title')}
          {documents !== null && <span className="itd-count">{count}</span>}
        </h3>
      </header>

      {loadFailed && <p className="placeholder-text">{t('dossierDocuments.issued.loadFailed')}</p>}
      {!loadFailed && documents === null && <p className="placeholder-text">{t('dossierDocuments.loading')}</p>}
      {documents !== null && count === 0 && <p className="placeholder-text">{t('dossierDocuments.issued.empty')}</p>}
      {documents !== null && count > 0 && (
        <ul className="itd-list">
          {documents.map((doc) => (
            <li key={doc.id} className={isActive(doc) ? 'itd-item' : 'itd-item is-replaced'}>
              <span className="itd-kind">{t(`dossierDocuments.issued.kind.${doc.kind}`)}</span>
              <code className="itd-number">{doc.documentNumber}</code>
              {!isActive(doc) && (
                <span className="itd-replaced">
                  {doc.replacedByDocumentNumber
                    ? t('dossierDocuments.issued.replacedBy', { number: doc.replacedByDocumentNumber })
                    : t('dossierDocuments.issued.replaced')}
                </span>
              )}
              {isActive(doc) && doc.replacesDocumentNumber && (
                <span className="itd-meta">{t('dossierDocuments.issued.replaces', { number: doc.replacesDocumentNumber })}</span>
              )}
              {doc.externalNumber && (
                <span className="itd-meta">{t('dossierDocuments.issued.externalNumberValue', { number: doc.externalNumber })}</span>
              )}
              <span className="itd-meta">
                {doc.issuedByName
                  ? t('dossierDocuments.issued.issuedAtBy', { date: formatDateTime(doc.issuedAt), name: doc.issuedByName })
                  : t('dossierDocuments.issued.issuedAt', { date: formatDateTime(doc.issuedAt) })}
              </span>
              <button
                type="button"
                className="itd-pdf"
                aria-label={t('dossierDocuments.issued.pdfLabel', { number: doc.documentNumber })}
                onClick={() => handleDownload(doc)}
              >
                <FileText size={14} aria-hidden="true" /> PDF
              </button>
              {isActive(doc) && doc.customerVisible && (
                <span className="itd-meta itd-published">{t('dossierDocuments.issued.visibleForCustomer')}</span>
              )}
              {isActive(doc) && canIssue && (
                // KP-06: the customer downloads exactly this frozen document from the portal.
                <button type="button" className="itd-pdf" onClick={() => void togglePublication(doc)}>
                  {doc.customerVisible ? t('dossierDocuments.issued.withdraw') : t('dossierDocuments.issued.publish')}
                </button>
              )}
              {!isActive(doc) && doc.replacementReason && (
                <span className="itd-reason">{t('dossierDocuments.issued.replacementReason', { reason: doc.replacementReason })}</span>
              )}
            </li>
          ))}
        </ul>
      )}

      {canIssue && (
        <div className="itd-issue">
          <label className="itd-field" htmlFor={`${fieldId}-kind`}>
            {t('dossierDocuments.issued.kindLabel')}
            <select
              id={`${fieldId}-kind`}
              value={kind}
              onChange={(event) => setChosenKind(event.target.value as IssuedTransportDocumentKind)}
              disabled={issuing}
            >
              {ISSUED_DOCUMENT_KINDS.map((option) => (
                <option key={option} value={option}>
                  {t(`dossierDocuments.issued.kind.${option}`)}
                </option>
              ))}
            </select>
          </label>
          <label className="itd-field" htmlFor={`${fieldId}-external`}>
            {t('dossierDocuments.issued.externalNumber')}
            <input
              id={`${fieldId}-external`}
              type="text"
              maxLength={50}
              value={externalNumber}
              onChange={(event) => setExternalNumber(event.target.value)}
              disabled={issuing}
            />
          </label>
          <Button onClick={handleIssueClick} disabled={issuing}>
            {issuing && !replacing ? t('ui.actions.busy') : t(`dossierDocuments.issued.create.${kind}`)}
          </Button>
        </div>
      )}
      {issueError && (
        <p className="itd-alert" role="alert">
          {t('dossierDocuments.issued.failedRetry', { reason: issueError })}
        </p>
      )}

      {replacing && (
        <Modal
          title={t(`dossierDocuments.issued.replace.title.${replacing.kind}`)}
          onClose={() => setReplacing(null)}
          busy={issuing}
          footer={
            <>
              <Button variant="secondary" onClick={() => setReplacing(null)} disabled={issuing}>
                {t('ui.actions.cancel')}
              </Button>
              <Button onClick={confirmReplacement} disabled={issuing}>
                {issuing ? t('ui.actions.busy') : t('dossierDocuments.issued.replace.confirm')}
              </Button>
            </>
          }
        >
          <p>{t(`dossierDocuments.issued.replace.message.${replacing.kind}`, { number: replacing.documentNumber })}</p>
          <p className="itd-meta">{t('dossierDocuments.issued.replace.keepsHistory', { number: replacing.documentNumber })}</p>
          <FormField label={t('dossierDocuments.issued.replace.reasonLabel')} htmlFor={`${fieldId}-reason`} required>
            <textarea
              id={`${fieldId}-reason`}
              rows={3}
              value={reason}
              maxLength={500}
              onChange={(event) => setReason(event.target.value)}
              disabled={issuing}
            />
          </FormField>
          {reasonError && (
            <p className="itd-alert" role="alert">
              {reasonError}
            </p>
          )}
        </Modal>
      )}
    </section>
  )
}
