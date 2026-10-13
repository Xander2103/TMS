import { FileText } from 'lucide-react'
import { useEffect, useId, useRef, useState } from 'react'
import { describeApiError } from '../../../api/problemDetails'
import { Button } from '../../../components/ui/Button'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import {
  ISSUED_DOCUMENT_KINDS,
  downloadIssuedTransportDocumentPdf,
  issueTransportDocument,
  listIssuedTransportDocuments,
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
}

/**
 * "Transportdocumenten" of one order: the issued documents (kind, server-issued number, issued
 * at/by, PDF) and the button that issues a new one. One click intent = ONE `requestId`, reused when
 * that same intent is retried after a failure, so a double click or a retry can never burn a second
 * number. Numbers shown here always come from the server.
 */
export function IssuedTransportDocumentsBlock({ orderId, craneJobKind }: IssuedTransportDocumentsBlockProps) {
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

  async function handleIssue() {
    if (inFlight.current) return
    const external = externalNumber.trim()
    const key = `${kind}|${external}`
    // Same kind + same external number = the same intent (a retry): keep its requestId.
    if (intent.current?.key !== key) intent.current = { key, requestId: crypto.randomUUID() }
    inFlight.current = true
    setIssuing(true)
    setIssueError(null)
    try {
      const issued = await issueTransportDocument(orderId, {
        kind,
        requestId: intent.current.requestId,
        externalNumber: external || null,
      })
      intent.current = null
      setExternalNumber('')
      // Idempotent replay returns the record we may already hold — never list it twice.
      setDocuments((current) => [...(current ?? []).filter((doc) => doc.id !== issued.id), issued])
      showSuccess(t('dossierDocuments.issued.success', { number: issued.documentNumber }))
    } catch (err) {
      setIssueError(describeApiError(err, t('dossierDocuments.issued.failed')).message)
    } finally {
      inFlight.current = false
      setIssuing(false)
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
            <li key={doc.id} className="itd-item">
              <span className="itd-kind">{t(`dossierDocuments.issued.kind.${doc.kind}`)}</span>
              <code className="itd-number">{doc.documentNumber}</code>
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
          <Button onClick={() => void handleIssue()} disabled={issuing}>
            {issuing ? t('ui.actions.busy') : t(`dossierDocuments.issued.create.${kind}`)}
          </Button>
        </div>
      )}
      {issueError && (
        <p className="itd-alert" role="alert">
          {t('dossierDocuments.issued.failedRetry', { reason: issueError })}
        </p>
      )}
    </section>
  )
}
