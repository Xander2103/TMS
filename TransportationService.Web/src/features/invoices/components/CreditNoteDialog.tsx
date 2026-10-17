import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useLocale } from '../../../i18n/localeContext'

interface CreditNoteDialogProps {
  invoiceNumber: string
  /** The gross amount that will be credited, already formatted. */
  totalLabel: string
  busy: boolean
  onConfirm: (reason: string) => void
  onCancel: () => void
}

/**
 * ACC-M11: crediting an issued invoice is a deliberate accounting act — it needs an explicit
 * confirmation and a reason (max 500), recorded in the history of both documents.
 */
export function CreditNoteDialog({ invoiceNumber, totalLabel, busy, onConfirm, onCancel }: CreditNoteDialogProps) {
  const { t } = useLocale()
  const [reason, setReason] = useState('')
  const trimmed = reason.trim()
  return (
    <Modal
      title={t('invoices.detail.creditDialog.title')}
      onClose={() => !busy && onCancel()}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button onClick={() => onConfirm(trimmed)} disabled={busy || trimmed.length === 0} data-testid="credit-note-confirm">
            {busy ? t('invoices.common.busy') : t('invoices.detail.creditDialog.confirm')}
          </Button>
        </>
      }
    >
      <p>{t('invoices.detail.creditDialog.intro', { number: invoiceNumber, total: totalLabel })}</p>
      <div className="inv-override-form">
        <FormField label={t('invoices.detail.creditDialog.reasonLabel')} htmlFor="inv-credit-reason" required hint={t('invoices.detail.creditDialog.reasonHint')}>
          <textarea
            id="inv-credit-reason"
            rows={3}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            disabled={busy}
            maxLength={500}
          />
        </FormField>
      </div>
    </Modal>
  )
}
