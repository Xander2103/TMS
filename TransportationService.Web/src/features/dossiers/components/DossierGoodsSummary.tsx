import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import { formatQuantity } from '../../../utils/numbers'
import { CargoCoverageBadge } from '../../transport-orders/components/CargoCoverageBadge'
import { GoodsCapacityHint } from '../../transport-orders/components/GoodsCapacityHint'
import { isOnSiteLiftingOrder } from '../../transport-orders/components/onSiteLifting'
import { computeCargoWeightTotals } from '../../transport-orders/components/sections/cargoWeight'
import { unitLabelFrom } from '../../transport-orders/pricing/cargoLabels'
import type { TransportOrderDetail } from '../../transport-orders/types'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'

interface DossierGoodsSummaryProps {
  order: TransportOrderDetail | null
  loading: boolean
  canEdit: boolean
  onEdit: () => void
  /** Capacities of the planned vehicle, when the host knows one; absent → "nog te controleren". */
  vehiclePayloadKg?: number | null
  tailLiftCapacityKg?: number | null
}

/** §11 Goederen section: compact "2 × Europallet"-style lines from the first linked order. */
export function DossierGoodsSummary({
  order, loading, canEdit, onEdit, vehiclePayloadKg, tailLiftCapacityKg,
}: DossierGoodsSummaryProps) {
  const { t } = useLocale()
  // Same resolver as the order shell: catalogue name for a managed code (the code itself while
  // the catalogue is still loading or does not know it), else the preserved legacy free text.
  const { options: unitTypes } = useLookupOptions('/api/unit-types')
  const unitLabel = unitLabelFrom(unitTypes)
  const lines = order?.cargoItems ?? []
  // D2: an on-site lifting job lifts a load — it has no goods lines, and that is not an omission.
  const liftingWithoutGoods = isOnSiteLiftingOrder(order) && lines.length === 0
  const weightTotals = computeCargoWeightTotals(lines)
  return (
    <>
      {loading && <p className="placeholder-text">{t('dossiers.goods.loading')}</p>}
      {!loading && liftingWithoutGoods && (
        <p className="placeholder-text goods-empty-lifting">{t('dossiers.goods.noGoodsOnSiteLifting')}</p>
      )}
      {!loading && order && lines.length === 0 && !liftingWithoutGoods && (
        <p className="placeholder-text">
          {order.goodsDescription
            ? order.goodsDescription
            : t('dossiers.goods.noLines')}
          {order.quantity != null && order.quantityUnit && ` — ${order.quantity} ${order.quantityUnit}`}
        </p>
      )}
      {!loading && lines.length > 0 && (
        <>
          <ul className="dossier-goods-lines">
            {lines.map((line) => (
              <li key={line.id}>
                {line.expectedQuantity} × {unitLabel(line.quantityUnitCode, line.quantityUnit) || t('dossiers.goods.fallbackUnit')}
                {line.description && ` — ${line.description}`}
                {/* Only what was entered: a total-only line shows its total, never a per-unit guess. */}
                {line.weightPerUnitKg !== null &&
                  ` · ${t('dossiers.goods.weightPerUnit', { value: formatQuantity(line.weightPerUnitKg) })}`}
                {line.totalWeightKg !== null &&
                  ` · ${t('dossiers.goods.totalWeight', { value: formatQuantity(line.totalWeightKg) })}`}{' '}
                <CargoCoverageBadge coverage={line.commercialCoverage} />
              </li>
            ))}
          </ul>
          <GoodsCapacityHint
            totalWeightKg={weightTotals.totalWeightKg}
            heaviestUnitKg={weightTotals.heaviestUnitKg}
            linesWithoutUnitWeight={weightTotals.linesWithoutUnitWeight}
            payloadKg={vehiclePayloadKg}
            tailLiftCapacityKg={tailLiftCapacityKg}
          />
        </>
      )}
      {canEdit && order && !liftingWithoutGoods && (
        <p>
          <Button variant="secondary" onClick={onEdit}>
            {lines.length > 0 ? t('dossiers.goods.edit') : t('dossiers.goods.add')}
          </Button>
        </p>
      )}
    </>
  )
}
