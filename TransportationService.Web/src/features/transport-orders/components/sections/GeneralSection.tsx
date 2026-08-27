import { FormField } from '../../../../components/ui/FormField'
import { useLocale } from '../../../../i18n/localeContext'
import type { CustomerDetail, CustomerListItem } from '../../../customers/types'
import type { LegalEntityOption } from '../../../legal-entities/types'
import type { TransportOrderDetail } from '../../types'

interface GeneralSectionProps {
  order?: TransportOrderDetail
  customers: CustomerListItem[]
  legalEntities: LegalEntityOption[]
  /** Selected customer's detail (intake requirements + block status); null while unknown. */
  customerRequirements: CustomerDetail | null
  requirementHints: string[]
  customerId: string
  setCustomerId: (value: string) => void
  customerReference: string
  setCustomerReference: (value: string) => void
  orderDate: string
  setOrderDate: (value: string) => void
  legalEntityId: string
  setLegalEntityId: (value: string) => void
  dieselSurchargeOverride: boolean
  setDieselSurchargeOverride: (value: boolean) => void
  dieselSurchargePercentOverride: string
  setDieselSurchargePercentOverride: (value: string) => void
  dieselSurchargeOverrideReason: string
  setDieselSurchargeOverrideReason: (value: string) => void
  saving: boolean
  /** First validation message per field path (inline errors + aria-invalid). */
  errors: Record<string, string>
}

/** Algemeen section: customer, reference, date, invoicing entity, diesel-surcharge exception. */
export function GeneralSection({
  order,
  customers,
  legalEntities,
  customerRequirements,
  requirementHints,
  customerId,
  setCustomerId,
  customerReference,
  setCustomerReference,
  orderDate,
  setOrderDate,
  legalEntityId,
  setLegalEntityId,
  dieselSurchargeOverride,
  setDieselSurchargeOverride,
  dieselSurchargePercentOverride,
  setDieselSurchargePercentOverride,
  dieselSurchargeOverrideReason,
  setDieselSurchargeOverrideReason,
  saving,
  errors,
}: GeneralSectionProps) {
  const { t } = useLocale()
  return (
    <>
      <div className="tof-row">
        <FormField label={t('transportOrders.general.customer')} htmlFor="to-customer" required error={errors.customerId}>
          <select
            id="to-customer"
            value={customerId}
            onChange={(e) => setCustomerId(e.target.value)}
            disabled={saving}
            aria-invalid={errors.customerId ? true : undefined}
          >
            <option value="">{t('transportOrders.general.selectCustomer')}</option>
            {/* The list offers only active customers; an existing order keeps its (possibly
                deactivated) customer selectable so editing never silently switches customers. */}
            {order && !customers.some((customer) => customer.id === order.customerId) && (
              <option value={order.customerId}>
                {order.customerName} {t('transportOrders.general.deactivatedSuffix')}
              </option>
            )}
            {customers.map((customer) => (
              <option key={customer.id} value={customer.id}>
                {customer.name} ({customer.customerNumber})
              </option>
            ))}
          </select>
        </FormField>
        <FormField label={t('transportOrders.general.customerReference')} htmlFor="to-ref">
          <input id="to-ref" value={customerReference} onChange={(e) => setCustomerReference(e.target.value)} disabled={saving} maxLength={100} />
        </FormField>
        <FormField label={t('transportOrders.general.orderDate')} htmlFor="to-date">
          <input id="to-date" type="date" value={orderDate} onChange={(e) => setOrderDate(e.target.value)} disabled={saving} />
        </FormField>
      </div>

      {requirementHints.length > 0 && (
        <p className="tof-customer-requirements" role="note">
          {t('transportOrders.general.requirementsNote', { hints: requirementHints.join(', ') })}
        </p>
      )}
      {customerRequirements?.isBlocked && (
        <p className="tof-error" role="alert">
          {customerRequirements.blockReason
            ? t('transportOrders.general.blockedWithReason', { reason: customerRequirements.blockReason })
            : t('transportOrders.general.blocked')}
        </p>
      )}

      <div className="tof-row">
        <FormField
          label={t('transportOrders.general.legalEntity')}
          htmlFor="to-legal-entity"
          hint={t('transportOrders.general.legalEntityHint')}
        >
          <select id="to-legal-entity" value={legalEntityId} onChange={(e) => setLegalEntityId(e.target.value)} disabled={saving}>
            <option value="">{t('transportOrders.general.customerDefault')}</option>
            {legalEntities.map((entity) => (
              <option key={entity.id} value={entity.id}>
                {entity.displayName}
                {entity.isDefault ? ` ${t('transportOrders.general.defaultSuffix')}` : ''}
                {!entity.isActive ? ` ${t('transportOrders.general.inactiveSuffix')}` : ''}
              </option>
            ))}
          </select>
        </FormField>
      </div>

      <details className="tof-stop-extended" open={dieselSurchargeOverride}>
        <summary>{t('transportOrders.general.dieselSummary')}</summary>
        <label className="tof-checkbox">
          <input
            type="checkbox"
            checked={dieselSurchargeOverride}
            onChange={(e) => setDieselSurchargeOverride(e.target.checked)}
            disabled={saving}
          />
          {t('transportOrders.general.dieselOverride')}
        </label>
        {dieselSurchargeOverride && (
          <div className="tof-row">
            <FormField label={t('transportOrders.general.percent')} htmlFor="to-diesel-pct">
              <input
                id="to-diesel-pct"
                type="number"
                min={0}
                max={100}
                step="0.01"
                value={dieselSurchargePercentOverride}
                onChange={(e) => setDieselSurchargePercentOverride(e.target.value)}
                disabled={saving}
              />
            </FormField>
            <FormField
              label={t('transportOrders.general.reason')}
              htmlFor="to-diesel-reason"
              required
              hint={t('transportOrders.general.dieselReasonHint')}
              error={errors.dieselSurchargeOverrideReason}
            >
              <textarea
                id="to-diesel-reason"
                rows={2}
                value={dieselSurchargeOverrideReason}
                onChange={(e) => setDieselSurchargeOverrideReason(e.target.value)}
                disabled={saving}
                maxLength={500}
                aria-invalid={errors.dieselSurchargeOverrideReason ? true : undefined}
              />
            </FormField>
          </div>
        )}
      </details>
    </>
  )
}
