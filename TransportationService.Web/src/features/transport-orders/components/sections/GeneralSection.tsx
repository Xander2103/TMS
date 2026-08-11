import { FormField } from '../../../../components/ui/FormField'
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
  return (
    <>
      <div className="tof-row">
        <FormField label="Klant" htmlFor="to-customer" required error={errors.customerId}>
          <select
            id="to-customer"
            value={customerId}
            onChange={(e) => setCustomerId(e.target.value)}
            disabled={saving}
            aria-invalid={errors.customerId ? true : undefined}
          >
            <option value="">Selecteer een klant…</option>
            {/* The list offers only active customers; an existing order keeps its (possibly
                deactivated) customer selectable so editing never silently switches customers. */}
            {order && !customers.some((customer) => customer.id === order.customerId) && (
              <option value={order.customerId}>{order.customerName} (gedeactiveerd)</option>
            )}
            {customers.map((customer) => (
              <option key={customer.id} value={customer.id}>
                {customer.name} ({customer.customerNumber})
              </option>
            ))}
          </select>
        </FormField>
        <FormField label="Klantreferentie" htmlFor="to-ref">
          <input id="to-ref" value={customerReference} onChange={(e) => setCustomerReference(e.target.value)} disabled={saving} maxLength={100} />
        </FormField>
        <FormField label="Opdrachtdatum" htmlFor="to-date">
          <input id="to-date" type="date" value={orderDate} onChange={(e) => setOrderDate(e.target.value)} disabled={saving} />
        </FormField>
      </div>

      {requirementHints.length > 0 && (
        <p className="tof-customer-requirements" role="note">
          Let op voor deze klant: {requirementHints.join(', ')}.
        </p>
      )}
      {customerRequirements?.isBlocked && (
        <p className="tof-error" role="alert">
          Deze klant is geblokkeerd{customerRequirements.blockReason ? ` (${customerRequirements.blockReason})` : ''}; er
          kunnen geen nieuwe opdrachten voor worden aangemaakt.
        </p>
      )}

      <div className="tof-row">
        <FormField
          label="Facturerende entiteit"
          htmlFor="to-legal-entity"
          hint="Leeg = de standaardentiteit van de klant."
        >
          <select id="to-legal-entity" value={legalEntityId} onChange={(e) => setLegalEntityId(e.target.value)} disabled={saving}>
            <option value="">— Klantstandaard —</option>
            {legalEntities.map((entity) => (
              <option key={entity.id} value={entity.id}>
                {entity.displayName}
                {entity.isDefault ? ' (standaard)' : ''}
                {!entity.isActive ? ' — inactief' : ''}
              </option>
            ))}
          </select>
        </FormField>
      </div>

      <details className="tof-stop-extended" open={dieselSurchargeOverride}>
        <summary>Dieseltoeslag afwijking</summary>
        <label className="tof-checkbox">
          <input
            type="checkbox"
            checked={dieselSurchargeOverride}
            onChange={(e) => setDieselSurchargeOverride(e.target.checked)}
            disabled={saving}
          />
          Afwijkend percentage voor deze opdracht
        </label>
        {dieselSurchargeOverride && (
          <div className="tof-row">
            <FormField label="Percentage (%)" htmlFor="to-diesel-pct">
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
              label="Reden"
              htmlFor="to-diesel-reason"
              required
              hint="De klantconfiguratie is de standaard; afwijkingen worden gelogd."
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
