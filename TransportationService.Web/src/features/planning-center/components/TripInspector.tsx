import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import {
  CONFLICT_SEVERITY_META, TRIP_STATUS_LABELS, TRIP_STATUS_TONE, type TripDetail,
} from '../../planning/types'
import { formatDate, formatDateTime } from '../../../utils/dates'

interface TripInspectorProps {
  trip: TripDetail | null
  isLoading: boolean
  busy: boolean
  onClose: () => void
  onPlan: (trip: TripDetail) => void
  onRevertToDraft: (trip: TripDetail) => void
  onRemoveOrder: (trip: TripDetail, orderId: string) => void
  onMoveOrder: (trip: TripDetail, orderId: string, direction: -1 | 1) => void
  onClearResource: (trip: TripDetail, slot: 'driver' | 'vehicle' | 'trailer') => void
}

/**
 * Drawer with the selected trip's details. Every action calls the backend through the page's
 * mutation pipeline, so the board context never gets lost.
 */
export function TripInspector({
  trip, isLoading, busy, onClose, onPlan, onRevertToDraft, onRemoveOrder, onMoveOrder, onClearResource,
}: TripInspectorProps) {
  return (
    <aside className="pc-inspector" aria-label="Ritdetails">
      <header className="pc-inspector-header">
        {trip ? (
          <>
            <div>
              <h2>{trip.tripNumber}</h2>
              <Badge tone={TRIP_STATUS_TONE[trip.status]}>{TRIP_STATUS_LABELS[trip.status]}</Badge>
            </div>
            <button type="button" className="pc-inspector-close" onClick={onClose} aria-label="Sluiten">×</button>
          </>
        ) : (
          <h2>{isLoading ? 'Laden…' : 'Geen rit geselecteerd'}</h2>
        )}
      </header>
      {trip && (
        <div className="pc-inspector-body">
          <dl className="pc-inspector-facts">
            <div>
              <dt>Datum</dt>
              <dd>{formatDate(trip.tripDate)}</dd>
            </div>
            <div>
              <dt>Chauffeur</dt>
              <dd>
                {trip.driverName ?? '—'}
                {trip.driverId && (trip.status === 'Draft' || trip.status === 'Planned') && (
                  <button type="button" className="pc-mini" disabled={busy} onClick={() => onClearResource(trip, 'driver')}>
                    verwijder
                  </button>
                )}
              </dd>
            </div>
            <div>
              <dt>Voertuig</dt>
              <dd>
                {trip.vehicleNumber ?? '—'}
                {trip.vehicleId && (trip.status === 'Draft' || trip.status === 'Planned') && (
                  <button type="button" className="pc-mini" disabled={busy} onClick={() => onClearResource(trip, 'vehicle')}>
                    verwijder
                  </button>
                )}
              </dd>
            </div>
            <div>
              <dt>Oplegger</dt>
              <dd>
                {trip.trailerNumber ?? '—'}
                {trip.trailerId && (trip.status === 'Draft' || trip.status === 'Planned') && (
                  <button type="button" className="pc-mini" disabled={busy} onClick={() => onClearResource(trip, 'trailer')}>
                    verwijder
                  </button>
                )}
              </dd>
            </div>
          </dl>

          <h3>Opdrachten ({trip.orders.length})</h3>
          <ul className="pc-inspector-orders">
            {trip.orders.map((order, index) => (
              <li key={order.transportOrderId}>
                <div className="pc-inspector-order">
                  <span>
                    <strong>{order.orderNumber}</strong> · {order.customerName}
                    {order.firstLoadingCity && ` · ${order.firstLoadingCity} → ${order.lastUnloadingCity ?? '?'}`}
                  </span>
                  {(trip.status === 'Draft' || trip.status === 'Planned') && (
                    <span className="pc-inspector-order-actions">
                      <button
                        type="button" className="pc-mini" disabled={busy || index === 0}
                        onClick={() => onMoveOrder(trip, order.transportOrderId, -1)}
                        aria-label={`${order.orderNumber} eerder in de volgorde`}
                      >↑</button>
                      <button
                        type="button" className="pc-mini" disabled={busy || index === trip.orders.length - 1}
                        onClick={() => onMoveOrder(trip, order.transportOrderId, 1)}
                        aria-label={`${order.orderNumber} later in de volgorde`}
                      >↓</button>
                      <button
                        type="button" className="pc-mini pc-mini-danger" disabled={busy}
                        onClick={() => onRemoveOrder(trip, order.transportOrderId)}
                        aria-label={`${order.orderNumber} van de rit halen`}
                      >×</button>
                    </span>
                  )}
                </div>
              </li>
            ))}
          </ul>

          {trip.conflicts.length > 0 && (
            <>
              <h3>Conflicten</h3>
              <ul className="pc-inspector-conflicts">
                {trip.conflicts.map((conflict, index) => (
                  <li key={`${conflict.code}-${index}`}>
                    <Badge tone={CONFLICT_SEVERITY_META[conflict.severity].tone}>
                      {CONFLICT_SEVERITY_META[conflict.severity].label}
                    </Badge>
                    <span>{conflict.description}</span>
                  </li>
                ))}
              </ul>
            </>
          )}

          {trip.overrides.length > 0 && (
            <>
              <h3>Overschrijvingen</h3>
              <ul className="pc-inspector-overrides">
                {trip.overrides.map((entry) => (
                  <li key={entry.id}>
                    <span className="pc-muted">
                      {formatDateTime(entry.occurredAt)} · {entry.actorName ?? 'Onbekend'}
                    </span>
                    <span>{entry.reason}</span>
                  </li>
                ))}
              </ul>
            </>
          )}

          <div className="pc-inspector-actions">
            {trip.allowedTransitions.includes('Planned') && (
              <Button onClick={() => onPlan(trip)} disabled={busy}>Inplannen</Button>
            )}
            {trip.allowedTransitions.includes('Draft') && (
              <Button variant="secondary" onClick={() => onRevertToDraft(trip)} disabled={busy}>
                Terug naar concept
              </Button>
            )}
            <Link className="pc-inspector-link" to={`/planning/${trip.id}`}>Volledige ritpagina →</Link>
          </div>
        </div>
      )}
    </aside>
  )
}
