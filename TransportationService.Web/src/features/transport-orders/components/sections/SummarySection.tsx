import { FormField } from '../../../../components/ui/FormField'
import { formatCurrency } from '../../../../utils/numbers'
import type { CustomerListItem } from '../../../customers/types'
import type { PriceCalculationResult, ServiceOption } from '../../../tarification/api/pricingApi'
import type { UnitOptionItem } from '../UnitSelect'
import type { StopInput, TransportOrderDetail } from '../../types'
import type { CargoFormRow, StopFormRow } from './orderFormState'

interface SummarySectionProps {
  order?: TransportOrderDetail
  customers: CustomerListItem[]
  customerId: string
  stops: StopFormRow[]
  cargoItems: CargoFormRow[]
  quantity: string
  quantityUnit: string
  quantityUnitCode: string | null
  weightKg: string
  unitOptions: UnitOptionItem[]
  serviceOptions: ServiceOption[]
  selectedServiceOptionIds: string[]
  preview: PriceCalculationResult | null
  priceIsManual: boolean
  agreedPrice: string
  notes: string
  setNotes: (value: string) => void
  saving: boolean
}

/** Samenvatting section: read-only recap + the order notes (unchanged behavior, Wave 1 phase 6). */
export function SummarySection({
  order,
  customers,
  customerId,
  stops,
  cargoItems,
  quantity,
  quantityUnit,
  quantityUnitCode,
  weightKg,
  unitOptions,
  serviceOptions,
  selectedServiceOptionIds,
  preview,
  priceIsManual,
  agreedPrice,
  notes,
  setNotes,
  saving,
}: SummarySectionProps) {
  const stopSummary = (kind: StopInput['stopType']) => {
    const matching = stops.filter((s) => s.stopType === kind)
    return matching.map((s) => s.city || s.locationName || '—').join(' → ') || '—'
  }

  return (
    <>
      <dl className="tof-summary">
        <div>
          <dt>Klant</dt>
          <dd>{customers.find((c) => c.id === customerId)?.name ?? order?.customerName ?? '—'}</dd>
        </div>
        <div>
          <dt>Laden</dt>
          <dd>{stopSummary('Loading')}</dd>
        </div>
        <div>
          <dt>Lossen</dt>
          <dd>{stopSummary('Unloading')}</dd>
        </div>
        <div>
          <dt>Goederen</dt>
          <dd>
            {quantity || '—'} {unitOptions.find((u) => u.code === quantityUnitCode)?.name ?? quantityUnit ?? ''}
            {weightKg ? ` · ${weightKg} kg` : ''}
            {cargoItems.length > 0 ? ` · ${cargoItems.length} goederenlijn(en)` : ''}
          </dd>
        </div>
        <div>
          <dt>Diensten</dt>
          <dd>
            {selectedServiceOptionIds.length > 0
              ? serviceOptions.filter((o) => selectedServiceOptionIds.includes(o.id)).map((o) => o.name).join(', ')
              : '—'}
          </dd>
        </div>
        <div>
          <dt>Prijs</dt>
          <dd>
            {priceIsManual
              ? `${formatCurrency(Number(agreedPrice) || 0)} (handmatig)`
              : preview
                ? `${formatCurrency(preview.total)} (berekend)`
                : agreedPrice
                  ? formatCurrency(Number(agreedPrice) || 0)
                  : '—'}
          </dd>
        </div>
      </dl>
      <FormField label="Notities" htmlFor="to-notes">
        <textarea id="to-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={saving} maxLength={4000} />
      </FormField>
    </>
  )
}
