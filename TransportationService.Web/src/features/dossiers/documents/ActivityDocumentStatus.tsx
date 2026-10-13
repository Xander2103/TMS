import { useLocale } from '../../../i18n/localeContext'
import type { DossierActivity } from '../types'

type IssuedSummary = NonNullable<DossierActivity['issuedDocuments']>[number]

interface ActivityDocumentStatusProps {
  /** Issued transport documents (CMR / leveringsbon / werkbon) of the activity's order. */
  issued: IssuedSummary[]
  /** Uploaded documents of the activity's order, or of the standalone activity itself. */
  documentCount: number
  /**
   * False when no transport document is expected for this activity at all (a standalone activity
   * has no order and therefore no CMR): uploads still count, but nothing is ever "missing".
   * Defaults to true, which is right for every order-backed activity.
   */
  transportDocumentsApply?: boolean
  /** Opens the Documenten tab on the activity's level; without it the status is plain text. */
  onOpen?: () => void
}

/**
 * The "CMR / document" cell of an activity card (spec §16/§20). States what exists: the number
 * of the single issued document, a count for several ("2 vrachtbrieven" / "3 transportdocumenten"),
 * the upload count, or "Nog niet aangemaakt" where a transport document is expected. A standalone
 * activity (no transport document expected) shows its uploads, or a neutral "Geen documenten" —
 * never a missing state. Which document kind applies is the server's decision (document
 * strategy, crane job kind, activity link); this component never guesses from an activity name.
 */
export function ActivityDocumentStatus({ issued, documentCount, transportDocumentsApply = true, onOpen }: ActivityDocumentStatusProps) {
  const { t } = useLocale()

  const parts: string[] = []
  if (transportDocumentsApply) {
    if (issued.length === 1) {
      parts.push(issued[0].documentNumber)
    } else if (issued.length > 1) {
      const kinds = new Set(issued.map((doc) => doc.kind))
      const kind = kinds.size === 1 ? issued[0].kind : 'Mixed'
      parts.push(t(`dossierDocuments.activityStatus.several.${kind}`, { count: issued.length }))
    }
  }
  if (documentCount > 0) parts.push(t('dossierDocuments.activityStatus.uploads', { count: documentCount }))

  if (parts.length === 0) {
    return transportDocumentsApply
      ? <span className="ddoc-activity-status is-missing">{t('dossierDocuments.activityStatus.none')}</span>
      : <span className="ddoc-activity-status is-empty">{t('dossierDocuments.activityStatus.noUploads')}</span>
  }

  const text = parts.join(' · ')
  if (!onOpen) return <span className="ddoc-activity-status">{text}</span>
  return (
    <button type="button" className="link-button ddoc-activity-status" onClick={onOpen}>
      {text}
    </button>
  )
}
