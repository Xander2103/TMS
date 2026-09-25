import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useLocale } from '../../../i18n/localeContext'
import './dossier-confirm-dialog.css'

interface DossierReasonDialogProps {
  title: string
  intro: string
  reasonLabel: string
  confirmLabel: string
  /** Renders the confirm button as a danger action (cancelling a dossier). */
  destructive?: boolean
  /** Resolves when the action succeeded (the caller closes the dialog); a rejection is shown inline. */
  onSubmit: (reason: string) => Promise<void>
  onClose: () => void
}

/**
 * Confirmation sprint 2026-09-23 — ONE small dialog for every lifecycle action that needs a
 * mandatory reason (reopen, cancel). The reason is required client-side so the server never
 * sees an empty one; a failure is shown inline so the entered text stays.
 */
export function DossierReasonDialog({ title, intro, reasonLabel, confirmLabel, destructive = false, onSubmit, onClose }: DossierReasonDialogProps) {
  const { t } = useLocale()
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit() {
    const trimmed = reason.trim()
    if (!trimmed) {
      setError(t('dossiers.lifecycle.reasonRequired'))
      return
    }
    setBusy(true)
    setError(null)
    try {
      await onSubmit(trimmed)
    } catch (err) {
      setError(err instanceof Error && err.message ? err.message : t('dossiers.detail.actionFailed'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={title}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button variant={destructive ? 'danger' : 'primary'} onClick={() => void submit()} disabled={busy}>
            {busy ? t('ui.actions.busy') : confirmLabel}
          </Button>
        </>
      }
    >
      <p className="dossier-confirm-intro">{intro}</p>
      <FormField label={reasonLabel} htmlFor="dossier-reason" required>
        <textarea
          id="dossier-reason"
          rows={3}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          disabled={busy}
          maxLength={1000}
        />
      </FormField>
      {error && <p className="dossier-confirm-error" role="alert">{error}</p>}
    </Modal>
  )
}
