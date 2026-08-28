import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { CustomerSearchPicker } from '../../customers/components/CustomerSearchPicker'
import type { CustomerListItem } from '../../customers/types'
import { changeOrderCustomer, getOrderCustomerChangeImpact, type OrderCustomerChangeImpact } from '../api/transportOrdersApi'
import { CustomerChangeImpactList } from './CustomerChangeImpactList'

interface OrderCustomerChangeDialogProps {
  orderId: string
  orderNumber: string
  currentCustomerId: string
  currentCustomerName: string
  onClose: () => void
  /** Called after a successful change; the caller reloads the order. */
  onChanged: (impact: OrderCustomerChangeImpact) => void
}

/**
 * Sprint 6: move an order to the customer it really belongs to. Search → impact preview from
 * the backend → mandatory reason → apply. A blocked preview (sent invoice, dossier-owned order)
 * disables the action instead of letting the server refuse it afterwards.
 */
export function OrderCustomerChangeDialog({
  orderId, orderNumber, currentCustomerId, currentCustomerName, onClose, onChanged,
}: OrderCustomerChangeDialogProps) {
  const { t } = useLocale()
  const [target, setTarget] = useState<CustomerListItem | null>(null)
  const [impact, setImpact] = useState<OrderCustomerChangeImpact | null>(null)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  function selectTarget(customer: CustomerListItem | null) {
    setTarget(customer)
    setImpact(null)
    setError(null)
  }

  useEffect(() => {
    if (!target) return
    let cancelled = false
    getOrderCustomerChangeImpact(orderId, target.id)
      .then((data) => {
        if (!cancelled) setImpact(data)
      })
      .catch((err) => {
        if (!cancelled) setError(localizeApiError(t, err, t('transportOrders.customerChange.impactFailed')))
      })
    return () => {
      cancelled = true
    }
  }, [orderId, target, t])

  // The preview belongs to the chosen customer; anything else is still loading.
  const currentImpact = target && impact?.newCustomerId === target.id ? impact : null
  const impactLoading = !!target && !currentImpact && !error
  const canApply = !!target && !!currentImpact && !currentImpact.blockedReason && reason.trim().length > 0 && !busy

  async function apply() {
    if (!target) return
    if (!reason.trim()) {
      setError(t('transportOrders.customerChange.reasonRequired'))
      return
    }
    setBusy(true)
    setError(null)
    try {
      const result = await changeOrderCustomer(orderId, target.id, reason.trim())
      onChanged(result)
    } catch (err) {
      setError(localizeApiError(t, err, t('transportOrders.customerChange.failed')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={t('transportOrders.customerChange.title', { number: orderNumber })}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button onClick={() => void apply()} disabled={!canApply}>
            {t('transportOrders.customerChange.confirm')}
          </Button>
        </>
      }
    >
      <dl className="to-summary-list">
        <dt>{t('transportOrders.customerChange.currentCustomer')}</dt>
        <dd>{currentCustomerName}</dd>
      </dl>
      <FormField label={t('transportOrders.customerChange.newCustomer')} htmlFor="order-customer-change-search" required>
        <CustomerSearchPicker
          id="order-customer-change-search"
          value={target}
          onChange={selectTarget}
          currentCustomerId={currentCustomerId}
          disabled={busy}
        />
      </FormField>

      {target && (
        <section className="to-impact" aria-live="polite">
          <h3>{t('transportOrders.customerChange.impactTitle')}</h3>
          {impactLoading && <p className="customer-form-muted">{t('transportOrders.customerChange.impactLoading')}</p>}
          {currentImpact && <CustomerChangeImpactList impact={currentImpact} />}
        </section>
      )}

      <FormField
        label={t('transportOrders.customerChange.reasonLabel')}
        htmlFor="order-customer-change-reason"
        hint={t('transportOrders.customerChange.reasonHint')}
        required
      >
        <textarea
          id="order-customer-change-reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          rows={2}
          maxLength={500}
          disabled={busy}
        />
      </FormField>

      {error && (
        <p className="customer-import-message customer-import-message-error" role="alert">
          {error}
        </p>
      )}
    </Modal>
  )
}
