import { Modal } from './Modal'
import { Button } from './Button'
import { useLocale } from '../../i18n/localeContext'

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
  confirmLabel,
  cancelLabel,
  destructive = false,
  busy = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const { t } = useLocale()
  return (
    <Modal
      title={title}
      onClose={onCancel}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={busy}>
            {cancelLabel ?? t('ui.actions.cancel')}
          </Button>
          <Button variant={destructive ? 'danger' : 'primary'} onClick={onConfirm} disabled={busy}>
            {busy ? t('ui.actions.busy') : (confirmLabel ?? t('ui.actions.confirm'))}
          </Button>
        </>
      }
    >
      <p>{message}</p>
    </Modal>
  )
}
