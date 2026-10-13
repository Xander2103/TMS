import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useLocale } from '../../../i18n/localeContext'
import type { NegativeStockPayload } from '../inventoryApi'
import './NegativeStockConfirmModal.css'

interface NegativeStockConfirmModalProps {
  /** 409-payload van de server (negative_stock_confirmation_required). */
  payload: NegativeStockPayload
  /** Bepaalt de waarschuwingstekst en of een redenveld wordt getoond (correcties dragen hun reden al mee). */
  kind: 'issue' | 'correction'
  /** Naam van de medewerker aan wie wordt uitgereikt, indien van toepassing. */
  employeeName?: string | null
  /** Opslaglocatie van het artikel, indien bekend. */
  storageLocation?: string | null
  /** Zonder bevestigingsrecht toont de modal enkel een melding, geen bevestigknop. */
  canConfirm: boolean
  busy?: boolean
  onConfirm: (reason: string) => void
  onCancel: () => void
}

/**
 * Domme bevestigingsmodal voor voorraadverlagende mutaties die onder nul gaan.
 * Toont de servercijfers uit de 409 en stuurt enkel de (optionele) reden terug.
 */
export function NegativeStockConfirmModal({
  payload,
  kind,
  employeeName,
  storageLocation,
  canConfirm,
  busy = false,
  onConfirm,
  onCancel,
}: NegativeStockConfirmModalProps) {
  const { t } = useLocale()
  const [reason, setReason] = useState('')

  const needsReason = kind === 'issue' && payload.requiresReason
  const confirmDisabled = busy || (needsReason && reason.trim() === '')

  return (
    <Modal
      title={t('issuedItems.negative.title')}
      onClose={onCancel}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          {canConfirm && (
            <Button variant="danger" onClick={() => onConfirm(reason.trim())} disabled={confirmDisabled}>
              {t('issuedItems.negative.confirm')}
            </Button>
          )}
        </>
      }
    >
      <div className="negative-stock-body">
        <p className="negative-stock-warning" role="alert">
          <span aria-hidden="true">⚠</span>{' '}
          {kind === 'issue' ? t('issuedItems.negative.warningIssue') : t('issuedItems.negative.warningCorrection')}
        </p>
        {payload.versionMismatch && (
          <p className="negative-stock-mismatch" role="alert">
            {t('issuedItems.negative.mismatch')}
          </p>
        )}
        <dl className="negative-stock-summary">
          <div>
            <dt>{t('issuedItems.negative.item')}</dt>
            <dd>
              {payload.itemName}
              {payload.variantLabel ? ` — ${payload.variantLabel}` : ''}
            </dd>
          </div>
          {employeeName && (
            <div>
              <dt>{t('issuedItems.negative.employee')}</dt>
              <dd>{employeeName}</dd>
            </div>
          )}
          {storageLocation && (
            <div>
              <dt>{t('issuedItems.negative.location')}</dt>
              <dd>{storageLocation}</dd>
            </div>
          )}
          <div>
            <dt>{t('issuedItems.negative.currentStock')}</dt>
            <dd>{payload.currentStock}</dd>
          </div>
          <div>
            <dt>{t('issuedItems.negative.requested')}</dt>
            <dd>{Math.abs(payload.requestedDelta)}</dd>
          </div>
          <div>
            <dt>{t('issuedItems.negative.newStock')}</dt>
            <dd>
              <span className="negative-stock-projected">
                <span aria-hidden="true">⚠</span> {t('issuedItems.negative.projected', { stock: payload.projectedStock })}
              </span>
            </dd>
          </div>
        </dl>
        {!canConfirm && (
          <p className="negative-stock-no-permission" role="alert">
            {t('issuedItems.negative.noPermission')}
          </p>
        )}
        {canConfirm && kind === 'issue' && (
          <FormField
            label={t('issuedItems.negative.reason')}
            htmlFor="negative-stock-reason"
            required={needsReason}
            hint={needsReason ? t('issuedItems.negative.reasonHint') : undefined}
          >
            <textarea
              id="negative-stock-reason"
              rows={2}
              value={reason}
              maxLength={300}
              onChange={(e) => setReason(e.target.value)}
              disabled={busy}
            />
          </FormField>
        )}
      </div>
    </Modal>
  )
}
