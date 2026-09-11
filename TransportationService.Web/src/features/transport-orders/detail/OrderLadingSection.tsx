import { Badge } from '../../../components/ui/Badge'
import { useLocale } from '../../../i18n/localeContext'
import { formatCurrency, formatQuantity } from '../../../utils/numbers'
import { UNIT_TYPE_LABELS } from '../../packages/types'
import { OrderDocumentsPanel } from '../components/OrderDocumentsPanel'
import { OrderDocumentStrategyPanel } from '../components/OrderDocumentStrategyPanel'
import { useOrderDetail } from './orderDetailContext'

/**
 * Lading workspace: the commercial goods facts, the goods lines and — because photos and
 * documents belong to the shipment — the order documents panel with its document strategy.
 * Same data and behaviour as before the redesign, presented in panels.
 */
export function OrderLadingSection() {
  const { t } = useLocale()
  const { order, unitLabel, aggregateCargo, editing } = useOrderDetail()

  return (
    <div className="tod-section" id="sectie-lading">
      <section className="tod-panel">
        <h2>Lading</h2>
        <dl className="to-facts tod-facts">
          <div>
            <dt>Goederen</dt>
            <dd>{order.goodsDescription ?? '—'}</dd>
          </div>
          {order.cargoItems.length > 0 ? (
            <div>
              <dt>Lading</dt>
              <dd>
                <ul className="to-lading-list">
                  {aggregateCargo(order.cargoItems).map(({ unit, total }) => (
                    <li key={unit}>{formatQuantity(total)} {unit}</li>
                  ))}
                </ul>
              </dd>
            </div>
          ) : (
            <div>
              <dt>Aantal</dt>
              <dd>{order.quantity !== null ? `${order.quantity} ${unitLabel(order.quantityUnitCode, order.quantityUnit)}`.trim() : '—'}</dd>
            </div>
          )}
          <div>
            <dt>Gewicht</dt>
            <dd>{order.weightKg !== null ? `${formatQuantity(order.weightKg)} kg` : '—'}</dd>
          </div>
          <div>
            <dt>Volume</dt>
            <dd>{order.volumeM3 !== null ? `${formatQuantity(order.volumeM3)} m³` : '—'}</dd>
          </div>
          <div>
            <dt>Paletten</dt>
            <dd>{order.palletCount ?? '—'}</dd>
          </div>
          <div>
            <dt>Prijs</dt>
            <dd>{order.agreedPrice !== null ? formatCurrency(order.agreedPrice) : '—'}</dd>
          </div>
          <div>
            <dt>Kenmerken</dt>
            <dd>
              {order.adrRequired && <Badge tone="danger">ADR</Badge>}
              {order.craneRequired && <Badge tone="info">Kraan</Badge>}
              {order.plateauRequired && <Badge tone="info">Plateau</Badge>}
              {order.moffettRequired && <Badge tone="info">Moffett</Badge>}
              {order.isReturnMovement && <Badge tone="neutral">Retour</Badge>}
              {!order.adrRequired && !order.craneRequired && !order.plateauRequired &&
                !order.moffettRequired && !order.isReturnMovement && '—'}
            </dd>
          </div>
        </dl>
        {order.notes && <p className="to-notes">{order.notes}</p>}
      </section>

      {order.cargoItems.length > 0 && (
        <section className="tod-panel">
          <h2>Goederenlijnen</h2>
          <div className="tod-table-wrap">
            <table className="to-stops-table tod-table">
              <thead>
                <tr>
                  <th>#</th>
                  <th>Omschrijving</th>
                  <th>Type</th>
                  <th>Barcode</th>
                  <th>Verwacht</th>
                  <th>Gewicht</th>
                  <th>Volume/stuk</th>
                  <th>Kenmerken</th>
                  <th>Notities</th>
                </tr>
              </thead>
              <tbody>
                {order.cargoItems.map((item) => (
                  <tr key={item.id}>
                    <td>{item.sequence}</td>
                    <td>{item.description ?? `${item.expectedQuantity} × ${unitLabel(item.quantityUnitCode, item.quantityUnit)}`}</td>
                    <td>{item.unitType ? (item.unitTypeLabel ?? t(UNIT_TYPE_LABELS[item.unitType])) : '—'}</td>
                    <td>{item.barcode ? <code>{item.barcode}</code> : '—'}</td>
                    <td>
                      {item.expectedQuantity} {unitLabel(item.quantityUnitCode, item.quantityUnit)}
                    </td>
                    <td>{item.totalWeightKg !== null ? `${formatQuantity(item.totalWeightKg)} kg` : '—'}</td>
                    <td>{item.volumeM3 !== null ? `${formatQuantity(item.volumeM3)} m³` : '—'}</td>
                    <td>
                      {item.adrRequired && <Badge tone="danger">ADR</Badge>}{' '}
                      {!item.stackable && <Badge tone="warning">Niet stapelbaar</Badge>}
                    </td>
                    <td>{item.notes ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {!editing && (
        <section className="tod-panel" id="sectie-fotos">
          <h2>Foto's &amp; documenten</h2>
          <OrderDocumentStrategyPanel orderId={order.id} />
          <OrderDocumentsPanel orderId={order.id} />
        </section>
      )}
    </div>
  )
}
