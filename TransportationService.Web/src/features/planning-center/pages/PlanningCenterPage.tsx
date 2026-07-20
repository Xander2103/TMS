import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ApiError } from '../../../api/apiClient'
import { describeApiError } from '../../../api/problemDetails'
import {
  deleteResourceLink, listResourceLinks, touchResourceLink, type ResourceLink,
} from '../../../api/resourceLinksApi'
import { useToast } from '../../../components/ui/toastContext'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { getTrip } from '../../planning/api/planningApi'
import type { PlanningConflict, TripDetail } from '../../planning/types'
import {
  assignDriver, assignOrders, assignTrailer, assignVehicle, changeTripStatusVersioned,
  createTripFromOrders, getBoard, getResources, getUnplannedOrders, removeOrder, reorderOrders,
  rescheduleTrip,
} from '../api/planningCenterApi'
import { BoardTimeline } from '../components/BoardTimeline'
import { ConflictDialog } from '../components/ConflictDialog'
import { ResourcesPanel } from '../components/ResourcesPanel'
import { TripInspector } from '../components/TripInspector'
import { UnplannedPanel, type UnplannedFilters } from '../components/UnplannedPanel'
import {
  type DragPayload, type PlanningBoard, type PlanningBoardTrip, type PlanningRejection,
  type PlanningResources, type UnplannedOrder,
} from '../types'
import { shiftAnchor, toIsoDate, viewDates, type BoardViewDays } from '../utils/boardMath'
import './planning-center.css'

const FILTER_STORAGE_KEY = 'ts.planningCenter.filters'

interface MutationOptions {
  success?: string
  reloadOrders?: boolean
  reloadResources?: boolean
  /** Retry with override+reason when the backend reports blocking conflicts. */
  overrideRetry?: (reason: string) => Promise<unknown>
}

interface ConflictState {
  message: string
  conflicts: PlanningConflict[]
  /** Builds the override retry call; executed through the same mutation pipeline. */
  retryFactory: ((reason: string) => Promise<unknown>) | null
  retryOptions: MutationOptions
}

const EMPTY_FILTERS: UnplannedFilters = {
  search: '', status: '', priority: '', fromDate: '', toDate: '', onlyWithAttention: false,
}

function loadStoredFilters(): UnplannedFilters {
  try {
    const raw = localStorage.getItem(FILTER_STORAGE_KEY)
    return raw ? { ...EMPTY_FILTERS, ...(JSON.parse(raw) as Partial<UnplannedFilters>) } : EMPTY_FILTERS
  } catch {
    return EMPTY_FILTERS
  }
}

/** The dispatcher workspace: unplanned pool, drag-and-drop board and resource panel. */
export function PlanningCenterPage() {
  const { showError, showSuccess } = useToast()

  const [anchor, setAnchor] = useState(() => toIsoDate(new Date()))
  const [days, setDays] = useState<BoardViewDays>(3)
  const dates = useMemo(() => viewDates(anchor, days), [anchor, days])
  const rangeFrom = dates[0]
  const rangeTo = dates[dates.length - 1]

  const [board, setBoard] = useState<PlanningBoard | null>(null)
  const [boardToken, setBoardToken] = useState(0)
  const [boardLoadedKey, setBoardLoadedKey] = useState('')
  const boardKey = JSON.stringify({ rangeFrom, rangeTo, boardToken })

  const [filters, setFilters] = useState<UnplannedFilters>(loadStoredFilters)
  const [unplannedPage, setUnplannedPage] = useState(1)
  const [unplanned, setUnplanned] = useState<{ items: UnplannedOrder[]; totalCount: number; pageSize: number }>({
    items: [], totalCount: 0, pageSize: 50,
  })
  const [unplannedToken, setUnplannedToken] = useState(0)
  const [unplannedLoadedKey, setUnplannedLoadedKey] = useState('')
  const unplannedKey = JSON.stringify({ filters, unplannedPage, unplannedToken })

  const [resources, setResources] = useState<PlanningResources | null>(null)
  const [resourcesToken, setResourcesToken] = useState(0)
  const [resourcesLoadedKey, setResourcesLoadedKey] = useState('')
  const resourcesKey = JSON.stringify({ rangeFrom, rangeTo, resourcesToken })

  const [selectedTripId, setSelectedTripId] = useState<string | null>(null)
  const [tripDetail, setTripDetail] = useState<TripDetail | null>(null)
  // Derived: the shown detail lags the selection until the fetch below catches up.
  const tripDetailLoading = selectedTripId !== null && tripDetail?.id !== selectedTripId

  const [selectedOrderId, setSelectedOrderId] = useState<string | null>(null)
  const [planPickerOrder, setPlanPickerOrder] = useState<UnplannedOrder | null>(null)

  const [pinned, setPinned] = useState<ResourceLink[]>([])
  const [busy, setBusy] = useState(false)
  const [conflictState, setConflictState] = useState<ConflictState | null>(null)

  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  useEffect(() => {
    localStorage.setItem(FILTER_STORAGE_KEY, JSON.stringify(filters))
  }, [filters])

  // --- Zone loads (request-key idiom; each zone refreshes independently) ---

  useEffect(() => {
    let cancelled = false
    getBoard(rangeFrom, rangeTo)
      .then((data) => {
        if (!cancelled) {
          setBoard(data)
          setBoardLoadedKey(boardKey)
        }
      })
      .catch((error: unknown) => showError(describeApiError(error, 'Planbord laden mislukt.').message))
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [boardKey])

  useEffect(() => {
    let cancelled = false
    const handle = setTimeout(() => {
      getUnplannedOrders({
        search: filters.search || undefined,
        status: filters.status || undefined,
        priority: filters.priority || undefined,
        fromDate: filters.fromDate || undefined,
        toDate: filters.toDate || undefined,
        onlyWithAttention: filters.onlyWithAttention || undefined,
        page: unplannedPage,
      })
        .then((data) => {
          if (!cancelled) {
            setUnplanned({ items: data.items, totalCount: data.totalCount, pageSize: data.pageSize })
            setUnplannedLoadedKey(unplannedKey)
          }
        })
        .catch((error: unknown) => showError(describeApiError(error, 'Ongeplande opdrachten laden mislukt.').message))
    }, 300)
    return () => {
      cancelled = true
      clearTimeout(handle)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [unplannedKey])

  useEffect(() => {
    let cancelled = false
    getResources(rangeFrom, rangeTo)
      .then((data) => {
        if (!cancelled) {
          setResources(data)
          setResourcesLoadedKey(resourcesKey)
        }
      })
      .catch((error: unknown) => showError(describeApiError(error, 'Middelen laden mislukt.').message))
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [resourcesKey])

  useEffect(() => {
    listResourceLinks('Pinned')
      .then((links) => {
        if (mounted.current) setPinned(links)
      })
      .catch(() => undefined)
  }, [])

  // Inspector detail follows the selection and every board refresh. Clearing on deselect
  // happens in the close handler (not here) to avoid synchronous setState in an effect.
  useEffect(() => {
    if (!selectedTripId) {
      return
    }
    let cancelled = false
    getTrip(selectedTripId)
      .then((detail) => {
        if (!cancelled) setTripDetail(detail)
      })
      .catch(() => {
        if (!cancelled) setTripDetail(null)
      })
    return () => {
      cancelled = true
    }
  }, [selectedTripId, boardLoadedKey])

  const reloadBoard = useCallback(() => setBoardToken((t) => t + 1), [])
  const reloadUnplanned = useCallback(() => setUnplannedToken((t) => t + 1), [])
  const reloadResources = useCallback(() => setResourcesToken((t) => t + 1), [])

  // --- Mutation pipeline: every action persists server-side; rejections restore state by
  // refetching only the affected zones and show the exact conflicts. ---

  const runMutation = useCallback(async (
    action: () => Promise<unknown>,
    options: MutationOptions = {},
  ) => {
    setBusy(true)
    try {
      await action()
      if (options.success) showSuccess(options.success)
      reloadBoard()
      if (options.reloadOrders) reloadUnplanned()
      if (options.reloadResources) reloadResources()
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        const body = (error.body ?? {}) as PlanningRejection
        if (body.staleVersion) {
          showError('De rit is intussen door iemand anders gewijzigd; het bord is ververst.')
          reloadBoard()
        } else if (body.conflicts && body.conflicts.length > 0) {
          setConflictState({
            message: body.message ?? 'De actie is geweigerd door conflicten.',
            conflicts: body.conflicts,
            retryFactory: options.overrideRetry ?? null,
            retryOptions: { ...options, overrideRetry: undefined },
          })
        } else {
          showError(describeApiError(error, 'De actie is geweigerd.').message)
          reloadBoard()
        }
      } else {
        showError(describeApiError(error, 'De actie is mislukt.').message)
      }
    } finally {
      setBusy(false)
    }
  }, [reloadBoard, reloadResources, reloadUnplanned, showError, showSuccess])

  // --- Drag-and-drop handlers (all persist through backend commands) ---

  const handleDropOnTrip = useCallback((trip: PlanningBoardTrip, payload: DragPayload) => {
    if (trip.status !== 'Draft' && trip.status !== 'Planned') {
      showError('Alleen concept- of geplande ritten kunnen worden aangepast.')
      return
    }
    switch (payload.kind) {
      case 'order':
        void runMutation(
          () => assignOrders(trip.id, [payload.id], trip.version),
          { success: `${payload.label} toegevoegd aan ${trip.tripNumber}.`, reloadOrders: true },
        )
        break
      case 'driver':
        void runMutation(
          () => assignDriver(trip.id, payload.id, trip.version),
          {
            success: `${payload.label} toegewezen aan ${trip.tripNumber}.`,
            reloadResources: true,
            overrideRetry: (reason) => assignDriver(trip.id, payload.id, trip.version, true, reason),
          },
        )
        break
      case 'vehicle':
        void runMutation(
          () => assignVehicle(trip.id, payload.id, trip.version),
          {
            success: `${payload.label} toegewezen aan ${trip.tripNumber}.`,
            reloadResources: true,
            overrideRetry: (reason) => assignVehicle(trip.id, payload.id, trip.version, true, reason),
          },
        )
        break
      case 'trailer':
        void runMutation(
          () => assignTrailer(trip.id, payload.id, trip.version),
          {
            success: `${payload.label} toegewezen aan ${trip.tripNumber}.`,
            reloadResources: true,
            overrideRetry: (reason) => assignTrailer(trip.id, payload.id, trip.version, true, reason),
          },
        )
        break
      case 'trip':
        // Dropping a trip on a trip has no meaning; ignore.
        break
    }
  }, [runMutation, showError])

  const handleDropOnDay = useCallback((dateIso: string, payload: DragPayload) => {
    if (payload.kind === 'order') {
      void runMutation(
        () => createTripFromOrders(dateIso, [payload.id]),
        { success: `Nieuwe rit aangemaakt met ${payload.label}.`, reloadOrders: true },
      )
    } else if (payload.kind === 'trip') {
      const trip = board?.trips.find((t) => t.id === payload.id)
      if (!trip || trip.tripDate === dateIso) return
      void runMutation(
        () => rescheduleTrip(payload.id, dateIso, null, null, payload.version),
        {
          success: `${payload.label} verplaatst naar ${new Date(`${dateIso}T00:00:00`).toLocaleDateString('nl-BE')}.`,
          reloadResources: true,
          overrideRetry: (reason) => rescheduleTrip(payload.id, dateIso, null, null, payload.version, true, reason),
        },
      )
    }
  }, [board, runMutation])

  const handleTogglePin = useCallback((entityType: 'Driver' | 'Vehicle' | 'Trailer', id: string, label: string) => {
    const existing = pinned.find((link) => link.entityType === entityType && link.entityId === id)
    if (existing) {
      setPinned((links) => links.filter((link) => link.id !== existing.id))
      void deleteResourceLink(existing.id).catch(() => undefined)
    } else {
      void touchResourceLink({
        kind: 'Pinned', entityType, entityId: id, label,
        route: entityType === 'Driver' ? `/drivers/${id}` : entityType === 'Vehicle' ? `/vehicles/${id}` : `/trailers/${id}`,
      })
        .then((link) => {
          if (mounted.current) setPinned((links) => [...links, link])
        })
        .catch(() => undefined)
    }
  }, [pinned])

  const pinnedIds = useMemo(() => new Set(pinned.map((link) => link.entityId)), [pinned])
  const plannableTrips = useMemo(
    () => (board?.trips ?? []).filter((t) => t.status === 'Draft' || t.status === 'Planned'),
    [board],
  )

  return (
    <div className="pc-page">
      <header className="pc-toolbar">
        <div className="pc-toolbar-left">
          <h1>Planbord</h1>
        </div>
        <div className="pc-toolbar-range" role="group" aria-label="Periode">
          <Button variant="secondary" onClick={() => setAnchor(shiftAnchor(anchor, days, -1))}>‹</Button>
          <input type="date" value={anchor} onChange={(event) => setAnchor(event.target.value || anchor)} aria-label="Startdatum" />
          <Button variant="secondary" onClick={() => setAnchor(toIsoDate(new Date()))}>Vandaag</Button>
          <Button variant="secondary" onClick={() => setAnchor(shiftAnchor(anchor, days, 1))}>›</Button>
          <div className="pc-view-switch" role="group" aria-label="Weergave">
            {([1, 3, 7] as BoardViewDays[]).map((option) => (
              <button
                key={option}
                type="button"
                className={days === option ? 'pc-tab-active' : ''}
                onClick={() => setDays(option)}
              >
                {option === 1 ? 'Dag' : option === 3 ? '3 dagen' : 'Week'}
              </button>
            ))}
          </div>
        </div>
      </header>

      <div className="pc-layout">
        <UnplannedPanel
          orders={unplanned.items}
          totalCount={unplanned.totalCount}
          page={unplannedPage}
          pageSize={unplanned.pageSize}
          isLoading={unplannedLoadedKey !== unplannedKey}
          filters={filters}
          selectedOrderId={selectedOrderId}
          onFiltersChange={(next) => {
            setFilters(next)
            setUnplannedPage(1)
          }}
          onPageChange={setUnplannedPage}
          onSelect={(order) => setSelectedOrderId(order.id)}
          onPlanRequest={(order) => setPlanPickerOrder(order)}
        />

        <BoardTimeline
          dates={dates}
          days={days}
          trips={board?.trips ?? []}
          isLoading={boardLoadedKey !== boardKey}
          selectedTripId={selectedTripId}
          onSelectTrip={(trip) => setSelectedTripId(trip.id)}
          onDropOnTrip={handleDropOnTrip}
          onDropOnDay={handleDropOnDay}
        />

        <div className="pc-right">
          {selectedTripId ? (
            <TripInspector
              trip={tripDetail?.id === selectedTripId ? tripDetail : null}
              isLoading={tripDetailLoading}
              busy={busy}
              onClose={() => {
                setSelectedTripId(null)
                setTripDetail(null)
              }}
              onPlan={(trip) => void runMutation(
                () => changeTripStatusVersioned(trip.id, 'Planned', trip.version),
                {
                  success: `${trip.tripNumber} ingepland.`,
                  reloadOrders: true,
                  reloadResources: true,
                  overrideRetry: (reason) => changeTripStatusVersioned(trip.id, 'Planned', trip.version, true, reason),
                },
              )}
              onRevertToDraft={(trip) => void runMutation(
                () => changeTripStatusVersioned(trip.id, 'Draft', trip.version),
                { success: `${trip.tripNumber} terug naar concept.`, reloadOrders: true, reloadResources: true },
              )}
              onRemoveOrder={(trip, orderId) => void runMutation(
                () => removeOrder(trip.id, orderId, trip.version),
                { success: 'Opdracht van de rit gehaald.', reloadOrders: true },
              )}
              onMoveOrder={(trip, orderId, direction) => {
                const ids = trip.orders.map((o) => o.transportOrderId)
                const index = ids.indexOf(orderId)
                const target = index + direction
                if (index < 0 || target < 0 || target >= ids.length) return
                ;[ids[index], ids[target]] = [ids[target], ids[index]]
                void runMutation(() => reorderOrders(trip.id, ids, trip.version), {})
              }}
              onClearResource={(trip, slot) => {
                const api = slot === 'driver' ? assignDriver : slot === 'vehicle' ? assignVehicle : assignTrailer
                void runMutation(
                  () => api(trip.id, null, trip.version),
                  {
                    reloadResources: true,
                    overrideRetry: (reason) => api(trip.id, null, trip.version, true, reason),
                  },
                )
              }}
            />
          ) : (
            <ResourcesPanel
              resources={resources}
              isLoading={resourcesLoadedKey !== resourcesKey}
              pinnedIds={pinnedIds}
              onTogglePin={handleTogglePin}
            />
          )}
        </div>
      </div>

      {planPickerOrder && (
        <Modal title={`${planPickerOrder.orderNumber} inplannen`} onClose={() => setPlanPickerOrder(null)} busy={busy}>
          <p>Kies een rit of maak een nieuwe rit aan (toegankelijk alternatief voor slepen).</p>
          <div className="pc-picker-list">
            {plannableTrips.map((trip) => (
              <button
                key={trip.id}
                type="button"
                className="pc-picker-item"
                disabled={busy}
                onClick={() => {
                  const order = planPickerOrder
                  setPlanPickerOrder(null)
                  void runMutation(
                    () => assignOrders(trip.id, [order.id], trip.version),
                    { success: `${order.orderNumber} toegevoegd aan ${trip.tripNumber}.`, reloadOrders: true },
                  )
                }}
              >
                <strong>{trip.tripNumber}</strong> · {new Date(`${trip.tripDate}T00:00:00`).toLocaleDateString('nl-BE')}
                {trip.driverName ? ` · ${trip.driverName}` : ''} · {trip.orderCount} opdr.
              </button>
            ))}
            {plannableTrips.length === 0 && <p className="pc-muted">Geen open ritten in deze periode.</p>}
          </div>
          <div className="pc-picker-new">
            <label htmlFor="pc-picker-date">Nieuwe rit op</label>
            <input id="pc-picker-date" type="date" defaultValue={anchor} onChange={(event) => setAnchor(event.target.value || anchor)} />
            <Button
              disabled={busy}
              onClick={() => {
                const order = planPickerOrder
                setPlanPickerOrder(null)
                void runMutation(
                  () => createTripFromOrders(anchor, [order.id]),
                  { success: `Nieuwe rit aangemaakt met ${order.orderNumber}.`, reloadOrders: true },
                )
              }}
            >
              Nieuwe rit aanmaken
            </Button>
          </div>
        </Modal>
      )}

      {conflictState && (
        <ConflictDialog
          message={conflictState.message}
          conflicts={conflictState.conflicts}
          busy={busy}
          onClose={() => setConflictState(null)}
          onOverride={conflictState.retryFactory
            ? (reason) => {
                const factory = conflictState.retryFactory!
                const options = conflictState.retryOptions
                setConflictState(null)
                void runMutation(() => factory(reason), options)
              }
            : undefined}
        />
      )}
    </div>
  )
}
