import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { apiClient, ApiError } from '../../../api/apiClient'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { isNotFoundError } from '../../../api/notFound'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ActionMenu, type ActionMenuItem } from '../../../components/ui/ActionMenu'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { FormActions } from '../../../components/ui/FormActions'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { SaveStatus } from '../../../components/ui/SaveStatus'
import { useSaveStatus } from '../../../components/ui/useSaveStatus'
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
  type PlanningConflict,
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
import { formatDate } from '../../../utils/dates'
import { ConflictDialog } from '../../planning-center/components/ConflictDialog'

/** Lifecycle steps that move a trip forward; the first allowed one is the primary header action. */
const TRIP_FORWARD_STEPS: TripStatus[] = ['Planned', 'InProgress', 'Completed']

export function TripDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const { t } = useLocale()

  const [trip, setTrip] = useState<TripDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [loadNotFound, setLoadNotFound] = useState(false)
  const [reloadToken, setReloadToken] = useState(0)
  const [busy, setBusy] = useState(false)

  // Driver/vehicle/trailer lists + their per-picker status (shared with the dossier planning block).
  const { drivers, vehicles, trailers, status: resourceStatus } = usePlanningResources(true, trip?.tripDate)
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
  // B23/B31: explicit in-place save — its state ("Opslaan…" / "✓ Opgeslagen · 14:22" / failure with
  // retry) stays visible next to the assignment instead of a passing toast.
  const saveStatus = useSaveStatus()
  const [showPackages, setShowPackages] = useState(false)

  const [overrideTarget, setOverrideTarget] = useState<{ status: TripStatus; message: string; conflicts: PlanningConflict[] } | null>(null)
  const [cancelTarget, setCancelTarget] = useState<TripStatus | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [execution, setExecution] = useState<TripExecution | null>(null)
  const [readiness, setReadiness] = useState<TripPackageReadiness | null>(null)
  const [releaseTarget, setReleaseTarget] = useState<{ status: TripStatus; readiness: TripPackageReadiness } | null>(null)
  const [startWarnings, setStartWarnings] = useState<string[] | null>(null)
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
      .catch((err: unknown) => {
        if (!mounted) return
        setLoadNotFound(isNotFoundError(err))
        setLoadError(t('planning.detail.loadError'))
      })
    return () => {
      mounted = false
    }
  }, [id, applyTrip, t, reloadToken])

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
    saveStatus.setState({ kind: 'saving' })
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
      saveStatus.setState({ kind: 'saved', at: new Date().toISOString() })
    } catch (err) {
      saveStatus.setState({
        kind: 'error',
        message: err instanceof ApiError && err.status === 400
          ? t('planning.detail.saveFailedInput')
          : t('planning.detail.saveFailed'),
      })
    } finally {
      setBusy(false)
    }
  }

  /**
   * Transport acceptance M6: starting a trip is a dispatch decision. Hard rules stay on the
   * server (blocked departure rule, conflicts); conditions the domain merely WARNS about —
   * starting before the planned day, mandatory colli not loaded under a "warn" departure rule —
   * are confirmed explicitly instead of passing silently.
   */
  function requestTransition(target: TripStatus) {
    if (!trip || target !== 'InProgress') {
      void applyTransition(target, false)
      return
    }
    const warnings: string[] = []
    const today = new Date()
    const todayIso = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`
    if (trip.tripDate > todayIso) {
      warnings.push(t('planning.detail.startWarnEarly', { date: formatDate(trip.tripDate) }))
    }
    if (readiness && !readiness.isComplete && !readiness.isBlocked && !readiness.requiresOverride) {
      warnings.push(t('planning.detail.startWarnPackages', {
        loaded: readiness.loadedCount, mandatory: readiness.mandatoryPackages, notLoaded: readiness.notLoadedCount,
      }))
    }
    if (warnings.length === 0) {
      void applyTransition(target, false)
      return
    }
    setStartWarnings(warnings)
  }

  async function applyTransition(target: TripStatus, override: boolean, release = false, overrideReason: string | null = null) {
    if (!trip) return
    setBusy(true)
    try {
      // T-H5: a conflict override carries its mandatory reason exactly like the Planbord does.
      const updated = await changeTripStatus(
        trip.id, target, override, release, release ? releaseReason.trim() || null : overrideReason)
      applyTrip(updated)
      showSuccess(t('planning.detail.statusChanged', { status: t(TRIP_STATUS_LABELS[target]) }))
      setOverrideTarget(null)
      setReleaseTarget(null)
      setReleaseReason('')
    } catch (err) {
      const body = err instanceof ApiError
        ? (err.body as { packageReadiness?: TripPackageReadiness; message?: string; conflicts?: PlanningConflict[] } | null)
        : null
      if (err instanceof ApiError && err.status === 409 && body?.packageReadiness) {
        // Departure gate: not all mandatory packages are on the vehicle.
        setReleaseTarget({ status: target, readiness: body.packageReadiness })
        setReadiness(body.packageReadiness)
      } else if (err instanceof ApiError && err.status === 409 && !override) {
        // Blocking conflicts: the SAME dialog as the Planbord — structured conflicts, and an
        // override (mandatory reason) only for users holding the required permission.
        setOverrideTarget({
          status: target,
          message: body?.message ?? t('planning.detail.overrideTitle'),
          conflicts: body?.conflicts ?? trip.conflicts.filter((c) => c.blocking),
        })
      } else if (err instanceof ApiError && err.status === 403) {
        showError(err.message || t('planning.detail.noPermission'))
        setOverrideTarget(null)
        setReleaseTarget(null)
      } else if (err instanceof ApiError && err.status === 400 && err.message) {
        // A refused retry (e.g. missing reason) keeps the dialog open with the server's reason.
        showError(err.message)
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

  if (loadError && loadNotFound) return <ErrorState message={t('planning.detail.notFound')} backTo="/planning" />
  if (loadError) return <ErrorState message={loadError} onRetry={() => { setLoadError(null); setReloadToken((n) => n + 1) }} backTo="/planning" />
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

  // B23: the dossiers this trip serves (unique, in order sequence) — real links, never dead text.
  const tripDossiers = trip.orders.reduce<{ id: string; number: string }[]>(
    (list, order) =>
      order.dossierId && order.dossierNumber && !list.some((entry) => entry.id === order.dossierId)
        ? [...list, { id: order.dossierId, number: order.dossierNumber }]
        : list,
    [],
  )
  const criticalPackages = readiness?.outstandingPackages.filter(
    (item) => item.status === 'Missing' || item.status === 'Damaged' || item.exceptionState === 'Open',
  ) ?? []

  function discardChanges() {
    if (trip) applyTrip(trip)
  }

  // B04: the next forward step is the one primary action; "Terug naar concept", cancelling and
  // deleting live in the "Meer" menu so they never compete with it.
  const canEditTrip = hasPermission('planning.edit')
  const forwardTripTransitions = canEditTrip
    ? TRIP_FORWARD_STEPS.filter((step) => trip.allowedTransitions.includes(step))
    : []
  const primaryTripTransition = forwardTripTransitions[0] ?? null
  const secondaryTripTransitions = forwardTripTransitions.slice(1)
  const tripMenuItems: ActionMenuItem[] = [
    ...(canEditTrip && trip.allowedTransitions.includes('Draft')
      ? [{ key: 'draft', label: t(TRIP_TRANSITION_LABELS.Draft), onSelect: () => void applyTransition('Draft', false), disabled: dirty }]
      : []),
    ...(canEditTrip && trip.allowedTransitions.includes('Cancelled')
      ? [{ key: 'cancel', label: t(TRIP_TRANSITION_LABELS.Cancelled), onSelect: () => setCancelTarget('Cancelled'), danger: true, disabled: dirty }]
      : []),
    ...(canEditTrip && (trip.status === 'Draft' || trip.status === 'Cancelled')
      ? [{ key: 'delete', label: t('planning.detail.remove'), onSelect: () => setConfirmDelete(true), danger: true }]
      : []),
  ]

  return (
    <div>
      <Breadcrumbs
        items={
          // B23: a trip that serves ONE dossier sits in that dossier's path; otherwise in the Ritlijst.
          tripDossiers.length === 1
            ? [
                { label: t('navigation.menu.dossiers'), to: '/dossiers' },
                { label: tripDossiers[0].number, to: `/dossiers/${tripDossiers[0].id}` },
                { label: trip.tripNumber },
              ]
            : [{ label: t('planning.title'), to: '/planning' }, { label: trip.tripNumber }]
        }
      />
      <PageHeader
        title={`${trip.tripNumber} — ${formatDate(trip.tripDate)}`}
        subtitle={
          tripDossiers.length > 0 ? (
            <span className="pl-context-chips">
              <span>{t('planning.detail.dossiersLabel', { count: tripDossiers.length })}</span>
              {tripDossiers.map((dossier) => (
                <Link key={dossier.id} to={`/dossiers/${dossier.id}`} className="pl-chip">
                  {dossier.number}
                </Link>
              ))}
            </span>
          ) : undefined
        }
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
            {secondaryTripTransitions.map((target) => (
              <Button key={target} variant="secondary" onClick={() => requestTransition(target)} disabled={busy || dirty}>
                {t(TRIP_TRANSITION_LABELS[target])}
              </Button>
            ))}
            {primaryTripTransition && (
              <Button onClick={() => requestTransition(primaryTripTransition)} disabled={busy || dirty}>
                {t(TRIP_TRANSITION_LABELS[primaryTripTransition])}
              </Button>
            )}
            <ActionMenu items={tripMenuItems} disabled={busy} size="sm" />
          </span>
        }
      />

      <UnsavedChangesGuard when={dirty && !busy} />

      <section className="pl-section pl-card" aria-labelledby="tr-assign-title">
        <header className="pl-section-head">
          <h2 id="tr-assign-title">{t('planning.detail.assignmentTitle')}</h2>
          <SaveStatus
            state={saveStatus.state}
            dirty={dirty}
            onRetry={editable ? () => void handleSave() : undefined}
          />
        </header>
        {!editable && canEditTrip && (trip.status === 'Planned') && (
          <p className="pl-section-note">
            {t('planning.detail.editOnBoard')}{' '}
            <Link to="/planning-center">{t('planning.detail.openBoard')}</Link>
          </p>
        )}
        <div className="pl-assign pl-assign-4">
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
        </div>
      </section>

      <section className="pl-section pl-card" aria-labelledby="tr-orders-title">
        <header className="pl-section-head">
          <h2 id="tr-orders-title">{t('planning.detail.ordersStopsTitle', { count: orderIds.length })}</h2>
        </header>
        {attachedSummaries.length === 0 && <p className="placeholder-text">{t('planning.detail.ordersEmpty')}</p>}
        {attachedSummaries.length > 0 && (
          <ol className="pl-orders">
            {attachedSummaries.map((order, index) => {
              const onTrip = trip.orders.find((o) => o.transportOrderId === order.id)
              return (
                <li key={order.id}>
                  <span className="pl-order-label">
                    <Link to={`/transport-orders/${order.id}`} className="pl-chip pl-chip-order">
                      {onTrip?.orderNumber ?? order.label}
                    </Link>
                    {onTrip?.dossierId && onTrip.dossierNumber && (
                      <Link to={`/dossiers/${onTrip.dossierId}`} className="pl-chip">
                        {onTrip.dossierNumber}
                      </Link>
                    )}
                    {onTrip ? (
                      <span className="pl-order-text">
                        {onTrip.customerName}
                        <span className="pl-muted-inline"> · {onTrip.firstLoadingCity ?? '?'} → {onTrip.lastUnloadingCity ?? '?'}</span>
                        {onTrip.customerReference && <span className="pl-muted-inline"> · {t('planning.detail.orderReference', { reference: onTrip.customerReference })}</span>}
                      </span>
                    ) : (
                      <span className="pl-order-text pl-muted-inline">{t('planning.detail.addedUnsaved')}</span>
                    )}
                  </span>
                  {editable && (
                    <span className="pl-order-actions">
                      <button type="button" className="pl-link" onClick={() => moveOrder(index, -1)} disabled={busy || index === 0} aria-label={t('planning.detail.moveUp')}>
                        ↑
                      </button>
                      <button
                        type="button"
                        className="pl-link"
                        onClick={() => moveOrder(index, 1)}
                        disabled={busy || index === attachedSummaries.length - 1}
                        aria-label={t('planning.detail.moveDown')}
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
              )
            })}
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

      {execution && execution.stops.length > 0 && (
        <div className="pl-subsection">
          <h3>
            {t('planning.detail.executionTitle', { completed: execution.completedCount, total: execution.totalCount })}
          </h3>
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
        </div>
      )}

      </section>

      <section className="pl-section pl-card">
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
        <section className="pl-section pl-card" aria-labelledby="tr-readiness-title">
          <header className="pl-section-head">
            <h2 id="tr-readiness-title">{t('planning.detail.readinessTitle')}</h2>
          </header>
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
              {/* B23: critical exceptions surface without expanding; the full list is on request. */}
              {criticalPackages.length > 0 && (
                <ul className="pl-conflicts" aria-label={t('planning.detail.criticalPackages')}>
                  {criticalPackages.map((item) => (
                    <li key={item.packageId}>
                      <Badge tone={PACKAGE_STATUS_TONE[item.status]}>{t(PACKAGE_STATUS_LABELS[item.status])}</Badge>{' '}
                      {item.packageNumber} — {item.description} ({item.orderNumber})
                    </li>
                  ))}
                </ul>
              )}
              <button type="button" className="pl-link pl-toggle" aria-expanded={showPackages} onClick={() => setShowPackages((open) => !open)}>
                {showPackages
                  ? t('planning.detail.hidePackages')
                  : t('planning.detail.showPackages', { count: readiness.outstandingPackages.length })}
              </button>
              {showPackages && (
                <ul className="pl-conflicts pl-package-list">
                  {readiness.outstandingPackages.map((item) => (
                    <li key={item.packageId}>
                      <Badge tone={PACKAGE_STATUS_TONE[item.status]}>{t(PACKAGE_STATUS_LABELS[item.status])}</Badge>{' '}
                      {item.packageNumber} — {item.description} ({item.orderNumber})
                    </li>
                  ))}
                </ul>
              )}
            </>
          )}
        </section>
      )}

      <details className="pl-section pl-card pl-details" open={dirty || undefined}>
        <summary>{t('planning.detail.detailsTitle')}</summary>
        <div className="pl-assign pl-assign-2">
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
        {hasPermission('trip_costs.view') && (
          <div className="pl-subsection">
            <TripCostingPanel tripId={trip.id} tripStatus={trip.status} />
          </div>
        )}
      </details>


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

      {editable && dirty && (
        <FormActions dirty>
          <Button variant="secondary" onClick={discardChanges} disabled={busy}>
            {t('planning.detail.discard')}
          </Button>
          <Button onClick={() => void handleSave()} disabled={busy}>
            {busy ? t('planning.detail.saving') : t('planning.detail.save')}
          </Button>
        </FormActions>
      )}

      {startWarnings && (
        <ConfirmDialog
          title={t('planning.detail.startWarnTitle')}
          message={[t('planning.detail.startWarnIntro'), ...startWarnings.map((w) => `• ${w}`)].join('\n')}
          confirmLabel={t('planning.detail.startWarnConfirm')}
          onConfirm={() => {
            setStartWarnings(null)
            void applyTransition('InProgress', false)
          }}
          onCancel={() => setStartWarnings(null)}
        />
      )}

      {overrideTarget && (
        <ConflictDialog
          message={overrideTarget.message}
          conflicts={overrideTarget.conflicts}
          busy={busy}
          onClose={() => setOverrideTarget(null)}
          onOverride={(reason) => void applyTransition(overrideTarget.status, true, false, reason)}
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
