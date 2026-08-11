import { Button } from '../../../components/ui/Button'
import type { TransportOrderDetail } from '../../transport-orders/types'

interface DossierGoodsSummaryProps {
  order: TransportOrderDetail | null
  loading: boolean
  canEdit: boolean
  onEdit: () => void
}

/** §11 Goederen section: compact "2 × Europallet"-style lines from the first linked order. */
export function DossierGoodsSummary({ order, loading, canEdit, onEdit }: DossierGoodsSummaryProps) {
  const lines = order?.cargoItems ?? []
  return (
    <>
      {loading && <p className="placeholder-text">Goederen laden…</p>}
      {!loading && order && lines.length === 0 && (
        <p className="placeholder-text">
          {order.goodsDescription
            ? order.goodsDescription
            : 'Nog geen goederenlijnen.'}
          {order.quantity != null && order.quantityUnit && ` — ${order.quantity} ${order.quantityUnit}`}
        </p>
      )}
      {!loading && lines.length > 0 && (
        <ul className="dossier-goods-lines">
          {lines.map((line) => (
            <li key={line.id}>
              {line.expectedQuantity} × {line.quantityUnitCode ?? line.quantityUnit ?? 'stuks'}
              {line.description && ` — ${line.description}`}
            </li>
          ))}
        </ul>
      )}
      {canEdit && order && (
        <p>
          <Button variant="secondary" onClick={onEdit}>
            {lines.length > 0 ? 'Goederen bewerken' : '+ Goederen'}
          </Button>
        </p>
      )}
    </>
  )
}
