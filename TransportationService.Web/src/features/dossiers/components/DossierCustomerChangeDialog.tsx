import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { localizeApiError } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { CustomerSearchPicker } from '../../customers/components/CustomerSearchPicker'
import type { CustomerListItem } from '../../customers/types'
import { VAT_TREATMENT_LABEL_KEYS, type VatTreatment } from '../../customers/types'
import { CustomerChangeImpactList } from '../../transport-orders/components/CustomerChangeImpactList'
import { getLegalEntityOptions } from '../../legal-entities/api/legalEntitiesApi'
import { changeDossierCustomer, getDossierCustomerChangeImpact, type DossierCustomerChangeImpact } from '../api/dossiersApi'
import type { DossierDetail } from '../types'
import '../../transport-orders/components/commercialChange.css'

interface DossierCustomerChangeDialogProps {
  dossier: DossierDetail
  onClose: () => void
  onChanged: (dossier: DossierDetail) => void
}

/**
 * Sprint 6: the dossier is the commercial authority for its orders, so the customer is changed
 * HERE and every linked order that followed the dossier's customer moves along, in one
 * transaction on the backend. The preview lists every order's consequences; one blocked order
 * blocks the whole dossier.
 */
export function DossierCustomerChangeDialog({ dossier, onClose, onChanged }: DossierCustomerChangeDialogProps) {
  const { t } = useLocale()
  const [target, setTarget] = useState<CustomerListItem | null>(null)
  const [impact, setImpact] = useState<DossierCustomerChangeImpact | null>(null)
  const [entityNames, setEntityNames] = useState<Record<string, string>>({})
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getLegalEntityOptions()
      .then((options) => setEntityNames(Object.fromEntries(options.map((o) => [o.id, o.displayName]))))
      .catch(() => undefined)
  }, [])

  function selectTarget(customer: CustomerListItem | null) {
    setTarget(customer)
    setImpact(null)
    setError(null)
  }

  useEffect(() => {
    if (!target) return
    let cancelled = false
    getDossierCustomerChangeImpact(dossier.id, target.id)
      .then((data) => {
        if (!cancelled) setImpact(data)
      })
      .catch((err) => {
        if (!cancelled) setError(localizeApiError(t, err, t('transportOrders.customerChange.impactFailed')))
      })
    return () => {
      cancelled = true
    }
  }, [dossier.id, target, t])

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
      const updated = await changeDossierCustomer(dossier.id, target.id, reason.trim(), dossier.version)
      onChanged(updated)
    } catch (err) {
      setError(localizeApiError(t, err, t('dossiers.customerChange.failed')))
    } finally {
      setBusy(false)
    }
  }

  const treatment = currentImpact?.newVatTreatment
    ? (VAT_TREATMENT_LABEL_KEYS[currentImpact.newVatTreatment as VatTreatment]
      ? t(VAT_TREATMENT_LABEL_KEYS[currentImpact.newVatTreatment as VatTreatment])
      : currentImpact.newVatTreatment)
    : null
  const impactView = currentImpact

  return (
    <Modal
      title={t('dossiers.customerChange.title', { number: dossier.dossierNumber })}
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
        <dd>{dossier.customerName ?? '—'}</dd>
      </dl>
      <FormField label={t('transportOrders.customerChange.newCustomer')} htmlFor="dossier-customer-change-search" required>
        <CustomerSearchPicker
          id="dossier-customer-change-search"
          value={target}
          onChange={selectTarget}
          currentCustomerId={dossier.customerId}
          disabled={busy}
        />
      </FormField>

      {target && (
        <section className="to-impact" aria-live="polite">
          <h3>{t('transportOrders.customerChange.impactTitle')}</h3>
          {impactLoading && <p className="customer-form-muted">{t('transportOrders.customerChange.impactLoading')}</p>}
          {impactView && (
            <>
              {impactView.blockedReason && (
                <div className="to-impact-blocked" role="alert">
                  <strong>{t('transportOrders.customerChange.blocked')}</strong>
                  <p>{impactView.blockedReason}</p>
                </div>
              )}
              <ul className="to-impact-list">
                <li>
                  {t('dossiers.customerChange.entity', {
                    name: impactView.newLegalEntityId ? entityNames[impactView.newLegalEntityId] ?? impactView.newLegalEntityId : '—',
                  })}
                </li>
                {impactView.newInvoiceLanguage && (
                  <li>{t('transportOrders.customerChange.impact.language', { language: impactView.newInvoiceLanguage.toUpperCase() })}</li>
                )}
                {treatment && <li>{t('transportOrders.customerChange.impact.vatTreatment', { treatment })}</li>}
              </ul>
              <h4>{t('dossiers.customerChange.ordersTitle', { count: impactView.orders.length })}</h4>
              {impactView.orders.length === 0 && <p className="customer-form-muted">{t('dossiers.customerChange.noOrders')}</p>}
              {impactView.orders.map((order) => (
                <details key={order.orderId} className="to-impact-order" open={!!order.blockedReason}>
                  <summary>
                    <strong>{order.orderNumber}</strong>
                    {order.blockedReason && <span className="to-impact-warning"> · {t('transportOrders.customerChange.blocked')}</span>}
                  </summary>
                  <CustomerChangeImpactList impact={order} compact />
                </details>
              ))}
              {impactView.ordersLeftOnOtherCustomer.length > 0 && (
                <p className="to-impact-warning">
                  {t('dossiers.customerChange.leftAlone', { orders: impactView.ordersLeftOnOtherCustomer.join(', ') })}
                </p>
              )}
            </>
          )}
        </section>
      )}

      <FormField
        label={t('transportOrders.customerChange.reasonLabel')}
        htmlFor="dossier-customer-change-reason"
        hint={t('transportOrders.customerChange.reasonHint')}
        required
      >
        <textarea
          id="dossier-customer-change-reason"
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
