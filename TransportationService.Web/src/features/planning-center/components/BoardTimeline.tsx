import { useState, type DragEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { TRIP_STATUS_LABELS, TRIP_STATUS_TONE } from '../../planning/types'
import {
  blockPosition, formatDayHeading, formatTimeRange, gridHours, type BoardViewDays,
} from '../utils/boardMath'
import { DRAG_MIME, decodeDragPayload, encodeDragPayload, type DragPayload, type PlanningBoardTrip } from '../types'

interface BoardTimelineProps {
  dates: string[]
  days: BoardViewDays
  trips: PlanningBoardTrip[]
  isLoading: boolean
  selectedTripId: string | null
  onSelectTrip: (trip: PlanningBoardTrip) => void
  /** A payload was dropped on a trip block (order/driver/vehicle/trailer). */
  onDropOnTrip: (trip: PlanningBoardTrip, payload: DragPayload) => void
  /** A payload was dropped on a day column (order → new trip, trip → reschedule). */
  onDropOnDay: (dateIso: string, payload: DragPayload) => void
}

/** Center zone: day columns on a 05:00–22:00 grid; trips without times stack on top. */
export function BoardTimeline({
  dates, days, trips, isLoading, selectedTripId, onSelectTrip, onDropOnTrip, onDropOnDay,
}: BoardTimelineProps) {
  const [dragOverKey, setDragOverKey] = useState<string | null>(null)

  function readPayload(event: DragEvent): DragPayload | null {
    return decodeDragPayload(event.dataTransfer.getData(DRAG_MIME))
  }

  function allowDrop(event: DragEvent) {
    if (event.dataTransfer.types.includes(DRAG_MIME)) {
      event.preventDefault()
      event.dataTransfer.dropEffect = 'move'
    }
  }

  const byDate = new Map<string, PlanningBoardTrip[]>()
  for (const date of dates) byDate.set(date, [])
  for (const trip of trips) byDate.get(trip.tripDate)?.push(trip)

  return (
    <section className={`pc-board pc-board-${days}`} aria-label="Planbord">
      <div className="pc-board-axis" aria-hidden="true">
        {gridHours().map((hour) => (
          <span key={hour}>{String(hour).padStart(2, '0')}:00</span>
        ))}
      </div>
      <div className="pc-board-days">
        {dates.map((date) => {
          const dayTrips = byDate.get(date) ?? []
          const unscheduled = dayTrips.filter((t) => blockPosition(t.plannedStart, t.plannedEnd) === null)
          const scheduled = dayTrips.filter((t) => blockPosition(t.plannedStart, t.plannedEnd) !== null)
          return (
            <div
              key={date}
              className={`pc-day${dragOverKey === date ? ' pc-drop-target' : ''}`}
              onDragOver={allowDrop}
              onDragEnter={() => setDragOverKey(date)}
              onDragLeave={(event) => {
                if (!event.currentTarget.contains(event.relatedTarget as Node)) setDragOverKey(null)
              }}
              onDrop={(event) => {
                event.preventDefault()
                setDragOverKey(null)
                const payload = readPayload(event)
                if (payload) onDropOnDay(date, payload)
              }}
            >
              <header className="pc-day-header">{formatDayHeading(date)}</header>
              {unscheduled.length > 0 && (
                <div className="pc-day-unscheduled">
                  {unscheduled.map((trip) => (
                    <TripBlock
                      key={trip.id}
                      trip={trip}
                      floating={false}
                      selected={trip.id === selectedTripId}
                      dragOver={dragOverKey === trip.id}
                      setDragOver={setDragOverKey}
                      onSelect={onSelectTrip}
                      onDrop={onDropOnTrip}
                      allowDrop={allowDrop}
                      readPayload={readPayload}
                    />
                  ))}
                </div>
              )}
              <div className="pc-day-grid">
                {scheduled.map((trip) => {
                  const pos = blockPosition(trip.plannedStart, trip.plannedEnd)!
                  return (
                    <div
                      key={trip.id}
                      className="pc-block-anchor"
                      style={{ top: `${pos.topPct}%`, height: `${pos.heightPct}%` }}
                    >
                      <TripBlock
                        trip={trip}
                        floating
                        selected={trip.id === selectedTripId}
                        dragOver={dragOverKey === trip.id}
                        setDragOver={setDragOverKey}
                        onSelect={onSelectTrip}
                        onDrop={onDropOnTrip}
                        allowDrop={allowDrop}
                        readPayload={readPayload}
                      />
                    </div>
                  )
                })}
              </div>
            </div>
          )
        })}
      </div>
      {isLoading && <p className="pc-muted pc-board-loading">Planbord laden…</p>}
    </section>
  )
}

interface TripBlockProps {
  trip: PlanningBoardTrip
  floating: boolean
  selected: boolean
  dragOver: boolean
  setDragOver: (key: string | null) => void
  onSelect: (trip: PlanningBoardTrip) => void
  onDrop: (trip: PlanningBoardTrip, payload: DragPayload) => void
  allowDrop: (event: DragEvent) => void
  readPayload: (event: DragEvent) => DragPayload | null
}

function TripBlock({
  trip, floating, selected, dragOver, setDragOver, onSelect, onDrop, allowDrop, readPayload,
}: TripBlockProps) {
  const timeRange = formatTimeRange(trip.plannedStart, trip.plannedEnd)
  const overloaded =
    (trip.capacityWeightKg !== null && trip.totalWeightKg !== null && trip.totalWeightKg > trip.capacityWeightKg) ||
    (trip.capacityVolumeM3 !== null && trip.totalVolumeM3 !== null && trip.totalVolumeM3 > trip.capacityVolumeM3)

  return (
    <article
      className={[
        'pc-trip',
        floating ? 'pc-trip-floating' : '',
        selected ? 'pc-selected' : '',
        dragOver ? 'pc-drop-target' : '',
        trip.blockingConflictCount > 0 ? 'pc-trip-blocked' : '',
      ].filter(Boolean).join(' ')}
      tabIndex={0}
      draggable={trip.status === 'Draft' || trip.status === 'Planned'}
      onDragStart={(event) => {
        event.stopPropagation()
        event.dataTransfer.setData(DRAG_MIME, encodeDragPayload({
          kind: 'trip', id: trip.id, label: trip.tripNumber, version: trip.version,
        }))
        event.dataTransfer.effectAllowed = 'move'
      }}
      onDragOver={allowDrop}
      onDragEnter={(event) => {
        event.stopPropagation()
        setDragOver(trip.id)
      }}
      onDrop={(event) => {
        event.preventDefault()
        event.stopPropagation()
        setDragOver(null)
        const payload = readPayload(event)
        if (payload) onDrop(trip, payload)
      }}
      onClick={(event) => {
        event.stopPropagation()
        onSelect(trip)
      }}
      onKeyDown={(event) => {
        if (event.key === 'Enter') {
          event.preventDefault()
          onSelect(trip)
        }
      }}
      aria-label={`Rit ${trip.tripNumber}, ${TRIP_STATUS_LABELS[trip.status]}`}
    >
      <div className="pc-trip-head">
        <strong>{trip.tripNumber}</strong>
        <Badge tone={TRIP_STATUS_TONE[trip.status]}>{TRIP_STATUS_LABELS[trip.status]}</Badge>
      </div>
      {timeRange && <p className="pc-trip-time">{timeRange}</p>}
      <p className="pc-trip-line">{trip.driverName ?? 'Geen chauffeur'}</p>
      <p className="pc-trip-line">
        {trip.vehicleNumber ?? 'Geen voertuig'}
        {trip.trailerNumber ? ` + ${trip.trailerNumber}` : ''}
      </p>
      {trip.routeSummary && <p className="pc-trip-line pc-trip-route">{trip.routeSummary}</p>}
      <p className="pc-trip-line pc-trip-meta">
        <span>{trip.orderCount} opdr.</span>
        <span>{trip.stopCount} stops</span>
        {trip.totalWeightKg !== null && (
          <span className={overloaded ? 'pc-overloaded' : undefined}>
            {trip.totalWeightKg.toLocaleString('nl-BE')}
            {trip.capacityWeightKg !== null ? `/${trip.capacityWeightKg.toLocaleString('nl-BE')}` : ''} kg
          </span>
        )}
      </p>
      {(trip.blockingConflictCount > 0 || trip.warningConflictCount > 0) && (
        <div className="pc-trip-conflicts">
          {trip.blockingConflictCount > 0 && <Badge tone="danger">{trip.blockingConflictCount} blokkerend</Badge>}
          {trip.warningConflictCount > 0 && <Badge tone="warning">{trip.warningConflictCount} waarschuwing</Badge>}
        </div>
      )}
    </article>
  )
}
