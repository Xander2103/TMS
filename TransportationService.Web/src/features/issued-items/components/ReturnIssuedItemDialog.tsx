import { useState, type FormEvent } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { describeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { todayLocalIsoDate } from '../../../utils/dates'
import { RETURN_DISPOSITION_LABELS, type ReturnDisposition, type ReturnIssuedItemInput } from '../issuedItemsApi'

interface ReturnIssuedItemDialogProps {
  /** Item name shown in the intro ("Markeer … als teruggebracht"). */
  itemName: string
  /** Extra identification under the intro (serial, employee) — the warehouse desk shows it. */
  details?: string | null
  /** Only a stock-tracked catalogue item can put goods back into stock. */
  stockTracked: boolean
  onSubmit: (input: ReturnIssuedItemInput) => Promise<void>
  onClose: () => void
}

/**
 * "Teruggebracht" / "Retour verwerken": closes an open issuance (Issued → Returned) with a date,
 * the condition and the disposition. Shared by the HR employee tab and the warehouse desk so both
 * book the return identically. The return date defaults to the TENANT's today (WH-M7).
 */
export function ReturnIssuedItemDialog({ itemName, details, stockTracked, onSubmit, onClose }: ReturnIssuedItemDialogProps) {
  const { t } = useLocale()
  const [returnedDate, setReturnedDate] = useState(() => todayLocalIsoDate())
  const [returnCondition, setReturnCondition] = useState('')
  const [disposition, setDisposition] = useState<ReturnDisposition>('good')
  const [restoreStock, setRestoreStock] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const showRestoreStock = disposition === 'good' && stockTracked

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)
    try {
      await onSubmit({
        returnedDate: returnedDate || null,
        returnCondition: returnCondition.trim() || null,
        returnDisposition: disposition,
        restoreStock: showRestoreStock ? restoreStock : false,
      })
    } catch (err) {
      setError(describeApiError(err, t('issuedItems.return.failed')).message)
      setBusy(false)
    }
  }

  return (
    <Modal
      title={t('issuedItems.return.title')}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" form="issued-item-return-form" disabled={busy}>
            {busy ? t('issuedItems.return.saving') : t('issuedItems.return.confirm')}
          </Button>
        </>
      }
    >
      <form id="issued-item-return-form" className="issued-items-form" onSubmit={handleSubmit} noValidate>
        {error && (
          <div className="issued-items-form-error" role="alert">
            {error}
          </div>
        )}
        <p className="customer-form-muted">{t('issuedItems.return.intro', { name: itemName })}</p>
        {details && <p className="customer-form-muted">{details}</p>}
        <div className="issued-items-form-row">
          <FormField label={t('issuedItems.return.returnedDate')} htmlFor="ii-return-date">
            <input
              id="ii-return-date"
              type="date"
              value={returnedDate}
              onChange={(e) => setReturnedDate(e.target.value)}
              disabled={busy}
            />
          </FormField>
          <FormField label={t('issuedItems.return.condition')} htmlFor="ii-return-condition">
            <input
              id="ii-return-condition"
              value={returnCondition}
              onChange={(e) => setReturnCondition(e.target.value)}
              disabled={busy}
              maxLength={150}
            />
          </FormField>
        </div>
        <div className="issued-items-form-row">
          <FormField label={t('issuedItems.return.disposition')} htmlFor="ii-return-disposition" hint={t('issuedItems.tab.dispositionHint')}>
            <select
              id="ii-return-disposition"
              value={disposition}
              onChange={(e) => setDisposition(e.target.value as ReturnDisposition)}
              disabled={busy}
            >
              {(Object.keys(RETURN_DISPOSITION_LABELS) as ReturnDisposition[]).map((value) => (
                <option key={value} value={value}>
                  {t(RETURN_DISPOSITION_LABELS[value])}
                </option>
              ))}
            </select>
          </FormField>
          {showRestoreStock && (
            <label className="issued-items-checkbox">
              <input
                type="checkbox"
                checked={restoreStock}
                onChange={(e) => setRestoreStock(e.target.checked)}
                disabled={busy}
              />
              <span>{t('issuedItems.return.restoreStock')}</span>
            </label>
          )}
        </div>
      </form>
    </Modal>
  )
}
