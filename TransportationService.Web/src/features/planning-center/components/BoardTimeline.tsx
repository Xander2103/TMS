import { useEffect, useRef, useState, type CSSProperties, type DragEvent } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { useLocale } from '../../../i18n/localeContext'
import { TRIP_STATUS_LABELS, TRIP_STATUS_TONE } from '../../planning/types'
import {
  blockPosition, DAY_END_HOUR, DAY_START_HOUR, dayColumnLevel, dayColumnWidth, daysFitWithoutScroll, formatDayHeading, formatDayHeadingShort,
  formatTimeRange, gridHours, MIN_DAY_WIDTH, type BoardDensity, type BoardViewDays, type DayColumnLevel,
} from '../utils/boardMath'
import { formatWeightShort } from '../utils/boardFormat'
import { DRAG_MIME, decodeDragPayload, encodeDragPayload, type DragPayload, type PlanningBoardTrip } from '../types'
import { ConflictHint } from './ConflictHint'

interface BoardTimelineProps {
  dates: string[]
  days: BoardViewDays
  density: BoardDensity
  trips: PlanningBoardTrip[]
  isLoading: boolean
  selectedTripId: string | null
  /** True when both side panels are already rails (the "collapse for a full week" hint is moot). */
  sidesCollapsed: boolean
  onSelectTrip: (trip: PlanningBoardTrip) => void
  /** A payload was dropped on a trip block (order/driver/vehicle/trailer). */
  onDropOnTrip: (trip: PlanningBoardTrip, payload: DragPayload) => void
  /** A payload was dropped on a day column (order → new trip, trip → reschedule). */
  onDropOnDay: (dateIso: string, payload: DragPayload) => void
  /** B08: offered by the week hint when the columns do not fit next to each other. */
  onCollapseSides?: () => void
}

/**
 * Center zone: day columns on a 05:00–22:00 grid; trips without times stack on top.
 *
 * B08: the board is width-aware. The timeline measures itself; every day column gets an even
 * share of that width down to a readable minimum (`MIN_DAY_WIDTH`), and the cards adapt what
 * they show to the column width (`DayColumnLevel`). Only when the minimum does not fit does the
 * TIMELINE scroll sideways — the page never does.
 */
export function BoardTimeline({
  dates, days, density, trips, isLoading, selectedTripId, sidesCollapsed,
  onSelectTrip, onDropOnTrip, onDropOnDay, onCollapseSides,
}: BoardTimelineProps) {
  const { t } = useLocale()
  const [dragOverKey, setDragOverKey] = useState<string | null>(null)
  const daysRef = useRef<HTMLDivElement>(null)
  const [availableWidth, setAvailableWidth] = useState(0)

  // The measured width drives the column level; RO callbacks are asynchronous, so this is not a
  // synchronous setState-in-effect (React Compiler rule).
  useEffect(() => {
    const element = daysRef.current
    if (!element || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver((entries) => {
      const width = entries[0]?.contentRect.width ?? 0
      setAvailableWidth(Math.floor(width))
    })
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  const dayWidth = dayColumnWidth(availableWidth, days, density)
  const level: DayColumnLevel = availableWidth > 0 ? dayColumnLevel(dayWidth) : 'full'
  const fits = availableWidth === 0 || daysFitWithoutScroll(availableWidth, days, density)

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
  // Trips without times sit in ONE lane of equal height in every column (and in the axis), so
  // the time grids of all days — and the hour labels — stay aligned.
  const maxUnscheduled = Math.max(0, ...dates.map((date) => (byDate.get(date) ?? []).filter((trip) => blockPosition(trip.plannedStart, trip.plannedEnd) === null).length))
  const laneStyle = { '--pc-lane-rows': maxUnscheduled } as CSSProperties

  return (
    <div className="pc-board-wrap">
      {!fits && days === 7 && !sidesCollapsed && onCollapseSides && (
        <p className="pc-board-hint" role="status">
          <span>{t('planningCenter.board.weekHint')}</span>
          <button type="button" className="pc-link" onClick={onCollapseSides}>{t('planningCenter.board.weekHintAction')}</button>
        </p>
      )}
      <section
        className={`pc-board pc-board-${days}`}
        data-level={level}
        data-scrolls={fits ? undefined : 'true'}
        aria-label={t('planningCenter.board.label')}
        style={laneStyle}
      >
        <div className="pc-board-axis" aria-hidden="true">
          <div className="pc-day-header pc-axis-head">&nbsp;</div>
          {maxUnscheduled > 0 && <div className="pc-day-unscheduled pc-axis-lane">{t('planningCenter.board.noTime')}</div>}
          <div className="pc-axis-hours">
            {gridHours().map((hour) => (
              <span key={hour} style={{ top: `${((hour - DAY_START_HOUR) / (DAY_END_HOUR - DAY_START_HOUR)) * 100}%` }}>
                {String(hour).padStart(2, '0')}:00
              </span>
            ))}
          </div>
        </div>
        <div className="pc-board-days" ref={daysRef} style={{ '--pc-day-min': `${MIN_DAY_WIDTH[density]}px` } as CSSProperties}>
          {dates.map((date) => {
            const dayTrips = byDate.get(date) ?? []
            const unscheduled = dayTrips.filter((trip) => blockPosition(trip.plannedStart, trip.plannedEnd) === null)
            const scheduled = dayTrips.filter((trip) => blockPosition(trip.plannedStart, trip.plannedEnd) !== null)
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
                <header className="pc-day-header" title={formatDayHeading(date)}>
                  {level === 'narrow' ? formatDayHeadingShort(date) : formatDayHeading(date)}
                  {dayTrips.length > 0 && <span className="pc-day-count">{dayTrips.length}</span>}
                </header>
                {maxUnscheduled > 0 && (
                  <div className="pc-day-unscheduled">
                    {unscheduled.map((trip) => (
                      <TripBlock
                        key={trip.id}
                        trip={trip}
                        level={level}
                        inline
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
                          level={level}
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
        {isLoading && <p className="pc-muted pc-board-loading">{t('planningCenter.board.loading')}</p>}
      </section>
    </div>
  )
}

interface TripBlockProps {
  trip: PlanningBoardTrip
  level: DayColumnLevel
  /** A trip without times: one line in the shared lane (number · driver · ⚠). */
  inline?: boolean
  floating: boolean
  selected: boolean
  dragOver: boolean
  setDragOver: (key: string | null) => void
  onSelect: (trip: PlanningBoardTrip) => void
  onDrop: (trip: PlanningBoardTrip, payload: DragPayload) => void
  allowDrop: (event: DragEvent) => void
  readPayload: (event: DragEvent) => DragPayload | null
}

/**
 * One trip card. What it shows follows the column width: everything at `full`, no vehicle/route
 * at `medium`, only number · time · driver at `narrow`. The status is always readable from the
 * badge or, when there is no room for one, from the card's edge colour; conflicts always keep
 * their chip, and the chip explains itself on hover/focus (B09).
 */
function TripBlock({
  trip, level, inline = false, floating, selected, dragOver, setDragOver, onSelect, onDrop, allowDrop, readPayload,
}: TripBlockProps) {
  const { t } = useLocale()
  const timeRange = formatTimeRange(trip.plannedStart, trip.plannedEnd)
  const overloaded =
    (trip.capacityWeightKg !== null && trip.totalWeightKg !== null && trip.totalWeightKg > trip.capacityWeightKg) ||
    (trip.capacityVolumeM3 !== null && trip.totalVolumeM3 !== null && trip.totalVolumeM3 > trip.capacityVolumeM3)
  const conflictCount = trip.blockingConflictCount + trip.warningConflictCount
  const preview = trip.conflictPreview ?? []
  const reasons = preview.length > 0
    ? [...preview, ...(conflictCount > preview.length ? [t('planningCenter.board.moreConflicts', { count: conflictCount - preview.length })] : [])]
    : [t('planningCenter.board.conflictsSelectHint')]
  const statusLabel = t(TRIP_STATUS_LABELS[trip.status])

  return (
    <article
      className={[
        'pc-trip',
        `pc-trip-status-${trip.status.toLowerCase()}`,
        floating ? 'pc-trip-floating' : '',
        inline ? 'pc-trip-inline' : '',
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
      aria-label={t('planningCenter.board.tripAriaLabel', { tripNumber: trip.tripNumber, status: statusLabel })}
      title={level === 'full' && !inline ? undefined : [trip.tripNumber, statusLabel, timeRange, trip.driverName, trip.vehicleNumber, trip.routeSummary].filter(Boolean).join(' · ')}
    >
      {inline ? (
        <div className="pc-trip-head">
          <strong>{trip.tripNumber}</strong>
          <span className="pc-trip-inline-driver">{trip.driverName ?? t('planningCenter.board.noDriver')}</span>
          {conflictCount > 0 && (
            <ConflictHint
              tone={trip.blockingConflictCount > 0 ? 'danger' : 'warning'}
              label={`⚠ ${conflictCount}`}
              title={t('planningCenter.board.conflictsTitle')}
              reasons={reasons}
              ariaLabel={t('planningCenter.board.conflictCount', { count: conflictCount })}
              className="pc-trip-flag"
            />
          )}
        </div>
      ) : (<>
      <div className="pc-trip-head">
        <strong>{trip.tripNumber}</strong>
        {level === 'full' && <Badge tone={TRIP_STATUS_TONE[trip.status]}>{statusLabel}</Badge>}
        {level !== 'full' && conflictCount > 0 && (
          <ConflictHint
            tone={trip.blockingConflictCount > 0 ? 'danger' : 'warning'}
            label={`⚠ ${conflictCount}`}
            title={t('planningCenter.board.conflictsTitle')}
            reasons={reasons}
            ariaLabel={t('planningCenter.board.conflictCount', { count: conflictCount })}
            className="pc-trip-flag"
          />
        )}
      </div>
      {timeRange && <p className="pc-trip-time">{timeRange}</p>}
      <p className="pc-trip-line">{trip.driverName ?? t('planningCenter.board.noDriver')}</p>
      {level === 'full' && (
        <p className="pc-trip-line">
          {trip.vehicleNumber ?? t('planningCenter.board.noVehicle')}
          {trip.trailerNumber ? ` + ${trip.trailerNumber}` : ''}
        </p>
      )}
      {level === 'full' && trip.routeSummary && <p className="pc-trip-line pc-trip-route">{trip.routeSummary}</p>}
      {level !== 'narrow' && (
        <p className="pc-trip-line pc-trip-meta">
          <span>{t('planningCenter.board.orders', { count: trip.orderCount })}</span>
          {level === 'full' && <span>{t('planningCenter.board.stops', { count: trip.stopCount })}</span>}
          {trip.totalWeightKg !== null && (
            <span className={overloaded ? 'pc-overloaded' : undefined}>
              {level === 'full' && trip.capacityWeightKg !== null
                ? `${formatWeightShort(trip.totalWeightKg)} / ${formatWeightShort(trip.capacityWeightKg)}`
                : formatWeightShort(trip.totalWeightKg)}
            </span>
          )}
        </p>
      )}
      {level === 'full' && conflictCount > 0 && (
        <div className="pc-trip-conflicts">
          {trip.blockingConflictCount > 0 && (
            <ConflictHint
              tone="danger"
              label={t('planningCenter.board.blocking', { count: trip.blockingConflictCount })}
              title={t('planningCenter.board.conflictsTitle')}
              reasons={reasons}
              ariaLabel={t('planningCenter.board.conflictCount', { count: conflictCount })}
            />
          )}
          {trip.warningConflictCount > 0 && (
            <ConflictHint
              tone="warning"
              label={t('planningCenter.board.warnings', { count: trip.warningConflictCount })}
              title={t('planningCenter.board.conflictsTitle')}
              reasons={reasons}
              ariaLabel={t('planningCenter.board.conflictCount', { count: conflictCount })}
            />
          )}
        </div>
      )}
      </>)}
    </article>
  )
}
