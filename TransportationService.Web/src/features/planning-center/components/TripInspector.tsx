import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { useLocale } from '../../../i18n/localeContext'
import {
  CONFLICT_SEVERITY_META, TRIP_STATUS_LABELS, TRIP_STATUS_TONE, type TripDetail,
} from '../../planning/types'
import type { PlanningResources } from '../types'
import { driverPickerOptions, trailerPickerOptions, vehiclePickerOptions } from '../utils/resourcePickerOptions'
import { formatDate, formatDateTime } from '../../../utils/dates'

type ResourceSlot = 'driver' | 'vehicle' | 'trailer'

interface TripInspectorProps {
  trip: TripDetail | null
  isLoading: boolean
  busy: boolean
  onClose: () => void
  onPlan: (trip: TripDetail) => void
  onRevertToDraft: (trip: TripDetail) => void
  onRemoveOrder: (trip: TripDetail, orderId: string) => void
  onMoveOrder: (trip: TripDetail, orderId: string, direction: -1 | 1) => void
  /** UX-005: the board's already loaded resources feed the pickers; no extra query per selection. */
  resources: PlanningResources | null
  onAssignResource: (trip: TripDetail, slot: ResourceSlot, resourceId: string | null, label: string | null) => void
}

/**
 * Drawer with the selected trip's details. Every action calls the backend through the page's
 * mutation pipeline, so the board context never gets lost.
 */
export function TripInspector({
  trip, isLoading, busy, onClose, onPlan, onRevertToDraft, onRemoveOrder, onMoveOrder, resources, onAssignResource,
}: TripInspectorProps) {
  const { t } = useLocale()
  return (
    <aside className="pc-inspector" aria-label={t('planningCenter.inspector.label')}>
      <header className="pc-inspector-header">
        {trip ? (
          <>
            <div>
              <h2>{trip.tripNumber}</h2>
              <Badge tone={TRIP_STATUS_TONE[trip.status]}>{t(TRIP_STATUS_LABELS[trip.status])}</Badge>
            </div>
            <button type="button" className="pc-inspector-close" onClick={onClose} aria-label={t('planningCenter.inspector.close')}>×</button>
          </>
        ) : (
          <h2>{isLoading ? t('planningCenter.inspector.loading') : t('planningCenter.inspector.noTrip')}</h2>
        )}
      </header>
      {trip && (
        <div className="pc-inspector-body">
          <dl className="pc-inspector-facts">
            <div>
              <dt>{t('planningCenter.inspector.date')}</dt>
              <dd>{formatDate(trip.tripDate)}</dd>
            </div>
            <ResourceField
              slot="driver" trip={trip} busy={busy} current={trip.driverName}
              label={t('planningCenter.inspector.driver')} options={driverPickerOptions(resources, t)}
              onAssign={onAssignResource}
            />
            <ResourceField
              slot="vehicle" trip={trip} busy={busy} current={trip.vehicleNumber}
              label={t('planningCenter.inspector.vehicle')} options={vehiclePickerOptions(resources, t)}
              onAssign={onAssignResource}
            />
            <ResourceField
              slot="trailer" trip={trip} busy={busy} current={trip.trailerNumber}
              label={t('planningCenter.inspector.trailer')} options={trailerPickerOptions(resources, t)}
              onAssign={onAssignResource}
            />
          </dl>

          <h3>{t('planningCenter.inspector.ordersTitle', { count: trip.orders.length })}</h3>
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
                        aria-label={t('planningCenter.inspector.moveEarlier', { orderNumber: order.orderNumber })}
                      >↑</button>
                      <button
                        type="button" className="pc-mini" disabled={busy || index === trip.orders.length - 1}
                        onClick={() => onMoveOrder(trip, order.transportOrderId, 1)}
                        aria-label={t('planningCenter.inspector.moveLater', { orderNumber: order.orderNumber })}
                      >↓</button>
                      <button
                        type="button" className="pc-mini pc-mini-danger" disabled={busy}
                        onClick={() => onRemoveOrder(trip, order.transportOrderId)}
                        aria-label={t('planningCenter.inspector.removeFromTrip', { orderNumber: order.orderNumber })}
                      >×</button>
                    </span>
                  )}
                </div>
              </li>
            ))}
          </ul>

          {trip.conflicts.length > 0 && (
            <>
              <h3>{t('planningCenter.inspector.conflictsTitle')}</h3>
              <ul className="pc-inspector-conflicts">
                {trip.conflicts.map((conflict, index) => (
                  <li key={`${conflict.code}-${index}`}>
                    {trip.status === 'Draft' && (conflict.code === 'MissingDriver' || conflict.code === 'MissingVehicle') ? (
                      <Badge tone="neutral">{t('planningCenter.board.incomplete')}</Badge>
                    ) : (
                      <Badge tone={CONFLICT_SEVERITY_META[conflict.severity].tone}>
                        {t(CONFLICT_SEVERITY_META[conflict.severity].label)}
                      </Badge>
                    )}
                    <span>{conflict.description}</span>
                  </li>
                ))}
              </ul>
            </>
          )}

          {trip.overrides.length > 0 && (
            <>
              <h3>{t('planningCenter.inspector.overridesTitle')}</h3>
              <ul className="pc-inspector-overrides">
                {trip.overrides.map((entry) => (
                  <li key={entry.id}>
                    <span className="pc-muted">
                      {formatDateTime(entry.occurredAt)} · {entry.actorName ?? t('planningCenter.inspector.unknownActor')}
                    </span>
                    <span>{entry.reason}</span>
                  </li>
                ))}
              </ul>
            </>
          )}

          <div className="pc-inspector-actions">
            {trip.allowedTransitions.includes('Planned') && (
              <Button onClick={() => onPlan(trip)} disabled={busy}>{t('planningCenter.inspector.plan')}</Button>
            )}
            {trip.allowedTransitions.includes('Draft') && (
              <Button variant="secondary" onClick={() => onRevertToDraft(trip)} disabled={busy}>
                {t('planningCenter.inspector.backToDraft')}
              </Button>
            )}
            <Link className="pc-inspector-link" to={`/planning/${trip.id}`}>{t('planningCenter.inspector.fullTripPage')}</Link>
          </div>
        </div>
      )}
    </aside>
  )
}

/** Picker on an assignable trip (keyboard path next to drag-and-drop), plain text once the trip runs. */
function ResourceField({ slot, trip, busy, current, label, options, onAssign }: {
  slot: ResourceSlot
  trip: TripDetail
  busy: boolean
  current: string | null
  label: string
  options: ReturnType<typeof driverPickerOptions>
  onAssign: TripInspectorProps['onAssignResource']
}) {
  const selectedId = slot === 'driver' ? trip.driverId : slot === 'vehicle' ? trip.vehicleId : trip.trailerId
  if (trip.status !== 'Draft' && trip.status !== 'Planned') {
    return (
      <div>
        <dt>{label}</dt>
        <dd>{current ?? '—'}</dd>
      </div>
    )
  }
  const inputId = `pc-inspector-${slot}`
  return (
    <div className="pc-inspector-picker">
      <dt><label htmlFor={inputId}>{label}</label></dt>
      <dd>
        <SearchableSelect
          id={inputId}
          value={selectedId}
          selectedLabel={current ?? undefined}
          options={options}
          disabled={busy}
          onChange={(value) => {
            if (value === selectedId) return
            onAssign(trip, slot, value, options.find((option) => option.value === value)?.label ?? null)
          }}
        />
      </dd>
    </div>
  )
}
