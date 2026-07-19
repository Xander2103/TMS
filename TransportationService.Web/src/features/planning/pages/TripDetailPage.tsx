import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { searchDrivers } from '../../drivers/api/driversApi'
import type { DriverListItem } from '../../drivers/types'
import { getVehicleOptions } from '../../vehicles/api/vehiclesApi'
import type { VehicleOption } from '../../vehicles/types'
import { getTrailerOptions } from '../../trailers/api/trailersApi'
import type { TrailerOption } from '../../trailers/types'
import { searchTransportOrders } from '../../transport-orders/api/transportOrdersApi'
import type { TransportOrderListItem } from '../../transport-orders/types'
import { changeTripStatus, deleteTrip, getTrip, updateTrip } from '../api/planningApi'
import { getTripExecution } from '../../my-trips/api/myTripsApi'
import type { TripExecution } from '../../my-trips/types'
import { STOP_EXECUTION_ICONS, STOP_EXECUTION_LABELS, STOP_EXECUTION_TONE } from '../../my-trips/types'
import { getPodForStop } from '../../pod/api/podApi'
import {
  TRIP_STATUS_LABELS,
  TRIP_STATUS_TONE,
  TRIP_TRANSITION_LABELS,
  type TripDetail,
  type TripStatus,
} from '../types'
import './planning.css'

export function TripDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()

  const [trip, setTrip] = useState<TripDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [drivers, setDrivers] = useState<DriverListItem[]>([])
  const [vehicles, setVehicles] = useState<VehicleOption[]>([])
  const [trailers, setTrailers] = useState<TrailerOption[]>([])
  const [availableOrders, setAvailableOrders] = useState<TransportOrderListItem[]>([])

  // Draft edits kept locally until "Opslaan".
  const [driverId, setDriverId] = useState('')
  const [vehicleId, setVehicleId] = useState('')
  const [trailerId, setTrailerId] = useState('')
  const [tripDate, setTripDate] = useState('')
  const [notes, setNotes] = useState('')
  const [orderIds, setOrderIds] = useState<string[]>([])
  const [dirty, setDirty] = useState(false)

  const [overrideTarget, setOverrideTarget] = useState<{ status: TripStatus; conflicts: string[] } | null>(null)
  const [cancelTarget, setCancelTarget] = useState<TripStatus | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [execution, setExecution] = useState<TripExecution | null>(null)

  const canSeeExecution = hasPermission('driver_workflow.view')
  const canOpenPod = hasPermission('pod.view')

  const applyTrip = useCallback((data: TripDetail) => {
    setTrip(data)
    setDriverId(data.driverId ?? '')
    setVehicleId(data.vehicleId ?? '')
    setTrailerId(data.trailerId ?? '')
    setTripDate(data.tripDate)
    setNotes(data.notes ?? '')
    setOrderIds(data.orders.map((o) => o.transportOrderId))
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
        if (mounted) setLoadError('De rit kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [id, applyTrip])

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

  async function openPod(stopId: string) {
    try {
      const pod = await getPodForStop(id, stopId)
      navigate(`/pods/${pod.id}`)
    } catch {
      showError('De POD kon niet worden geopend.')
    }
  }

  useEffect(() => {
    let mounted = true
    searchDrivers({ isActive: true, page: 1, pageSize: 200 })
      .then((data) => {
        if (mounted) setDrivers(data.items)
      })
      .catch(() => {})
    getVehicleOptions()
      .then((data) => {
        if (mounted) setVehicles(data)
      })
      .catch(() => {})
    getTrailerOptions()
      .then((data) => {
        if (mounted) setTrailers(data)
      })
      .catch(() => {})
    searchTransportOrders({ status: 'Confirmed', page: 1, pageSize: 100 })
      .then((data) => {
        if (mounted) setAvailableOrders(data.items)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  const editable = trip?.status === 'Draft' && hasPermission('planning.edit')

  function markDirty() {
    setDirty(true)
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
      const updated = await updateTrip(trip.id, {
        tripDate,
        driverId: driverId || null,
        vehicleId: vehicleId || null,
        trailerId: trailerId || null,
        plannedStart: trip.plannedStart,
        plannedEnd: trip.plannedEnd,
        notes: notes.trim() || null,
        orderIds,
      })
      applyTrip(updated)
      showSuccess('Rit bijgewerkt.')
    } catch (err) {
      showError(
        err instanceof ApiError && err.status === 400
          ? 'De rit kon niet worden opgeslagen — controleer de invoer.'
          : 'De rit kon niet worden opgeslagen.',
      )
    } finally {
      setBusy(false)
    }
  }

  async function applyTransition(target: TripStatus, override: boolean) {
    if (!trip) return
    setBusy(true)
    try {
      const updated = await changeTripStatus(trip.id, target, override)
      applyTrip(updated)
      showSuccess(`Rit is nu: ${TRIP_STATUS_LABELS[target]}.`)
      setOverrideTarget(null)
    } catch (err) {
      if (err instanceof ApiError && err.status === 409 && !override) {
        // Blocking conflicts: offer the override path (guarded server-side by permission).
        setOverrideTarget({
          status: target,
          conflicts: trip.conflicts.filter((c) => c.blocking).map((c) => c.description),
        })
      } else if (err instanceof ApiError && err.status === 403) {
        showError('Je hebt geen recht om planningsconflicten te overschrijven.')
        setOverrideTarget(null)
      } else {
        showError('De status kon niet worden gewijzigd.')
        setOverrideTarget(null)
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
      showSuccess('Rit verwijderd.')
      navigate('/planning')
    } catch {
      showError('De rit kon niet worden verwijderd.')
      setConfirmDelete(false)
    }
  }

  if (loadError) return <ErrorState message={loadError} />
  if (!trip) return <LoadingState message="Rit laden..." />

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

  const attachable = availableOrders.filter((o) => !orderIds.includes(o.id))

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Planning', to: '/planning' }, { label: trip.tripNumber }]} />
      <PageHeader
        title={`${trip.tripNumber} — ${trip.tripDate}`}
        action={
          <span className="pl-header-actions">
            <Badge tone={TRIP_STATUS_TONE[trip.status]}>{TRIP_STATUS_LABELS[trip.status]}</Badge>
            {hasPermission('planning.edit') &&
              trip.allowedTransitions.map((target) => (
                <Button
                  key={target}
                  variant={target === 'Cancelled' ? 'secondary' : 'primary'}
                  onClick={() => (target === 'Cancelled' ? setCancelTarget(target) : void applyTransition(target, false))}
                  disabled={busy || dirty}
                >
                  {TRIP_TRANSITION_LABELS[target]}
                </Button>
              ))}
          </span>
        }
      />

      {dirty && <p className="pl-dirty-hint">Er zijn niet-opgeslagen wijzigingen — sla eerst op voor je de status wijzigt.</p>}

      <section className="pl-section">
        <h2>Toewijzing</h2>
        <div className="pl-assign">
          <FormField label="Datum" htmlFor="tr-date">
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
          <FormField label="Chauffeur" htmlFor="tr-driver">
            <select
              id="tr-driver"
              value={driverId}
              onChange={(e) => {
                setDriverId(e.target.value)
                markDirty()
              }}
              disabled={!editable || busy}
            >
              <option value="">Geen</option>
              {drivers.map((driver) => (
                <option key={driver.id} value={driver.id}>
                  {driver.fullName} ({driver.driverNumber})
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Voertuig" htmlFor="tr-vehicle">
            <select
              id="tr-vehicle"
              value={vehicleId}
              onChange={(e) => {
                setVehicleId(e.target.value)
                markDirty()
              }}
              disabled={!editable || busy}
            >
              <option value="">Geen</option>
              {vehicles.map((vehicle) => (
                <option key={vehicle.id} value={vehicle.id}>
                  {vehicle.internalNumber} ({vehicle.licensePlate})
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Oplegger" htmlFor="tr-trailer">
            <select
              id="tr-trailer"
              value={trailerId}
              onChange={(e) => {
                setTrailerId(e.target.value)
                markDirty()
              }}
              disabled={!editable || busy}
            >
              <option value="">Geen</option>
              {trailers.map((trailer) => (
                <option key={trailer.id} value={trailer.id}>
                  {trailer.internalNumber} ({trailer.licensePlate})
                </option>
              ))}
            </select>
          </FormField>
        </div>
        <FormField label="Notities" htmlFor="tr-notes">
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
        <h2>Opdrachten ({orderIds.length})</h2>
        {attachedSummaries.length === 0 && <p className="placeholder-text">Nog geen opdrachten op deze rit.</p>}
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
                      Verwijderen
                    </button>
                  </span>
                )}
              </li>
            ))}
          </ol>
        )}

        {editable && attachable.length > 0 && (
          <div className="pl-add-order">
            <select
              value=""
              onChange={(e) => {
                if (e.target.value) {
                  setOrderIds((ids) => [...ids, e.target.value])
                  markDirty()
                }
              }}
              disabled={busy}
              aria-label="Opdracht toevoegen"
            >
              <option value="">+ Bevestigde opdracht toevoegen…</option>
              {attachable.map((order) => (
                <option key={order.id} value={order.id}>
                  {order.orderNumber} — {order.customerName} ({order.firstLoadingCity ?? '?'} → {order.lastUnloadingCity ?? '?'})
                </option>
              ))}
            </select>
          </div>
        )}
      </section>

      {execution && execution.stops.length > 0 && (
        <section className="pl-section">
          <h2>
            Uitvoering ({execution.completedCount}/{execution.totalCount} stops afgehandeld)
          </h2>
          <ol className="pl-execution-stops">
            {execution.stops.map((stop) => (
              <li key={stop.transportOrderStopId}>
                <Badge tone={stop.stopType === 'Loading' ? 'info' : 'success'}>
                  {stop.stopType === 'Loading' ? 'Laden' : 'Lossen'}
                </Badge>{' '}
                <span className="pl-execution-location">
                  {stop.locationName}
                  {stop.city && stop.locationName !== stop.city ? ` — ${stop.city}` : ''}
                </span>{' '}
                <Badge tone={STOP_EXECUTION_TONE[stop.status]}>
                  {STOP_EXECUTION_ICONS[stop.status]} {STOP_EXECUTION_LABELS[stop.status]}
                </Badge>
                {stop.lateArrivalReason && <span className="pl-execution-late"> · te laat: {stop.lateArrivalReason}</span>}
                {stop.hasPod && canOpenPod && (
                  <button type="button" className="pl-link" onClick={() => void openPod(stop.transportOrderStopId)}>
                    ✍ POD bekijken
                  </button>
                )}
              </li>
            ))}
          </ol>
        </section>
      )}

      <section className="pl-section">
        <h2>Conflicten</h2>
        {trip.conflicts.length === 0 && <p className="pl-ok">✓ Geen conflicten.</p>}
        {trip.conflicts.length > 0 && (
          <ul className="pl-conflicts">
            {trip.conflicts.map((conflict, index) => (
              <li key={`${conflict.code}-${index}`}>
                <Badge tone={conflict.blocking ? 'danger' : 'warning'}>
                  {conflict.blocking ? 'Blokkerend' : 'Waarschuwing'}
                </Badge>{' '}
                {conflict.description}
              </li>
            ))}
          </ul>
        )}
      </section>

      <div className="pl-detail-actions">
        {editable && dirty && (
          <Button onClick={() => void handleSave()} disabled={busy}>
            {busy ? 'Opslaan…' : 'Opslaan'}
          </Button>
        )}
        {(trip.status === 'Draft' || trip.status === 'Cancelled') && hasPermission('planning.edit') && (
          <Button variant="secondary" onClick={() => setConfirmDelete(true)} disabled={busy}>
            Verwijderen
          </Button>
        )}
      </div>

      {overrideTarget && (
        <ConfirmDialog
          title="Conflicten overschrijven?"
          message={`De rit heeft blokkerende conflicten:\n\n${overrideTarget.conflicts.join('\n')}\n\nToch inplannen? Dit vereist het recht 'Planningsbeperkingen overschrijven'.`}
          confirmLabel="Toch inplannen"
          destructive
          onConfirm={() => void applyTransition(overrideTarget.status, true)}
          onCancel={() => setOverrideTarget(null)}
        />
      )}

      {cancelTarget && (
        <ConfirmDialog
          title="Rit annuleren"
          message={`Weet je zeker dat je rit ${trip.tripNumber} wilt annuleren? Gekoppelde opdrachten komen weer vrij voor planning.`}
          confirmLabel="Annuleren"
          destructive
          onConfirm={() => void applyTransition(cancelTarget, false)}
          onCancel={() => setCancelTarget(null)}
        />
      )}

      {confirmDelete && (
        <ConfirmDialog
          title="Rit verwijderen"
          message={`Weet je zeker dat je rit ${trip.tripNumber} wilt verwijderen?`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(false)}
        />
      )}
    </div>
  )
}
