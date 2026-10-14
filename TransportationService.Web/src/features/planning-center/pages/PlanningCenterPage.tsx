import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ApiError } from '../../../api/apiClient'
import { describeApiError } from '../../../api/problemDetails'
import {
  deleteResourceLink, listResourceLinks, touchResourceLink, type ResourceLink,
} from '../../../api/resourceLinksApi'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { getTrip } from '../../planning/api/planningApi'
import { changeOrderPriority } from '../../transport-orders/api/transportOrdersApi'
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
import { PanelRail } from '../components/PanelRail'
import { UnplannedPanel, type UnplannedFilters } from '../components/UnplannedPanel'
import {
  type DragPayload, type PlanningBoard, type PlanningBoardTrip, type PlanningRejection,
  type PlanningResources, type UnplannedOrder,
} from '../types'
import { loadStoredLayout, storeLayout, type BoardLayout } from '../utils/boardLayout'
import { shiftAnchor, startOfWeek, toIsoDate, viewDates, type BoardDensity, type BoardViewDays } from '../utils/boardMath'
import { formatDate } from '../../../utils/dates'
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
  search: '', status: '', priority: '', fromDate: '', toDate: '', onlyWithAttention: false, includeDrafts: false,
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
  const { t } = useLocale()

  const [layout, setLayout] = useState<BoardLayout>(loadStoredLayout)
  const { density, leftCollapsed, rightCollapsed, days } = layout
  const updateLayout = useCallback((patch: Partial<BoardLayout>) => setLayout((current) => ({ ...current, ...patch })), [])
  useEffect(() => {
    storeLayout(layout)
  }, [layout])

  const [anchor, setAnchor] = useState(() => toIsoDate(new Date()))
  // The Week view always shows Monday … Sunday, whatever date the planner picked.
  const effectiveAnchor = days === 7 ? startOfWeek(anchor) : anchor
  const dates = useMemo(() => viewDates(effectiveAnchor, days), [effectiveAnchor, days])
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
      .catch((error: unknown) => showError(describeApiError(error, t('planningCenter.page.boardLoadFailed')).message))
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
        includeDrafts: filters.includeDrafts || undefined,
        page: unplannedPage,
      })
        .then((data) => {
          if (!cancelled) {
            setUnplanned({ items: data.items, totalCount: data.totalCount, pageSize: data.pageSize })
            setUnplannedLoadedKey(unplannedKey)
          }
        })
        .catch((error: unknown) => showError(describeApiError(error, t('planningCenter.page.unplannedLoadFailed')).message))
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
      .catch((error: unknown) => showError(describeApiError(error, t('planningCenter.page.resourcesLoadFailed')).message))
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
          showError(t('planningCenter.page.staleVersion'))
          reloadBoard()
        } else if (body.conflicts && body.conflicts.length > 0) {
          setConflictState({
            message: body.message ?? t('planningCenter.page.rejectedByConflicts'),
            conflicts: body.conflicts,
            retryFactory: options.overrideRetry ?? null,
            retryOptions: { ...options, overrideRetry: undefined },
          })
        } else {
          showError(describeApiError(error, t('planningCenter.page.rejected')).message)
          reloadBoard()
        }
      } else {
        showError(describeApiError(error, t('planningCenter.page.actionFailed')).message)
      }
    } finally {
      setBusy(false)
    }
  }, [reloadBoard, reloadResources, reloadUnplanned, showError, showSuccess, t])

  // --- Drag-and-drop handlers (all persist through backend commands) ---

  const handleDropOnTrip = useCallback((trip: PlanningBoardTrip, payload: DragPayload) => {
    if (trip.status !== 'Draft' && trip.status !== 'Planned') {
      showError(t('planningCenter.page.onlyDraftOrPlanned'))
      return
    }
    switch (payload.kind) {
      case 'order':
        void runMutation(
          () => assignOrders(trip.id, [payload.id], trip.version),
          { success: t('planningCenter.page.orderAdded', { label: payload.label, tripNumber: trip.tripNumber }), reloadOrders: true },
        )
        break
      case 'driver':
        void runMutation(
          () => assignDriver(trip.id, payload.id, trip.version),
          {
            success: t('planningCenter.page.resourceAssigned', { label: payload.label, tripNumber: trip.tripNumber }),
            reloadResources: true,
            overrideRetry: (reason) => assignDriver(trip.id, payload.id, trip.version, true, reason),
          },
        )
        break
      case 'vehicle':
        void runMutation(
          () => assignVehicle(trip.id, payload.id, trip.version),
          {
            success: t('planningCenter.page.resourceAssigned', { label: payload.label, tripNumber: trip.tripNumber }),
            reloadResources: true,
            overrideRetry: (reason) => assignVehicle(trip.id, payload.id, trip.version, true, reason),
          },
        )
        break
      case 'trailer':
        void runMutation(
          () => assignTrailer(trip.id, payload.id, trip.version),
          {
            success: t('planningCenter.page.resourceAssigned', { label: payload.label, tripNumber: trip.tripNumber }),
            reloadResources: true,
            overrideRetry: (reason) => assignTrailer(trip.id, payload.id, trip.version, true, reason),
          },
        )
        break
      case 'trip':
        // Dropping a trip on a trip has no meaning; ignore.
        break
    }
  }, [runMutation, showError, t])

  const handleDropOnDay = useCallback((dateIso: string, payload: DragPayload) => {
    if (payload.kind === 'order') {
      void runMutation(
        () => createTripFromOrders(dateIso, [payload.id]),
        { success: t('planningCenter.page.newTripWith', { label: payload.label }), reloadOrders: true },
      )
    } else if (payload.kind === 'trip') {
      const trip = board?.trips.find((entry) => entry.id === payload.id)
      if (!trip || trip.tripDate === dateIso) return
      void runMutation(
        () => rescheduleTrip(payload.id, dateIso, null, null, payload.version),
        {
          success: t('planningCenter.page.tripMoved', { label: payload.label, date: formatDate(dateIso) }),
          reloadResources: true,
          overrideRetry: (reason) => rescheduleTrip(payload.id, dateIso, null, null, payload.version, true, reason),
        },
      )
    }
  }, [board, runMutation, t])

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

  const sidesCollapsed = leftCollapsed && rightCollapsed

  return (
    <div className="pc-page" data-density={density}>
      <header className="pc-toolbar">
        <div className="pc-toolbar-left">
          <h2 className="pc-title">{t('planningCenter.page.title')}</h2>
        </div>
        <div className="pc-toolbar-range" role="group" aria-label={t('planningCenter.page.periodLabel')}>
          <Button variant="secondary" onClick={() => setAnchor(shiftAnchor(effectiveAnchor, days, -1))} aria-label={t('planningCenter.page.previousPeriod')}>‹</Button>
          <input type="date" value={anchor} onChange={(event) => setAnchor(event.target.value || anchor)} aria-label={t('planningCenter.page.startDateLabel')} />
          <Button variant="secondary" onClick={() => setAnchor(toIsoDate(new Date()))}>{t('planningCenter.page.today')}</Button>
          <Button variant="secondary" onClick={() => setAnchor(shiftAnchor(effectiveAnchor, days, 1))} aria-label={t('planningCenter.page.nextPeriod')}>›</Button>
          <div className="pc-view-switch" role="group" aria-label={t('planningCenter.page.viewLabel')}>
            {([1, 3, 7] as BoardViewDays[]).map((option) => (
              <button
                key={option}
                type="button"
                className={days === option ? 'pc-tab-active' : ''}
                aria-pressed={days === option}
                onClick={() => updateLayout({ days: option })}
              >
                {option === 1
                  ? t('planningCenter.page.viewDay')
                  : option === 3
                    ? t('planningCenter.page.viewThreeDays')
                    : t('planningCenter.page.viewWeek')}
              </button>
            ))}
          </div>
          <div className="pc-view-switch" role="group" aria-label={t('planningCenter.page.densityLabel')}>
            {(['compact', 'normal'] as BoardDensity[]).map((option) => (
              <button
                key={option}
                type="button"
                className={density === option ? 'pc-tab-active' : ''}
                aria-pressed={density === option}
                onClick={() => updateLayout({ density: option })}
              >
                {option === 'compact' ? t('planningCenter.page.densityCompact') : t('planningCenter.page.densityNormal')}
              </button>
            ))}
          </div>
        </div>
      </header>

      <div className="pc-layout" data-left={leftCollapsed ? 'collapsed' : 'open'} data-right={rightCollapsed && !selectedTripId ? 'collapsed' : 'open'}>
        {leftCollapsed ? (
          <PanelRail
            side="left"
            label={t('planningCenter.unplanned.title')}
            count={unplanned.totalCount}
            expandLabel={t('planningCenter.page.expandLeft')}
            onExpand={() => updateLayout({ leftCollapsed: false })}
          />
        ) : (
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
            onPriorityChange={async (order, priority) => {
              try {
                await changeOrderPriority(order.id, priority)
                reloadUnplanned()
              } catch (error) {
                showError(describeApiError(error, t('planningCenter.page.priorityChangeFailed')).message)
                throw error
              }
            }}
            onCollapse={() => updateLayout({ leftCollapsed: true })}
          />
        )}

        <BoardTimeline
          dates={dates}
          days={days}
          density={density}
          trips={board?.trips ?? []}
          isLoading={boardLoadedKey !== boardKey}
          selectedTripId={selectedTripId}
          sidesCollapsed={sidesCollapsed}
          onSelectTrip={(trip) => setSelectedTripId(trip.id)}
          onDropOnTrip={handleDropOnTrip}
          onDropOnDay={handleDropOnDay}
          onCollapseSides={() => updateLayout({ leftCollapsed: true, rightCollapsed: true })}
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
                  success: t('planningCenter.page.tripPlanned', { tripNumber: trip.tripNumber }),
                  reloadOrders: true,
                  reloadResources: true,
                  overrideRetry: (reason) => changeTripStatusVersioned(trip.id, 'Planned', trip.version, true, reason),
                },
              )}
              onRevertToDraft={(trip) => void runMutation(
                () => changeTripStatusVersioned(trip.id, 'Draft', trip.version),
                { success: t('planningCenter.page.tripBackToDraft', { tripNumber: trip.tripNumber }), reloadOrders: true, reloadResources: true },
              )}
              onRemoveOrder={(trip, orderId) => void runMutation(
                () => removeOrder(trip.id, orderId, trip.version),
                { success: t('planningCenter.page.orderRemoved'), reloadOrders: true },
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
          ) : rightCollapsed ? (
            <PanelRail
              side="right"
              label={t('planningCenter.resources.title')}
              expandLabel={t('planningCenter.page.expandRight')}
              onExpand={() => updateLayout({ rightCollapsed: false })}
            />
          ) : (
            <ResourcesPanel
              resources={resources}
              isLoading={resourcesLoadedKey !== resourcesKey}
              pinnedIds={pinnedIds}
              onTogglePin={handleTogglePin}
              onCollapse={() => updateLayout({ rightCollapsed: true })}
            />
          )}
        </div>
      </div>

      {planPickerOrder && (
        <Modal title={t('planningCenter.page.pickerTitle', { orderNumber: planPickerOrder.orderNumber })} onClose={() => setPlanPickerOrder(null)} busy={busy}>
          <p>{t('planningCenter.page.pickerIntro')}</p>
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
                    { success: t('planningCenter.page.orderAdded', { label: order.orderNumber, tripNumber: trip.tripNumber }), reloadOrders: true },
                  )
                }}
              >
                <strong>{trip.tripNumber}</strong> · {formatDate(trip.tripDate)}
                {trip.driverName ? ` · ${trip.driverName}` : ''} · {t('planningCenter.page.pickerOrders', { count: trip.orderCount })}
              </button>
            ))}
            {plannableTrips.length === 0 && <p className="pc-muted">{t('planningCenter.page.pickerEmpty')}</p>}
          </div>
          <div className="pc-picker-new">
            <label htmlFor="pc-picker-date">{t('planningCenter.page.pickerNewTripOn')}</label>
            <input id="pc-picker-date" type="date" defaultValue={effectiveAnchor} onChange={(event) => setAnchor(event.target.value || anchor)} />
            <Button
              disabled={busy}
              onClick={() => {
                const order = planPickerOrder
                setPlanPickerOrder(null)
                void runMutation(
                  () => createTripFromOrders(effectiveAnchor, [order.id]),
                  { success: t('planningCenter.page.newTripWith', { label: order.orderNumber }), reloadOrders: true },
                )
              }}
            >
              {t('planningCenter.page.pickerCreateTrip')}
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
