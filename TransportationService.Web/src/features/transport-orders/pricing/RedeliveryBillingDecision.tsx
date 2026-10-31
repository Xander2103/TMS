import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useLocale } from '../../../i18n/localeContext'
import { READINESS_REASON_KEYS } from '../../invoices/utils/readiness'
import type { TransportOrderDetail } from '../types'

interface RedeliveryBillingDecisionProps {
  order: TransportOrderDetail
  /** orders.override_price | orders.manage, the right behind every post-execution price change. */
  canDecide: boolean
  busy: boolean
  onConfirm: (reason: string) => Promise<boolean>
}

/**
 * R-N1/R-N2: on a completed order of a failed-delivery pair the server says why it cannot be billed
 * and whether "gratis" is still legal. This shows that verdict and offers only what the server allows.
 */
export function RedeliveryBillingDecision({ order, canDecide, busy, onConfirm }: RedeliveryBillingDecisionProps) {
  const { t } = useLocale()
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState('')
  const holdKey = order.billingHoldReason ? READINESS_REASON_KEYS[order.billingHoldReason] : undefined
  const canConfirmFree = Boolean(order.postExecutionFreeAllowed) && canDecide
  if (!holdKey && !canConfirmFree) return null

  async function submit() {
    if (await onConfirm(reason.trim())) {
      setOpen(false)
      setReason('')
    }
  }

  return (
    <div className="order-coverage-attention" role="note">
      {holdKey && <p className="order-billing-hold-reason">{t(holdKey, { number: order.billingHoldOrderNumber ?? '' })}</p>}
      {canConfirmFree ? (
        <>
          <p>{t('dossierPricing.postExecution.freeExplain')}</p>
          <Button variant="secondary" onClick={() => setOpen(true)} disabled={busy}>
            {t('dossierPricing.method.freeConfirm')}
          </Button>
        </>
      ) : (
        <p>{t('dossierPricing.postExecution.needsRight')}</p>
      )}
      {open && (
        <Modal
          title={t('dossierPricing.order.zeroConfirmTitle')}
          onClose={() => setOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setOpen(false)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button onClick={() => void submit()} disabled={busy || reason.trim() === ''}>
                {t('dossierPricing.method.freeConfirm')}
              </Button>
            </>
          }
        >
          <p>{t('dossierPricing.postExecution.freeConfirmMessage', { number: order.orderNumber })}</p>
          <FormField label={t('dossierPricing.postExecution.freeReason')} htmlFor="order-free-reason" required>
            <textarea id="order-free-reason" rows={2} value={reason} onChange={(event) => setReason(event.target.value)} disabled={busy} />
          </FormField>
        </Modal>
      )}
    </div>
  )
}
