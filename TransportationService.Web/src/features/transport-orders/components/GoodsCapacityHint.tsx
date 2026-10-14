import { useLocale } from '../../../i18n/localeContext'
import { formatQuantity } from '../../../utils/numbers'
import './goods-capacity.css'

export interface GoodsCapacityHintProps {
  /** Sum of the goods-line totals; null = no line carries a weight. */
  totalWeightKg: number | null
  /** Heaviest single unit, ONLY from lines with a weight per unit; null = unknown. */
  heaviestUnitKg: number | null
  /** `Vehicle.payloadKg` of the assigned vehicle; absent/null = unknown. */
  payloadKg?: number | null
  /** `Vehicle.tailLiftCapacityKg` of the assigned vehicle; absent/null = unknown. */
  tailLiftCapacityKg?: number | null
  /** Lines without a weight per unit: their heaviest unit cannot be judged. */
  linesWithoutUnitWeight?: number
  /**
   * Is there a vehicle to judge against? `none` (default) = nothing assigned yet: the hint stays
   * quiet and neutral. `loading` = assigned, capacities still being fetched. `assigned` = the
   * capacities above are the vehicle's real data; a missing payload is then worth attention.
   */
  vehicle?: VehicleContext
}

export type VehicleContext = 'none' | 'loading' | 'assigned'

/**
 * Operational capacity hint (master sprint 2026-09-21, D4; B55). Purely presentational: it
 * compares what it is GIVEN. Without a vehicle there is nothing to compare, so it only shows the
 * weights and a muted "available after vehicle assignment" line — no warning tone. With a vehicle
 * a missing payload gets an attention line and an exceeded known capacity a warning. It never
 * reports a load as fine: pallet-truck/forklift capacities are not modelled, which a muted note
 * says plainly.
 */
export function GoodsCapacityHint({
  totalWeightKg,
  heaviestUnitKg,
  payloadKg = null,
  tailLiftCapacityKg = null,
  linesWithoutUnitWeight = 0,
  vehicle = 'none',
}: GoodsCapacityHintProps) {
  const { t } = useLocale()
  const kg = (value: number) => `${formatQuantity(value)} kg`

  const assigned = vehicle === 'assigned'
  const warnings: string[] = []
  if (assigned && payloadKg !== null && totalWeightKg !== null && totalWeightKg > payloadKg) {
    warnings.push(t('transportOrders.capacity.payloadExceeded', { weight: kg(totalWeightKg), capacity: kg(payloadKg) }))
  }
  if (assigned && tailLiftCapacityKg !== null && heaviestUnitKg !== null && heaviestUnitKg > tailLiftCapacityKg) {
    warnings.push(t('transportOrders.capacity.tailLiftExceeded', { weight: kg(heaviestUnitKg), capacity: kg(tailLiftCapacityKg) }))
  }


  return (
    <aside className="goods-capacity" aria-label={t('transportOrders.capacity.title')}>
      <h4>{t('transportOrders.capacity.title')}</h4>
      <dl>
        <div>
          <dt>{t('transportOrders.capacity.totalWeight')}</dt>
          <dd>{totalWeightKg !== null ? kg(totalWeightKg) : t('transportOrders.capacity.unknownValue')}</dd>
        </div>
        <div>
          <dt>{t('transportOrders.capacity.heaviestUnit')}</dt>
          <dd>{heaviestUnitKg !== null ? kg(heaviestUnitKg) : t('transportOrders.capacity.unknownValue')}</dd>
        </div>
        {assigned && payloadKg !== null && (
          <div>
            <dt>{t('transportOrders.capacity.payload')}</dt>
            <dd>{kg(payloadKg)}</dd>
          </div>
        )}
        {assigned && tailLiftCapacityKg !== null && (
          <div>
            <dt>{t('transportOrders.capacity.tailLift')}</dt>
            <dd>{kg(tailLiftCapacityKg)}</dd>
          </div>
        )}
      </dl>
      {linesWithoutUnitWeight > 0 && (
        <p className="goods-capacity-note">
          {t('transportOrders.capacity.linesWithoutUnitWeight', { count: linesWithoutUnitWeight })}
        </p>
      )}
      {warnings.map((warning) => (
        <p key={warning} className="goods-capacity-warning" role="alert">
          {warning}
        </p>
      ))}
      {assigned && payloadKg === null && (
        <p className="goods-capacity-attention">{t('transportOrders.capacity.payloadMissing')}</p>
      )}
      {assigned && <p className="goods-capacity-note">{t('transportOrders.capacity.handlingNotModelled')}</p>}
      {vehicle === 'none' && <p className="goods-capacity-note">{t('transportOrders.capacity.afterAssignment')}</p>}
    </aside>
  )
}
