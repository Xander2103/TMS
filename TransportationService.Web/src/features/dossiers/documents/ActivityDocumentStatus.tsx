import { useLocale } from '../../../i18n/localeContext'
import type { DossierActivity } from '../types'

type IssuedSummary = NonNullable<DossierActivity['issuedDocuments']>[number]

interface ActivityDocumentStatusProps {
  /** Issued transport documents (CMR / leveringsbon / werkbon) of the activity's order. */
  issued: IssuedSummary[]
  /** Uploaded documents of that order. */
  documentCount: number
  /**
   * False when no transport document applies to this activity at all (a standalone activity has
   * no order and therefore no CMR): shown as "Niet van toepassing", never as missing. Defaults
   * to true, which is right for every order-backed activity.
   */
  applicable?: boolean
  /** Opens the Documenten tab on the activity's order; without it the status is plain text. */
  onOpen?: () => void
}

/**
 * The "CMR / document" cell of an activity card (spec §16/§20). States what exists: the number
 * of the single issued document, a count for several ("2 vrachtbrieven" / "3 transportdocumenten"),
 * the upload count, or "Nog niet aangemaakt" — and "Niet van toepassing" where no transport
 * document is expected. Which document kind applies is the server's decision (document
 * strategy, crane job kind); this component never guesses from an activity name.
 */
export function ActivityDocumentStatus({ issued, documentCount, applicable = true, onOpen }: ActivityDocumentStatusProps) {
  const { t } = useLocale()

  if (!applicable) {
    return <span className="ddoc-activity-status is-not-applicable">{t('dossierDocuments.activityStatus.notApplicable')}</span>
  }

  const parts: string[] = []
  if (issued.length === 1) {
    parts.push(issued[0].documentNumber)
  } else if (issued.length > 1) {
    const kinds = new Set(issued.map((doc) => doc.kind))
    const kind = kinds.size === 1 ? issued[0].kind : 'Mixed'
    parts.push(t(`dossierDocuments.activityStatus.several.${kind}`, { count: issued.length }))
  }
  if (documentCount > 0) parts.push(t('dossierDocuments.activityStatus.uploads', { count: documentCount }))

  if (parts.length === 0) {
    return <span className="ddoc-activity-status is-missing">{t('dossierDocuments.activityStatus.none')}</span>
  }

  const text = parts.join(' · ')
  if (!onOpen) return <span className="ddoc-activity-status">{text}</span>
  return (
    <button type="button" className="link-button ddoc-activity-status" onClick={onOpen}>
      {text}
    </button>
  )
}
