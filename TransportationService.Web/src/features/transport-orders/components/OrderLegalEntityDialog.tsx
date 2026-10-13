import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { getCustomer } from '../../customers/api/customersApi'
import { getLegalEntityOptions } from '../../legal-entities/api/legalEntitiesApi'
import type { LegalEntityOption } from '../../legal-entities/types'
import {
  changeOrderLegalEntity,
  getOrderLegalEntityChangeImpact,
  type OrderLegalEntityChangeImpact,
} from '../api/transportOrdersApi'
import type { TransportOrderDetail } from '../types'

interface OrderLegalEntityDialogProps {
  order: TransportOrderDetail
  onClose: () => void
  onChanged: (order: TransportOrderDetail) => void
}

/**
 * Sprint 6: the invoicing entity of ONE order. The list only holds the entities the customer
 * allows; the customer default is marked. Picking another allowed entity is the privileged
 * override path: it needs dossiers.override_entity AND a reason, and is audited. The backend
 * preview tells what happens to concept invoices and whether the change is blocked.
 */
export function OrderLegalEntityDialog({ order, onClose, onChanged }: OrderLegalEntityDialogProps) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const mayOverride = hasPermission('dossiers.override_entity')
  const [entities, setEntities] = useState<LegalEntityOption[]>([])
  const [allowedIds, setAllowedIds] = useState<string[] | null>(null)
  const [customerDefaultId, setCustomerDefaultId] = useState<string | null>(null)
  const [entityId, setEntityId] = useState(order.legalEntityId ?? '')
  const [reason, setReason] = useState('')
  const [impact, setImpact] = useState<OrderLegalEntityChangeImpact | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    Promise.all([getLegalEntityOptions(), getCustomer(order.customerId)])
      .then(([options, customer]) => {
        if (cancelled) return
        setEntities(options.filter((e) => e.isActive))
        setAllowedIds(customer.allowedLegalEntityIds ?? [])
        setCustomerDefaultId(customer.defaultLegalEntityId)
      })
      .catch((err) => {
        if (!cancelled) setError(localizeApiError(t, err, t('transportOrders.entityChange.impactFailed')))
      })
    return () => {
      cancelled = true
    }
  }, [order.customerId, t])

  function selectEntity(value: string) {
    setEntityId(value)
    setImpact(null)
    setError(null)
  }

  useEffect(() => {
    if (!entityId || entityId === order.legalEntityId) return
    let cancelled = false
    getOrderLegalEntityChangeImpact(order.id, entityId)
      .then((data) => {
        if (!cancelled) setImpact(data)
      })
      .catch((err) => {
        if (!cancelled) setError(localizeApiError(t, err, t('transportOrders.entityChange.impactFailed')))
      })
    return () => {
      cancelled = true
    }
  }, [order.id, order.legalEntityId, entityId, t])

  // An empty allowed set means "no restriction configured" (backend semantics).
  // The order's CURRENT entity always stays selectable, even when it sits outside the
  // customer's allowed set (a pre-existing state), so the dialog never hides where the order is.
  const visibleEntities =
    allowedIds && allowedIds.length > 0
      ? entities.filter((e) => allowedIds.includes(e.id) || e.id === order.legalEntityId)
      : entities
  // Only a preview for the CURRENT selection counts.
  const currentImpact = impact && impact.targetLegalEntityId === entityId ? impact : null
  const deviates = currentImpact?.deviatesFromCustomerDefault ?? (entityId !== '' && entityId !== customerDefaultId)
  const blocked = currentImpact?.blockedReason ?? null
  const lacksRight = deviates && !mayOverride
  const canApply =
    entityId !== '' && entityId !== order.legalEntityId && !busy && !blocked && !lacksRight && (!deviates || reason.trim().length > 0)

  async function apply() {
    if (deviates && !reason.trim()) {
      setError(t('transportOrders.entityChange.reasonRequired'))
      return
    }
    setBusy(true)
    setError(null)
    try {
      const updated = await changeOrderLegalEntity(order.id, entityId, reason.trim() || null, order.version)
      onChanged(updated)
    } catch (err) {
      setError(localizeApiError(t, err, t('transportOrders.entityChange.failed')))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={t('transportOrders.entityChange.title', { number: order.orderNumber })}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button onClick={() => void apply()} disabled={!canApply}>
            {t('transportOrders.entityChange.confirm')}
          </Button>
        </>
      }
    >
      <FormField
        label={t('transportOrders.entityChange.entityLabel')}
        htmlFor="order-entity"
        required
        hint={t('transportOrders.entityChange.allowedHint')}
      >
        <select id="order-entity" value={entityId} onChange={(e) => selectEntity(e.target.value)} disabled={busy}>
          <option value="">{t('transportOrders.entityChange.chooseEntity')}</option>
          {visibleEntities.map((entity) => (
            <option key={entity.id} value={entity.id}>
              {entity.displayName}
              {entity.id === customerDefaultId ? ` ${t('transportOrders.entityChange.defaultSuffix')}` : ''}
            </option>
          ))}
        </select>
      </FormField>

      {blocked && (
        <p className="customer-import-message customer-import-message-error" role="alert">
          {blocked}
        </p>
      )}
      {!blocked && deviates && entityId !== order.legalEntityId && (
        <p className={lacksRight ? 'customer-import-message customer-import-message-error' : 'customer-form-muted'} role={lacksRight ? 'alert' : undefined}>
          {lacksRight ? t('transportOrders.entityChange.noOverrideRight') : t('transportOrders.entityChange.deviates')}
        </p>
      )}
      {currentImpact && !blocked && (
        <p className={currentImpact.draftInvoiceLinesReleased > 0 ? 'to-impact-warning' : 'customer-form-muted'}>
          {currentImpact.draftInvoiceLinesReleased > 0
            ? t('transportOrders.entityChange.draftReleased', { count: currentImpact.draftInvoiceLinesReleased })
            : t('transportOrders.entityChange.noDraftImpact')}
        </p>
      )}

      {deviates && !lacksRight && (
        <FormField label={t('transportOrders.entityChange.reasonLabel')} htmlFor="order-entity-reason" required>
          <textarea
            id="order-entity-reason"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            rows={2}
            maxLength={500}
            disabled={busy}
          />
        </FormField>
      )}

      {error && (
        <p className="customer-import-message customer-import-message-error" role="alert">
          {error}
        </p>
      )}
    </Modal>
  )
}
