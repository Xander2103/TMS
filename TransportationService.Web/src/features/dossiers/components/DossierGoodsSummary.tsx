import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import type { TransportOrderDetail } from '../../transport-orders/types'

interface DossierGoodsSummaryProps {
  order: TransportOrderDetail | null
  loading: boolean
  canEdit: boolean
  onEdit: () => void
}

/** §11 Goederen section: compact "2 × Europallet"-style lines from the first linked order. */
export function DossierGoodsSummary({ order, loading, canEdit, onEdit }: DossierGoodsSummaryProps) {
  const { t } = useLocale()
  const lines = order?.cargoItems ?? []
  return (
    <>
      {loading && <p className="placeholder-text">{t('dossiers.goods.loading')}</p>}
      {!loading && order && lines.length === 0 && (
        <p className="placeholder-text">
          {order.goodsDescription
            ? order.goodsDescription
            : t('dossiers.goods.noLines')}
          {order.quantity != null && order.quantityUnit && ` — ${order.quantity} ${order.quantityUnit}`}
        </p>
      )}
      {!loading && lines.length > 0 && (
        <ul className="dossier-goods-lines">
          {lines.map((line) => (
            <li key={line.id}>
              {line.expectedQuantity} × {line.quantityUnitCode ?? line.quantityUnit ?? t('dossiers.goods.fallbackUnit')}
              {line.description && ` — ${line.description}`}
            </li>
          ))}
        </ul>
      )}
      {canEdit && order && (
        <p>
          <Button variant="secondary" onClick={onEdit}>
            {lines.length > 0 ? t('dossiers.goods.edit') : t('dossiers.goods.add')}
          </Button>
        </p>
      )}
    </>
  )
}
