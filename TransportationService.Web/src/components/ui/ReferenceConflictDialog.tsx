import { Button } from './Button'
import { Modal } from './Modal'
import type { ReferenceConflict } from '../../api/conflicts'
import { useLocale } from '../../i18n/localeContext'

interface ReferenceConflictDialogProps {
  title: string
  conflict: ReferenceConflict
  onClose: () => void
  /** The way out the server suggests ("Zet … inactief"), when the host can offer it here. */
  action?: { label: string; onSelect: () => void; busy?: boolean }
}

/**
 * A delete refused with 409 `common.record_referenced`: the server's own (Dutch) explanation plus
 * the ready-made reference labels as bullets ("12 ritten", "3 afleverbewijzen"). Used by the
 * driver, issued-item template and warehouse dock deletes; nothing was deleted.
 */
export function ReferenceConflictDialog({ title, conflict, onClose, action }: ReferenceConflictDialogProps) {
  const { t } = useLocale()
  const busy = action?.busy ?? false
  return (
    <Modal
      title={title}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant={action ? 'secondary' : 'primary'} onClick={onClose} disabled={busy}>
            {t('ui.actions.close')}
          </Button>
          {action && (
            <Button onClick={action.onSelect} disabled={busy}>
              {busy ? t('ui.actions.busy') : action.label}
            </Button>
          )}
        </>
      }
    >
      <p>{conflict.message}</p>
      {conflict.references.length > 0 && (
        <ul>
          {conflict.references.map((reference) => (
            <li key={reference.key}>{reference.label}</li>
          ))}
        </ul>
      )}
    </Modal>
  )
}
