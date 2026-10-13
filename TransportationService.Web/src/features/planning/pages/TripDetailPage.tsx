import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { apiClient, ApiError } from '../../../api/apiClient'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { searchTransportOrders } from '../../transport-orders/api/transportOrdersApi'
import type { TransportOrderListItem } from '../../transport-orders/types'
import { changeTripStatus, deleteTrip, getTrip, updateTrip } from '../api/planningApi'
import { getTripExecution } from '../../my-trips/api/myTripsApi'
import type { TripExecution } from '../../my-trips/types'
import { STOP_EXECUTION_ICONS, STOP_EXECUTION_LABELS, STOP_EXECUTION_TONE } from '../../my-trips/types'
import { getPodForStop } from '../../pod/api/podApi'
import { getTripPackageReadiness } from '../../packages/api/packagesApi'
import { PACKAGE_STATUS_LABELS, PACKAGE_STATUS_TONE, type TripPackageReadiness } from '../../packages/types'
import { TripCostingPanel } from '../../trip-costing/components/TripCostingPanel'
import { Modal } from '../../../components/ui/Modal'
import {
  CONFLICT_SEVERITY_META,
  TRIP_STATUS_LABELS,
  TRIP_STATUS_TONE,
  TRIP_TRANSITION_LABELS,
  type TripDetail,
  type TripStatus,
} from '../types'
import { downloadTripDocuments } from '../../transport-orders/api/transportDocumentsApi'
import {
  EMPTY_VEHICLE_DRAFT,
  vehicleDraftAfterDriverChange,
  vehicleDraftAfterManualPick,
  withVehicleSelectionSource,
  type VehicleDraft,
} from '../vehicleSelection'
import {
  buildDriverOptions,
  buildTrailerOptions,
  buildVehicleOptions,
  joinParts,
  lookupFixedVehicleId,
} from '../resourceOptions'
import { usePlanningResources, type ResourceListStatus } from '../usePlanningResources'
import './planning.css'

export function TripDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const { t } = useLocale()

  const [trip, setTrip] = useState<TripDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // Driver/vehicle/trailer lists + their per-picker status (shared with the dossier planning block).
  const { drivers, vehicles, trailers, status: resourceStatus } = usePlanningResources()
  const [availableOrders, setAvailableOrders] = useState<TransportOrderListItem[]>([])
  const [ordersStatus, setOrdersStatus] = useState<ResourceListStatus>('loading')
  const listStatus = { ...resourceStatus, orders: ordersStatus }

  // Draft edits kept locally until "Opslaan".
  const [driverId, setDriverId] = useState('')
  // Vehicle id plus how it got there: a suggestion follows the driver, a manual pick never does.
  const [vehicleDraft, setVehicleDraft] = useState<VehicleDraft>(EMPTY_VEHICLE_DRAFT)
  const vehicleId = vehicleDraft.vehicleId
  // Guards the fixed-vehicle lookup against an older driver choice answering last.
  const driverLookupSeq = useRef(0)
  const [trailerId, setTrailerId] = useState('')
  const [tripDate, setTripDate] = useState('')
  const [notes, setNotes] = useState('')
  const [orderIds, setOrderIds] = useState<string[]>([])
  const [plannedDistanceKm, setPlannedDistanceKm] = useState('')
  const [plannedEmptyKm, setPlannedEmptyKm] = useState('')
  const [dirty, setDirty] = useState(false)

  const [overrideTarget, setOverrideTarget] = useState<{ status: TripStatus; conflicts: string[] } | null>(null)
  const [cancelTarget, setCancelTarget] = useState<TripStatus | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [execution, setExecution] = useState<TripExecution | null>(null)
  const [readiness, setReadiness] = useState<TripPackageReadiness | null>(null)
  const [releaseTarget, setReleaseTarget] = useState<{ status: TripStatus; readiness: TripPackageReadiness } | null>(null)
  const [releaseReason, setReleaseReason] = useState('')

  const canSeeExecution = hasPermission('driver_workflow.view')
  const canOpenPod = hasPermission('pod.view')
  const canEditPlanning = hasPermission('planning.edit')
  const canSeePackages = hasPermission('packages.view')
  const canReleaseTrip = hasPermission('warehouse.release_trip')

  interface StopEtaInfo {
    transportOrderStopId: string
    currentEta: string
    source: 'Heuristic' | 'Provider' | 'DispatcherOverride'
    status: 'OnTime' | 'AtRisk' | 'Late'
  }
  interface TripEta {
    manualDelayMinutes: number
    delayReason: string | null
    stops: StopEtaInfo[]
  }
  const [eta, setEta] = useState<TripEta | null>(null)
  const [etaReload, setEtaReload] = useState(0)
  const [delayOpen, setDelayOpen] = useState(false)
  const [delayMinutes, setDelayMinutes] = useState('0')
  const [delayReason, setDelayReason] = useState('')

  const applyTrip = useCallback((data: TripDetail) => {
    setTrip(data)
    setDriverId(data.driverId ?? '')
    setVehicleDraft(
      data.vehicleId
        ? { vehicleId: data.vehicleId, source: data.vehicleSelectionSource ?? null }
        : EMPTY_VEHICLE_DRAFT,
    )
    driverLookupSeq.current++
    setTrailerId(data.trailerId ?? '')
    setTripDate(data.tripDate)
    setNotes(data.notes ?? '')
    setOrderIds(data.orders.map((o) => o.transportOrderId))
    setPlannedDistanceKm(data.plannedDistanceKm?.toString() ?? '')
    setPlannedEmptyKm(data.plannedEmptyKm?.toString() ?? '')
    setDirty(false)
  }, [])

  useEffect(() => {
    let mounted = true
    getTrip(id)
      .then((data) => {
        if (!mounted) return
        applyTrip(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError(t('planning.detail.loadError'))
      })
    return () => {
      mounted = false
    }
  }, [id, applyTrip, t])

  // Execution snapshot (stop statuses + POD flags) for trips that left Draft.
  useEffect(() => {
    if (!canSeeExecution || !trip || trip.status === 'Draft' || trip.status === 'Cancelled') return
    let mounted = true
    getTripExecution(id)
      .then((data) => {
        if (mounted) setExecution(data)
      })
      .catch(() => {
        // The section simply stays hidden.
      })
    return () => {
      mounted = false
    }
  }, [id, canSeeExecution, trip])

  // Load completeness for the departure gate; only relevant while the trip can still leave.
  const readinessRelevant = trip != null && (trip.status === 'Planned' || trip.status === 'InProgress')
  useEffect(() => {
    if (!canSeePackages || !trip || !readinessRelevant) return
    let mounted = true
    getTripPackageReadiness(trip.id)
      .then((data) => {
        if (mounted) setReadiness(data.totalPackages > 0 ? data : null)
      })
      .catch(() => {
        // The widget simply stays hidden.
      })
    return () => {
      mounted = false
    }
  }, [canSeePackages, trip, readinessRelevant])

  // Live ETA raming next to the execution snapshot (recalculated server-side on read).
  useEffect(() => {
    if (!canSeeExecution || !trip || trip.status !== 'InProgress') return
    let mounted = true
    apiClient
      .getJson<TripEta>(`/api/trips/${id}/eta`)
      .then((data) => {
        if (mounted) setEta(data)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [id, canSeeExecution, trip, etaReload])

  async function submitDelay() {
    setBusy(true)
    try {
      await apiClient.postJson(`/api/trips/${id}/delay`, {
        minutes: Number(delayMinutes || '0'),
        reason: delayReason.trim() || null,
      })
      showSuccess(t('planning.detail.delayRegistered'))
      setDelayOpen(false)
      setEtaReload((r) => r + 1)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('planning.detail.delayFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function openPod(stopId: string) {
    try {
      const pod = await getPodForStop(id, stopId)
      navigate(`/pods/${pod.id}`)
    } catch {
      showError(t('planning.detail.podOpenFailed'))
    }
  }

  useEffect(() => {
    let mounted = true
    searchTransportOrders({ status: 'Confirmed', page: 1, pageSize: 100 })
      .then((data) => {
        if (!mounted) return
        setAvailableOrders(data.items)
        setOrdersStatus('ready')
      })
      .catch(() => {
        if (mounted) setOrdersStatus('error')
      })
    return () => {
      mounted = false
    }
  }, [])

  const editable = trip?.status === 'Draft' && hasPermission('planning.edit')

  function markDirty() {
    setDirty(true)
  }

  function handleDriverChange(nextDriverId: string | null) {
    setDriverId(nextDriverId ?? '')
    markDirty()
    const seq = ++driverLookupSeq.current
    if (!nextDriverId) {
      setVehicleDraft((current) => vehicleDraftAfterDriverChange(current, null))
      return
    }
    // The fixed vehicle comes from the driver list (`fixedVehicleId`); an older payload without
    // it costs one driver-detail call. A failed lookup simply means no suggestion.
    void lookupFixedVehicleId(drivers, vehicles, nextDriverId).then((fixedId) => {
      if (seq !== driverLookupSeq.current) return
      setVehicleDraft((current) => vehicleDraftAfterDriverChange(current, fixedId))
    })
  }

  function moveOrder(index: number, delta: number) {
    setOrderIds((ids) => {
      const target = index + delta
      if (target < 0 || target >= ids.length) return ids
      const next = [...ids]
      ;[next[index], next[target]] = [next[target], next[index]]
      return next
    })
    markDirty()
  }

  async function handleSave() {
    if (!trip) return
    setBusy(true)
    try {
      const parsedDistance = plannedDistanceKm.trim() === '' ? null : Number(plannedDistanceKm.replace(',', '.'))
      const parsedEmpty = plannedEmptyKm.trim() === '' ? null : Number(plannedEmptyKm.replace(',', '.'))
      const updated = await updateTrip(
        trip.id,
        withVehicleSelectionSource(
          {
            tripDate,
            driverId: driverId || null,
            vehicleId: vehicleId || null,
            trailerId: trailerId || null,
            plannedStart: trip.plannedStart,
            plannedEnd: trip.plannedEnd,
            notes: notes.trim() || null,
            orderIds,
            plannedDistanceKm: parsedDistance !== null && Number.isNaN(parsedDistance) ? null : parsedDistance,
            plannedEmptyKm: parsedEmpty !== null && Number.isNaN(parsedEmpty) ? null : parsedEmpty,
          },
          vehicleDraft.source,
        ),
      )
      // The API echoes `vehicleSelectionSource`, so a manual choice survives this save and a reload.
      applyTrip(updated)
      showSuccess(t('planning.detail.saved'))
    } catch (err) {
      showError(
        err instanceof ApiError && err.status === 400
          ? t('planning.detail.saveFailedInput')
          : t('planning.detail.saveFailed'),
      )
    } finally {
      setBusy(false)
    }
  }

  async function applyTransition(target: TripStatus, override: boolean, release = false) {
    if (!trip) return
    setBusy(true)
    try {
      const updated = await changeTripStatus(
        trip.id, target, override, release, release ? releaseReason.trim() || null : null)
      applyTrip(updated)
      showSuccess(t('planning.detail.statusChanged', { status: t(TRIP_STATUS_LABELS[target]) }))
      setOverrideTarget(null)
      setReleaseTarget(null)
      setReleaseReason('')
    } catch (err) {
      const body = err instanceof ApiError ? (err.body as { packageReadiness?: TripPackageReadiness } | null) : null
      if (err instanceof ApiError && err.status === 409 && body?.packageReadiness) {
        // Departure gate: not all mandatory packages are on the vehicle.
        setReleaseTarget({ status: target, readiness: body.packageReadiness })
        setReadiness(body.packageReadiness)
      } else if (err instanceof ApiError && err.status === 409 && !override) {
        // Blocking conflicts: offer the override path (guarded server-side by permission).
        setOverrideTarget({
          status: target,
          conflicts: trip.conflicts.filter((c) => c.blocking).map((c) => c.description),
        })
      } else if (err instanceof ApiError && err.status === 403) {
        showError(err.message || t('planning.detail.noPermission'))
        setOverrideTarget(null)
        setReleaseTarget(null)
      } else {
        showError(t('planning.detail.statusChangeFailed'))
        setOverrideTarget(null)
        setReleaseTarget(null)
      }
    } finally {
      setBusy(false)
      setCancelTarget(null)
    }
  }

  async function handleDelete() {
    if (!trip) return
    try {
      await deleteTrip(trip.id)
      showSuccess(t('planning.detail.deleted'))
      navigate('/planning')
    } catch {
      showError(t('planning.detail.deleteFailed'))
      setConfirmDelete(false)
    }
  }

  // Picker options: built by the shared builders (planning/resourceOptions.ts), so the dossier
  // planning block searches exactly like this page.
  const driverOptions = useMemo(() => buildDriverOptions(drivers, t), [drivers, t])
  const vehicleOptions = useMemo(() => buildVehicleOptions(vehicles), [vehicles])
  const trailerOptions = useMemo(() => buildTrailerOptions(trailers), [trailers])

  const attachableOptions = useMemo<SearchableSelectOption[]>(
    () =>
      availableOrders
        .filter((order) => !orderIds.includes(order.id))
        .map((order) => ({
          value: order.id,
          label: `${order.orderNumber} — ${order.customerName}`,
          subtitle: `${order.firstLoadingCity ?? '?'} → ${order.lastUnloadingCity ?? '?'}`,
          meta: joinParts(
            [
              order.customerReference ? t('planning.detail.orderReference', { reference: order.customerReference }) : null,
              order.goodsDescription,
            ],
            ' · ',
          ),
          keywords: joinParts(
            [order.customerReference, order.firstLoadingCity, order.lastUnloadingCity, order.goodsDescription],
            ' ',
          ),
        })),
    [availableOrders, orderIds, t],
  )

  if (loadError) return <ErrorState message={loadError} />
  if (!trip) return <LoadingState message={t('planning.detail.loading')} />

  const attachedSummaries = orderIds.map((orderId) => {
    const onTrip = trip.orders.find((o) => o.transportOrderId === orderId)
    const available = availableOrders.find((o) => o.id === orderId)
    return {
      id: orderId,
      label: onTrip
        ? `${onTrip.orderNumber} — ${onTrip.customerName} (${onTrip.firstLoadingCity ?? '?'} → ${onTrip.lastUnloadingCity ?? '?'})`
        : available
          ? `${available.orderNumber} — ${available.customerName} (${available.firstLoadingCity ?? '?'} → ${available.lastUnloadingCity ?? '?'})`
          : orderId,
    }
  })

  return (
    <div>
      <Breadcrumbs items={[{ label: t('planning.title'), to: '/planning' }, { label: trip.tripNumber }]} />
      <PageHeader
        title={`${trip.tripNumber} — ${trip.tripDate}`}
        action={
          <span className="pl-header-actions">
            <Badge tone={TRIP_STATUS_TONE[trip.status]}>{t(TRIP_STATUS_LABELS[trip.status])}</Badge>
            {/* Wave 9: één samengevoegde PDF per rit, in routevolgorde. */}
            <Button
              variant="secondary"
              onClick={() => void downloadTripDocuments(trip.id, 'cmr', trip.tripNumber)
                .catch(() => showError(t('planning.detail.cmrFailed')))}
              disabled={busy}
            >
              {t('planning.detail.cmrButton')}
            </Button>
            <Button
              variant="secondary"
              onClick={() => void downloadTripDocuments(trip.id, 'delivery-note', trip.tripNumber)
                .catch(() => showError(t('planning.detail.deliveryNotesFailed')))}
              disabled={busy}
            >
              {t('planning.detail.deliveryNotesButton')}
            </Button>
            {hasPermission('planning.edit') &&
              trip.allowedTransitions.map((target) => (
                <Button
                  key={target}
                  variant={target === 'Cancelled' ? 'secondary' : 'primary'}
                  onClick={() => (target === 'Cancelled' ? setCancelTarget(target) : void applyTransition(target, false))}
                  disabled={busy || dirty}
                >
                  {t(TRIP_TRANSITION_LABELS[target])}
                </Button>
              ))}
          </span>
        }
      />

      {dirty && <p className="pl-dirty-hint">{t('planning.detail.dirtyHint')}</p>}

      <section className="pl-section">
        <h2>{t('planning.detail.assignmentTitle')}</h2>
        <div className="pl-assign">
          <FormField label={t('planning.detail.date')} htmlFor="tr-date">
            <input
              id="tr-date"
              type="date"
              value={tripDate}
              onChange={(e) => {
                setTripDate(e.target.value)
                markDirty()
              }}
              disabled={!editable || busy}
            />
          </FormField>
          <FormField label={t('planning.detail.driver')} htmlFor="tr-driver">
            <SearchableSelect
              id="tr-driver"
              value={driverId || null}
              onChange={handleDriverChange}
              options={driverOptions}
              // A driver that left the active list (or a list that failed) still shows by name.
              selectedLabel={driverId === trip.driverId ? (trip.driverName ?? undefined) : undefined}
              placeholder={editable ? t('planning.detail.driverSearchPlaceholder') : t('planning.detail.none')}
              isLoading={listStatus.drivers === 'loading'}
              errorMessage={listStatus.drivers === 'error' ? t('planning.detail.driversLoadError') : null}
              disabled={!editable || busy}
            />
          </FormField>
          <FormField
            label={t('planning.detail.vehicle')}
            htmlFor="tr-vehicle"
            hint={vehicleDraft.source === 'Suggested' ? t('planning.detail.vehicleSuggestedHint') : undefined}
          >
            <SearchableSelect
              id="tr-vehicle"
              value={vehicleId || null}
              onChange={(next) => {
                driverLookupSeq.current++
                setVehicleDraft(vehicleDraftAfterManualPick(next))
                markDirty()
              }}
              options={vehicleOptions}
              selectedLabel={
                vehicleId === trip.vehicleId && trip.vehicleNumber
                  ? trip.vehicleLicensePlate
                    ? `${trip.vehicleNumber} (${trip.vehicleLicensePlate})`
                    : trip.vehicleNumber
                  : undefined
              }
              placeholder={editable ? t('planning.detail.vehicleSearchPlaceholder') : t('planning.detail.none')}
              isLoading={listStatus.vehicles === 'loading'}
              errorMessage={listStatus.vehicles === 'error' ? t('planning.detail.vehiclesLoadError') : null}
              disabled={!editable || busy}
            />
          </FormField>
          <FormField label={t('planning.detail.trailer')} htmlFor="tr-trailer">
            <SearchableSelect
              id="tr-trailer"
              value={trailerId || null}
              onChange={(next) => {
                setTrailerId(next ?? '')
                markDirty()
              }}
              options={trailerOptions}
              selectedLabel={trailerId === trip.trailerId ? (trip.trailerNumber ?? undefined) : undefined}
              placeholder={editable ? t('planning.detail.trailerSearchPlaceholder') : t('planning.detail.none')}
              isLoading={listStatus.trailers === 'loading'}
              errorMessage={listStatus.trailers === 'error' ? t('planning.detail.trailersLoadError') : null}
              disabled={!editable || busy}
            />
          </FormField>
          <FormField label={t('planning.detail.plannedDistance')} htmlFor="tr-distance" hint={t('planning.detail.plannedDistanceHint')}>
            <input
              id="tr-distance"
              inputMode="decimal"
              value={plannedDistanceKm}
              onChange={(e) => {
                setPlannedDistanceKm(e.target.value)
                markDirty()
              }}
              disabled={!editable || busy}
            />
          </FormField>
          <FormField label={t('planning.detail.emptyDistance')} htmlFor="tr-empty">
            <input
              id="tr-empty"
              inputMode="decimal"
              value={plannedEmptyKm}
              onChange={(e) => {
                setPlannedEmptyKm(e.target.value)
                markDirty()
              }}
              disabled={!editable || busy}
            />
          </FormField>
        </div>
        <FormField label={t('planning.detail.notes')} htmlFor="tr-notes">
          <textarea
            id="tr-notes"
            rows={2}
            value={notes}
            onChange={(e) => {
              setNotes(e.target.value)
              markDirty()
            }}
            disabled={!editable || busy}
            className="pl-notes"
          />
        </FormField>
      </section>

      <section className="pl-section">
        <h2>{t('planning.detail.ordersTitle', { count: orderIds.length })}</h2>
        {attachedSummaries.length === 0 && <p className="placeholder-text">{t('planning.detail.ordersEmpty')}</p>}
        {attachedSummaries.length > 0 && (
          <ol className="pl-orders">
            {attachedSummaries.map((order, index) => (
              <li key={order.id}>
                <span className="pl-order-label">{order.label}</span>
                {editable && (
                  <span className="pl-order-actions">
                    <button type="button" className="pl-link" onClick={() => moveOrder(index, -1)} disabled={busy || index === 0}>
                      ↑
                    </button>
                    <button
                      type="button"
                      className="pl-link"
                      onClick={() => moveOrder(index, 1)}
                      disabled={busy || index === attachedSummaries.length - 1}
                    >
                      ↓
                    </button>
                    <button
                      type="button"
                      className="pl-link pl-link-danger"
                      onClick={() => {
                        setOrderIds((ids) => ids.filter((x) => x !== order.id))
                        markDirty()
                      }}
                      disabled={busy}
                    >
                      {t('planning.detail.remove')}
                    </button>
                  </span>
                )}
              </li>
            ))}
          </ol>
        )}

        {/* An adder, not a field: the value stays empty and every pick appends to the list. */}
        {editable && (listStatus.orders !== 'ready' || attachableOptions.length > 0) && (
          <div className="pl-add-order">
            <SearchableSelect
              id="tr-add-order"
              value={null}
              onChange={(orderId) => {
                if (!orderId) return
                setOrderIds((ids) => (ids.includes(orderId) ? ids : [...ids, orderId]))
                markDirty()
              }}
              options={attachableOptions}
              placeholder={t('planning.detail.addOrderOption')}
              ariaLabel={t('planning.detail.addOrderLabel')}
              emptyMessage={t('planning.detail.ordersNoneAvailable')}
              isLoading={listStatus.orders === 'loading'}
              errorMessage={listStatus.orders === 'error' ? t('planning.detail.ordersLoadError') : null}
              clearable={false}
              disabled={busy}
            />
          </div>
        )}
      </section>

      {execution && execution.stops.length > 0 && (
        <section className="pl-section">
          <h2>
            {t('planning.detail.executionTitle', { completed: execution.completedCount, total: execution.totalCount })}
          </h2>
          {trip.status === 'InProgress' && canEditPlanning && (
            <p className="pl-eta-bar">
              {eta && eta.manualDelayMinutes > 0 && (
                <span className="pl-execution-late">
                  ⏱ {t('planning.detail.delaySummary', { count: eta.manualDelayMinutes })}
                  {eta.delayReason ? ` (${eta.delayReason})` : ''} ·{' '}
                </span>
              )}
              <button
                type="button"
                className="pl-link"
                onClick={() => {
                  setDelayMinutes(String(eta?.manualDelayMinutes ?? 0))
                  setDelayReason(eta?.delayReason ?? '')
                  setDelayOpen(true)
                }}
              >
                {t('planning.detail.reportDelay')}
              </button>
              <span className="pl-eta-note"> · {t('planning.detail.etaNote')}</span>
            </p>
          )}
          <ol className="pl-execution-stops">
            {execution.stops.map((stop) => {
              const stopEta = eta?.stops.find((s) => s.transportOrderStopId === stop.transportOrderStopId)
              return (
                <li key={stop.transportOrderStopId}>
                  <Badge tone={stop.stopType === 'Loading' ? 'info' : 'success'}>
                    {stop.stopType === 'Loading' ? t('planning.detail.stopLoading') : t('planning.detail.stopUnloading')}
                  </Badge>{' '}
                  <span className="pl-execution-location">
                    {stop.locationName}
                    {stop.city && stop.locationName !== stop.city ? ` — ${stop.city}` : ''}
                  </span>{' '}
                  <Badge tone={STOP_EXECUTION_TONE[stop.status]}>
                    {STOP_EXECUTION_ICONS[stop.status]} {t(STOP_EXECUTION_LABELS[stop.status])}
                  </Badge>
                  {stopEta && (
                    <Badge tone={stopEta.status === 'Late' ? 'danger' : stopEta.status === 'AtRisk' ? 'warning' : 'success'}>
                      ⏱ ETA {stopEta.currentEta.slice(11, 16)}
                      {stopEta.source === 'DispatcherOverride' && ` ${t('planning.detail.etaManual')}`}
                    </Badge>
                  )}
                  {stop.lateArrivalReason && (
                    <span className="pl-execution-late"> · {t('planning.detail.lateArrival', { reason: stop.lateArrivalReason })}</span>
                  )}
                  {stop.hasPod && canOpenPod && (
                    <button type="button" className="pl-link" onClick={() => void openPod(stop.transportOrderStopId)}>
                      ✍ {t('planning.detail.viewPod')}
                    </button>
                  )}
                </li>
              )
            })}
          </ol>
        </section>
      )}

      {delayOpen && (
        <Modal
          title={t('planning.detail.delayModalTitle')}
          onClose={() => setDelayOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDelayOpen(false)} disabled={busy}>
                {t('planning.detail.cancel')}
              </Button>
              <Button onClick={() => void submitDelay()} disabled={busy}>
                {busy ? t('planning.detail.busy') : t('planning.detail.save')}
              </Button>
            </>
          }
        >
          <FormField label={t('planning.detail.delayMinutes')} htmlFor="pl-delay-min" hint={t('planning.detail.delayMinutesHint')}>
            <input id="pl-delay-min" type="number" min={0} max={1440} value={delayMinutes} onChange={(e) => setDelayMinutes(e.target.value)} disabled={busy} />
          </FormField>
          <FormField label={t('planning.detail.delayReason')} htmlFor="pl-delay-reason">
            <input id="pl-delay-reason" value={delayReason} onChange={(e) => setDelayReason(e.target.value)} disabled={busy} maxLength={500} placeholder={t('planning.detail.delayReasonPlaceholder')} />
          </FormField>
        </Modal>
      )}

      <section className="pl-section">
        <h2>{t('planning.detail.conflictsTitle')}</h2>
        {trip.conflicts.length === 0 && <p className="pl-ok">✓ {t('planning.detail.noConflicts')}</p>}
        {trip.conflicts.length > 0 && (
          <ul className="pl-conflicts">
            {trip.conflicts.map((conflict, index) => (
              <li key={`${conflict.code}-${index}`}>
                <Badge tone={CONFLICT_SEVERITY_META[conflict.severity]?.tone ?? (conflict.blocking ? 'danger' : 'warning')}>
                  {t(CONFLICT_SEVERITY_META[conflict.severity]?.label ??
                    (conflict.blocking ? 'planning.conflictSeverity.Blocking' : 'planning.conflictSeverity.Warning'))}
                </Badge>{' '}
                {conflict.description}
              </li>
            ))}
          </ul>
        )}
      </section>

      {readiness && readinessRelevant && (
        <section className="pl-section">
          <h2>{t('planning.detail.readinessTitle')}</h2>
          <p className={readiness.isComplete ? 'pl-ok' : 'pl-readiness-warning'}>
            {readiness.isComplete
              ? `✓ ${t('planning.detail.readinessComplete', { count: readiness.mandatoryPackages })}`
              : `⚠ ${t('planning.detail.readinessIncomplete', {
                  loaded: readiness.loadedCount,
                  mandatory: readiness.mandatoryPackages,
                  notLoaded: readiness.notLoadedCount,
                })}` +
                (readiness.missingCount > 0 ? t('planning.detail.readinessMissingPart', { count: readiness.missingCount }) : '') +
                (readiness.damagedCount > 0 ? t('planning.detail.readinessDamagedPart', { count: readiness.damagedCount }) : '') +
                '.'}
            {readiness.openExceptionCount > 0 &&
              ` ${t('planning.detail.readinessOpenExceptions', { count: readiness.openExceptionCount })}`}
          </p>
          {!readiness.isComplete && (
            <>
              <p className="pl-readiness-rule">
                {readiness.isBlocked
                  ? t('planning.detail.readinessRuleBlocked')
                  : readiness.requiresOverride
                    ? t('planning.detail.readinessRuleOverride')
                    : t('planning.detail.readinessRuleWarning')}
              </p>
              <ul className="pl-conflicts">
                {readiness.outstandingPackages.map((item) => (
                  <li key={item.packageId}>
                    <Badge tone={PACKAGE_STATUS_TONE[item.status]}>{t(PACKAGE_STATUS_LABELS[item.status])}</Badge>{' '}
                    {item.packageNumber} — {item.description} ({item.orderNumber})
                  </li>
                ))}
              </ul>
            </>
          )}
        </section>
      )}

      {hasPermission('trip_costs.view') && <TripCostingPanel tripId={trip.id} tripStatus={trip.status} />}

      <div className="pl-detail-actions">
        {editable && dirty && (
          <Button onClick={() => void handleSave()} disabled={busy}>
            {busy ? t('planning.detail.saving') : t('planning.detail.save')}
          </Button>
        )}
        {(trip.status === 'Draft' || trip.status === 'Cancelled') && hasPermission('planning.edit') && (
          <Button variant="secondary" onClick={() => setConfirmDelete(true)} disabled={busy}>
            {t('planning.detail.remove')}
          </Button>
        )}
      </div>

      {overrideTarget && (
        <ConfirmDialog
          title={t('planning.detail.overrideTitle')}
          message={t('planning.detail.overrideMessage', { conflicts: overrideTarget.conflicts.join('\n') })}
          confirmLabel={t('planning.detail.overrideConfirm')}
          destructive
          onConfirm={() => void applyTransition(overrideTarget.status, true)}
          onCancel={() => setOverrideTarget(null)}
        />
      )}

      {releaseTarget && (
        <Modal title={t('planning.detail.releaseTitle')} onClose={() => setReleaseTarget(null)} busy={busy}>
          <p>
            {t('planning.detail.releaseLoaded', {
              loaded: releaseTarget.readiness.loadedCount,
              mandatory: releaseTarget.readiness.mandatoryPackages,
            })}{' '}
            {releaseTarget.readiness.isBlocked
              ? t('planning.detail.releaseBlockedRule')
              : t('planning.detail.releaseOverrideRule')}
          </p>
          <ul className="pl-conflicts">
            {releaseTarget.readiness.outstandingPackages.map((item) => (
              <li key={item.packageId}>
                <Badge tone={PACKAGE_STATUS_TONE[item.status]}>{t(PACKAGE_STATUS_LABELS[item.status])}</Badge>{' '}
                {item.packageNumber} — {item.description}
              </li>
            ))}
          </ul>
          {!releaseTarget.readiness.isBlocked && (
            <>
              {!canReleaseTrip && (
                <p className="pl-readiness-warning">
                  {t('planning.detail.releaseNoRight')}
                </p>
              )}
              <FormField label={t('planning.detail.releaseReasonLabel')} htmlFor="release-reason" required>
                <input
                  id="release-reason"
                  value={releaseReason}
                  onChange={(e) => setReleaseReason(e.target.value)}
                  disabled={busy || !canReleaseTrip}
                  maxLength={500}
                  placeholder={t('planning.detail.releaseReasonPlaceholder')}
                />
              </FormField>
            </>
          )}
          <div className="pl-detail-actions">
            <Button variant="secondary" onClick={() => setReleaseTarget(null)} disabled={busy}>
              {t('planning.detail.close')}
            </Button>
            {!releaseTarget.readiness.isBlocked && canReleaseTrip && (
              <Button
                onClick={() => void applyTransition(releaseTarget.status, false, true)}
                disabled={busy || !releaseReason.trim()}
              >
                {t('planning.detail.releaseConfirm')}
              </Button>
            )}
          </div>
        </Modal>
      )}

      {cancelTarget && (
        <ConfirmDialog
          title={t('planning.detail.cancelTitle')}
          message={t('planning.detail.cancelMessage', { tripNumber: trip.tripNumber })}
          confirmLabel={t('planning.detail.cancelConfirm')}
          destructive
          onConfirm={() => void applyTransition(cancelTarget, false)}
          onCancel={() => setCancelTarget(null)}
        />
      )}

      {confirmDelete && (
        <ConfirmDialog
          title={t('planning.detail.deleteTitle')}
          message={t('planning.detail.deleteMessage', { tripNumber: trip.tripNumber })}
          confirmLabel={t('planning.detail.deleteConfirm')}
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(false)}
        />
      )}
    </div>
  )
}
