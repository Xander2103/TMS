import { Modal } from './Modal'
import { Button } from './Button'

interface ConfirmDialogProps {
  title: string
  message: string
  confirmLabel?: string
  cancelLabel?: string
  destructive?: boolean
  busy?: boolean
  onConfirm: () => void
  onCancel: () => void
}

/** Standard yes/no confirmation used for destructive or irreversible actions. */
export function ConfirmDialog({
  title,
  message,
  confirmLabel = 'Bevestigen',
  cancelLabel = 'Annuleren',
  destructive = false,
  busy = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  return (
    <Modal
      title={title}
      onClose={onCancel}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={busy}>
            {cancelLabel}
          </Button>
          <Button variant={destructive ? 'danger' : 'primary'} onClick={onConfirm} disabled={busy}>
            {busy ? 'Bezig...' : confirmLabel}
          </Button>
        </>
      }
    >
      <p>{message}</p>
    </Modal>
  )
}
