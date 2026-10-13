import { useLocale } from '../../../i18n/localeContext'
import './dossier-notes.css'

interface NotePreviewProps {
  /** Latest note text (the server already cuts it to 160 characters); null when there is none. */
  preview: string | null
  /** Number of notes on the activity. */
  count: number
  /** Opens the surface that shows all notes; without it the preview is plain text. */
  onOpen?: () => void
}

/**
 * One-line note teaser for an activity card: the latest note plus the total. Renders nothing
 * when the activity has no notes, so a card without notes keeps its height.
 */
export function NotePreview({ preview, count, onOpen }: NotePreviewProps) {
  const { t } = useLocale()
  if (count <= 0) return null

  const countLabel = t('dossierNotes.preview.count', { count })
  const content = (
    <>
      <span className="dossier-note-preview-count">{countLabel}</span>
      {preview && <span className="dossier-note-preview-text">{preview}</span>}
    </>
  )

  if (!onOpen) return <p className="dossier-note-preview">{content}</p>
  return (
    <button type="button" className="dossier-note-preview dossier-note-preview-button" onClick={onOpen} title={t('dossierNotes.preview.open')}>
      {content}
    </button>
  )
}
