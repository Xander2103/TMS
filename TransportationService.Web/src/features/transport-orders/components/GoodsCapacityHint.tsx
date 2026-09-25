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
}

/**
 * Operational capacity hint (master sprint 2026-09-21, D4). Purely presentational: it compares
 * what it is GIVEN and says so when something is unknown. It never reports a load as fine —
 * pallet-truck and forklift capacities are not modelled anywhere, so "Capaciteit nog te
 * controleren" always stays; a known capacity can only add a warning.
 */
export function GoodsCapacityHint({
  totalWeightKg,
  heaviestUnitKg,
  payloadKg = null,
  tailLiftCapacityKg = null,
  linesWithoutUnitWeight = 0,
}: GoodsCapacityHintProps) {
  const { t } = useLocale()
  const kg = (value: number) => `${formatQuantity(value)} kg`

  const warnings: string[] = []
  if (payloadKg !== null && totalWeightKg !== null && totalWeightKg > payloadKg) {
    warnings.push(t('transportOrders.capacity.payloadExceeded', { weight: kg(totalWeightKg), capacity: kg(payloadKg) }))
  }
  if (tailLiftCapacityKg !== null && heaviestUnitKg !== null && heaviestUnitKg > tailLiftCapacityKg) {
    warnings.push(t('transportOrders.capacity.tailLiftExceeded', { weight: kg(heaviestUnitKg), capacity: kg(tailLiftCapacityKg) }))
  }

  // What still has to be checked by a person; the handling equipment is always on the list.
  const unknown: string[] = []
  if (payloadKg === null) unknown.push(t('transportOrders.capacity.unknownPayload'))
  if (tailLiftCapacityKg === null) unknown.push(t('transportOrders.capacity.unknownTailLift'))
  unknown.push(t('transportOrders.capacity.unknownHandling'))

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
        {payloadKg !== null && (
          <div>
            <dt>{t('transportOrders.capacity.payload')}</dt>
            <dd>{kg(payloadKg)}</dd>
          </div>
        )}
        {tailLiftCapacityKg !== null && (
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
      <p className="goods-capacity-unknown">
        <strong>{t('transportOrders.capacity.toCheck')}</strong>
        {` — ${unknown.join(', ')}`}
      </p>
    </aside>
  )
}
