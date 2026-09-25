import { useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { describeApiError } from '../../../api/problemDetails'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { assignDriver, assignTrailer, assignVehicle } from '../../planning-center/api/planningCenterApi'
import { ConflictDialog } from '../../planning-center/components/ConflictDialog'
import type { PlanningRejection } from '../../planning-center/types'
import { getTrip } from '../../planning/api/planningApi'
import { buildDriverOptions, buildTrailerOptions, buildVehicleOptions, joinParts, lookupFixedVehicleId } from '../../planning/resourceOptions'
import {
  CONFLICT_SEVERITY_META,
  TRIP_STATUS_LABELS,
  TRIP_STATUS_TONE,
  type PlanningConflict,
  type TripDetail,
  type TripStatus,
} from '../../planning/types'
import { usePlanningResources } from '../../planning/usePlanningResources'
import {
  EMPTY_VEHICLE_DRAFT,
  vehicleDraftAfterDriverChange,
  vehicleDraftAfterManualPick,
  type VehicleDraft,
} from '../../planning/vehicleSelection'
import { getDossier, planDossierActivity } from '../api/dossiersApi'
import { formatDate } from '../dossierDisplay'
import type { DossierActivity, DossierActivityAssignment, DossierDetail } from '../types'
import './activity-planning.css'

interface ActivityPlanningBlockProps {
  dossier: DossierDetail
  /** The LIVE activity (re-resolved from the dossier after every apply). */
  activity: DossierActivity
  /** Open dossier; a closed one is read-only. */
  isOpen: boolean
  /** Receives the refreshed dossier after a successful plan/assignment (no toast, no close). */
  onApply: (dossier: DossierDetail) => void
  /** 409 on the dossier token: the page shows the rebase banner. Returns true when handled. */
  onConflict: (err: unknown) => boolean
  /** Unsaved picker changes, so the hosting drawer guards its close. */
  onDirtyChange?: (dirty: boolean) => void
}

/**
 * D1 "Planning" block of the activity detail. Driver, vehicle and trailer live ONLY on the trip:
 * without a trip the block plans the activity's order onto a new Draft trip (plan endpoint); with
 * a trip it edits that trip through the existing targeted trip endpoints, conflict rules and
 * override-with-reason flow included. Nothing here is a second source of assignment.
 */
export function ActivityPlanningBlock(props: ActivityPlanningBlockProps) {
  const { t } = useLocale()
  const { activity } = props
  if (!activity.hasStops) return null

  return (
    <section className="dossier-planning" aria-label={t('dossierActivities.planning.title')}>
      <h3>{t('dossierActivities.planning.title')}</h3>
      {!activity.linkedTransportOrderId ? (
        <p className="placeholder-text">{t('dossierActivities.planning.needsOrder')}</p>
      ) : (
        // Keyed on the trip: planning the activity (no trip → trip) starts from the trip's values.
        <PlanningForm key={activity.assignment?.tripId ?? 'unplanned'} {...props} />
      )}
    </section>
  )
}

function vehicleLabel(number: string | null, plate: string | null): string | undefined {
  if (!number) return plate ?? undefined
  return plate ? `${number} (${plate})` : number
}

function draftFromAssignment(assignment: DossierActivityAssignment | null): VehicleDraft {
  return assignment?.vehicleId
    ? { vehicleId: assignment.vehicleId, source: assignment.vehicleSelectionSource ?? null }
    : EMPTY_VEHICLE_DRAFT
}

function PlanningForm({ dossier, activity, isOpen, onApply, onConflict, onDirtyChange }: ActivityPlanningBlockProps) {
  const { t } = useLocale()
  const toast = useToast()
  const { hasPermission } = useAuth()
  const assignment = activity.assignment ?? null
  const canWrite = isOpen && (assignment ? hasPermission('planning.edit') : hasPermission('planning.create'))

  // The trip itself is only needed for writing (its version token, status and conflicts); the
  // read-only view renders from the assignment the dossier already carries.
  const [trip, setTrip] = useState<TripDetail | null>(null)
  const [tripFailed, setTripFailed] = useState(false)
  const tripId = assignment?.tripId ?? null
  useEffect(() => {
    if (!tripId || !canWrite) return
    let mounted = true
    getTrip(tripId)
      .then((data) => {
        if (mounted) setTrip(data)
      })
      .catch(() => {
        if (mounted) setTripFailed(true)
      })
    return () => {
      mounted = false
    }
  }, [tripId, canWrite])

  const tripStatus = (trip?.status ?? assignment?.tripStatus ?? null) as TripStatus | null
  // Same rule as the planning board: only a Draft or Planned trip is re-assigned.
  const editable = canWrite && (!assignment || (trip !== null && (trip.status === 'Draft' || trip.status === 'Planned')))

  const { drivers, vehicles, trailers, status: listStatus } = usePlanningResources(canWrite)
  const driverOptions = useMemo(() => buildDriverOptions(drivers, t), [drivers, t])
  const vehicleOptions = useMemo(() => buildVehicleOptions(vehicles), [vehicles])
  const trailerOptions = useMemo(() => buildTrailerOptions(trailers), [trailers])

  // Draft edits kept locally until the planner saves.
  const [driverId, setDriverId] = useState(assignment?.driverId ?? '')
  // Vehicle id plus how it got there: a suggestion follows the driver, a manual pick never does.
  const [vehicleDraft, setVehicleDraft] = useState<VehicleDraft>(() => draftFromAssignment(assignment))
  const [trailerId, setTrailerId] = useState(assignment?.trailerId ?? '')
  const [tripDate, setTripDate] = useState(activity.plannedDate ?? '')
  // Guards the fixed-vehicle lookup against an older driver choice answering last.
  const driverLookupSeq = useRef(0)

  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmShared, setConfirmShared] = useState(false)
  const [conflictState, setConflictState] = useState<{ message: string; conflicts: PlanningConflict[] } | null>(null)

  const savedDraft = draftFromAssignment(assignment)
  const dirty = assignment
    ? driverId !== (assignment.driverId ?? '') ||
      vehicleDraft.vehicleId !== savedDraft.vehicleId ||
      (vehicleDraft.vehicleId !== '' && vehicleDraft.source !== savedDraft.source) ||
      trailerId !== (assignment.trailerId ?? '')
    : Boolean(driverId || vehicleDraft.vehicleId || trailerId) || tripDate !== (activity.plannedDate ?? '')

  const dirtyRef = useRef(onDirtyChange)
  useEffect(() => {
    dirtyRef.current = onDirtyChange
  })
  useEffect(() => {
    dirtyRef.current?.(editable && dirty)
    return () => dirtyRef.current?.(false)
  }, [editable, dirty])

  function handleDriverChange(nextDriverId: string | null) {
    setDriverId(nextDriverId ?? '')
    const seq = ++driverLookupSeq.current
    if (!nextDriverId) {
      setVehicleDraft((current) => vehicleDraftAfterDriverChange(current, null))
      return
    }
    void lookupFixedVehicleId(drivers, vehicles, nextDriverId).then((fixedId) => {
      if (seq !== driverLookupSeq.current) return
      setVehicleDraft((current) => vehicleDraftAfterDriverChange(current, fixedId))
    })
  }

  /** "Rit aanmaken en toewijzen": the server creates the Draft trip (or reuses the open one). */
  async function plan() {
    setBusy(true)
    setError(null)
    try {
      const updated = await planDossierActivity(dossier.id, activity.id, {
        tripDate: tripDate || null,
        driverId: driverId || null,
        vehicleId: vehicleDraft.vehicleId || null,
        trailerId: trailerId || null,
        vehicleSelectionSource: vehicleDraft.vehicleId ? (vehicleDraft.source ?? 'Manual') : null,
        version: dossier.version,
      })
      onApply(updated)
      toast.showSuccess(t('dossierActivities.planning.planned'))
    } catch (err) {
      if (onConflict(err)) return
      // Refusals (draft order, no order, closed dossier) arrive as a Dutch 400 message: shown as is.
      setError(describeApiError(err, t('dossierActivities.planning.planFailed')).message)
    } finally {
      setBusy(false)
    }
  }

  /**
   * Saves the changed slots through the targeted trip endpoints, driver first (the server then
   * proposes the fixed vehicle itself), each call with the version the previous one returned. A
   * 409 with conflicts opens the conflict dialog; its override retry continues where this stopped.
   */
  async function saveAssignment(override = false, reason: string | null = null) {
    if (!trip) return
    setBusy(true)
    setError(null)
    let current = trip
    let changed = false
    try {
      if (driverId !== (current.driverId ?? '')) {
        current = await assignDriver(current.id, driverId || null, current.version, override, reason)
        changed = true
      }
      const draftSource = vehicleDraft.vehicleId ? (vehicleDraft.source ?? 'Manual') : null
      const currentSource = current.vehicleId ? (current.vehicleSelectionSource ?? 'Manual') : null
      if (vehicleDraft.vehicleId !== (current.vehicleId ?? '') || draftSource !== currentSource) {
        current = await assignVehicle(current.id, vehicleDraft.vehicleId || null, current.version, override, reason, draftSource)
        changed = true
      }
      if (trailerId !== (current.trailerId ?? '')) {
        current = await assignTrailer(current.id, trailerId || null, current.version, override, reason)
        changed = true
      }
      setConflictState(null)
      // The server may have filled in the fixed vehicle itself: the pickers follow what was saved.
      setDriverId(current.driverId ?? '')
      setVehicleDraft(current.vehicleId ? { vehicleId: current.vehicleId, source: current.vehicleSelectionSource ?? null } : EMPTY_VEHICLE_DRAFT)
      setTrailerId(current.trailerId ?? '')
      if (changed) toast.showSuccess(t('dossierActivities.planning.saved', { tripNumber: current.tripNumber }))
    } catch (err) {
      const body = err instanceof ApiError && err.status === 409 ? ((err.body ?? {}) as PlanningRejection) : null
      if (body?.conflicts && body.conflicts.length > 0) {
        setConflictState({ message: body.message ?? t('planningCenter.page.rejectedByConflicts'), conflicts: body.conflicts })
      } else if (body?.staleVersion) {
        setConflictState(null)
        setError(t('planningCenter.page.staleVersion'))
        void getTrip(current.id).then(setTrip).catch(() => {})
      } else {
        setConflictState(null)
        setError(describeApiError(err, t('dossierActivities.planning.saveFailed')).message)
      }
    } finally {
      setTrip(current)
      setBusy(false)
    }
    if (!changed) return
    // The trip endpoints answer with the trip; cards and Overzicht read the DOSSIER projection.
    try {
      onApply(await getDossier(dossier.id))
    } catch {
      setError(t('dossierActivities.planning.refreshFailed'))
    }
  }

  function requestSave() {
    if (assignment && assignment.otherOrderCount > 0) setConfirmShared(true)
    else void saveAssignment()
  }

  const sharedWarning =
    assignment && assignment.otherOrderCount > 0
      ? t('dossierActivities.planning.sharedTripWarning', { tripNumber: assignment.tripNumber, count: assignment.otherOrderCount })
      : null
  const conflicts = trip?.conflicts ?? []

  return (
    <>
      {assignment ? (
        <p className="dossier-planning-trip">
          <span>{t('dossierActivities.planning.trip', { tripNumber: assignment.tripNumber, date: formatDate(assignment.tripDate) })}</span>
          {tripStatus && TRIP_STATUS_LABELS[tripStatus] && <Badge tone={TRIP_STATUS_TONE[tripStatus]}>{t(TRIP_STATUS_LABELS[tripStatus])}</Badge>}
          <Link to={`/planning/${assignment.tripId}`}>{t('dossierActivities.planning.openTrip')}</Link>
        </p>
      ) : (
        <p className="dossier-planning-hint">{t('dossierActivities.planning.noTrip')}</p>
      )}
      {assignment && assignment.tripCount > 1 && (
        <p className="dossier-planning-hint">{t('dossierActivities.planning.multipleTrips', { count: assignment.tripCount })}</p>
      )}

      {error && (
        <p className="dossier-planning-error" role="alert">
          {error}
        </p>
      )}

      {!editable && (
        <>
          <dl className="dossier-planning-readonly">
            <div>
              <dt>{t('dossierActivities.card.driver')}</dt>
              <dd>{assignment?.driverName ?? t('dossierActivities.card.notAssigned')}</dd>
            </div>
            <div>
              <dt>{t('dossierActivities.planning.vehicle')}</dt>
              <dd>
                {vehicleLabel(assignment?.vehicleNumber ?? null, assignment?.vehiclePlate ?? null) ?? t('dossierActivities.card.notAssigned')}
                {assignment?.vehicleId && assignment.vehicleSelectionSource === 'Suggested' && (
                  <Badge tone="info">{t('dossierActivities.card.suggested')}</Badge>
                )}
              </dd>
            </div>
            <div>
              <dt>{t('dossierActivities.planning.trailer')}</dt>
              <dd>{vehicleLabel(assignment?.trailerNumber ?? null, assignment?.trailerPlate ?? null) ?? t('dossierActivities.card.notAssigned')}</dd>
            </div>
          </dl>
          <p className="dossier-planning-hint">
            {!canWrite
              ? isOpen
                ? t('dossierActivities.planning.readOnlyPermission')
                : t('dossierActivities.planning.readOnlyClosed')
              : tripFailed
                ? t('dossierActivities.planning.tripLoadFailed')
                : trip
                  ? t('dossierActivities.planning.readOnlyStatus')
                  : t('dossierActivities.planning.tripLoading')}
          </p>
        </>
      )}

      {editable && (
        <>
          {sharedWarning && (
            <p className="dossier-planning-warning" role="note">
              ⚠ {sharedWarning}
            </p>
          )}
          <div className="dossier-planning-fields">
            {!assignment && (
              <FormField label={t('dossierActivities.planning.tripDate')} htmlFor="ap-date" hint={t('dossierActivities.planning.tripDateHint')}>
                <input id="ap-date" type="date" value={tripDate} onChange={(event) => setTripDate(event.target.value)} disabled={busy} />
              </FormField>
            )}
            <FormField label={t('dossierActivities.card.driver')} htmlFor="ap-driver">
              <SearchableSelect
                id="ap-driver"
                value={driverId || null}
                onChange={handleDriverChange}
                options={driverOptions}
                // A driver that left the active list (or a list that failed) still shows by name.
                selectedLabel={driverId === assignment?.driverId ? (assignment?.driverName ?? undefined) : undefined}
                placeholder={t('planning.detail.driverSearchPlaceholder')}
                isLoading={listStatus.drivers === 'loading'}
                errorMessage={listStatus.drivers === 'error' ? t('planning.detail.driversLoadError') : null}
                disabled={busy}
              />
            </FormField>
            <FormField
              label={t('dossierActivities.planning.vehicle')}
              htmlFor="ap-vehicle"
              hint={vehicleDraft.source === 'Suggested' ? t('dossierActivities.planning.vehicleSuggestedHint') : undefined}
            >
              <SearchableSelect
                id="ap-vehicle"
                value={vehicleDraft.vehicleId || null}
                onChange={(next) => {
                  driverLookupSeq.current++
                  setVehicleDraft(vehicleDraftAfterManualPick(next))
                }}
                options={vehicleOptions}
                selectedLabel={
                  vehicleDraft.vehicleId === assignment?.vehicleId
                    ? vehicleLabel(assignment?.vehicleNumber ?? null, assignment?.vehiclePlate ?? null)
                    : undefined
                }
                placeholder={t('planning.detail.vehicleSearchPlaceholder')}
                isLoading={listStatus.vehicles === 'loading'}
                errorMessage={listStatus.vehicles === 'error' ? t('planning.detail.vehiclesLoadError') : null}
                disabled={busy}
              />
            </FormField>
            <FormField label={t('dossierActivities.planning.trailer')} htmlFor="ap-trailer">
              <SearchableSelect
                id="ap-trailer"
                value={trailerId || null}
                onChange={(next) => setTrailerId(next ?? '')}
                options={trailerOptions}
                selectedLabel={
                  trailerId === assignment?.trailerId
                    ? vehicleLabel(assignment?.trailerNumber ?? null, assignment?.trailerPlate ?? null)
                    : undefined
                }
                placeholder={t('planning.detail.trailerSearchPlaceholder')}
                isLoading={listStatus.trailers === 'loading'}
                errorMessage={listStatus.trailers === 'error' ? t('planning.detail.trailersLoadError') : null}
                disabled={busy}
              />
            </FormField>
          </div>
          <p className="dossier-planning-actions">
            {assignment ? (
              <Button onClick={requestSave} disabled={busy || !dirty}>
                {t('dossierActivities.planning.save')}
              </Button>
            ) : (
              <Button onClick={() => void plan()} disabled={busy}>
                {t('dossierActivities.planning.plan')}
              </Button>
            )}
            {!assignment && <span className="dossier-planning-hint">{t('dossierActivities.planning.optionalHint')}</span>}
          </p>
        </>
      )}

      {conflicts.length > 0 && (
        <ul className="dossier-planning-conflicts" aria-label={t('planning.detail.conflictsTitle')}>
          {conflicts.map((conflict, index) => (
            <li key={`${conflict.code}-${index}`}>
              <Badge tone={CONFLICT_SEVERITY_META[conflict.severity]?.tone ?? (conflict.blocking ? 'danger' : 'warning')}>
                {t(CONFLICT_SEVERITY_META[conflict.severity]?.label ??
                  (conflict.blocking ? 'planning.conflictSeverity.Blocking' : 'planning.conflictSeverity.Warning'))}
              </Badge>{' '}
              {joinParts([conflict.description, conflict.suggestedAction], ' — ')}
            </li>
          ))}
        </ul>
      )}

      {confirmShared && sharedWarning && (
        <ConfirmDialog
          title={t('dossierActivities.planning.sharedTripTitle')}
          message={sharedWarning}
          confirmLabel={t('dossierActivities.planning.sharedTripConfirm')}
          busy={busy}
          onCancel={() => setConfirmShared(false)}
          onConfirm={() => {
            setConfirmShared(false)
            void saveAssignment()
          }}
        />
      )}

      {conflictState && (
        <ConflictDialog
          message={conflictState.message}
          conflicts={conflictState.conflicts}
          busy={busy}
          onClose={() => setConflictState(null)}
          onOverride={(reason) => void saveAssignment(true, reason)}
        />
      )}
    </>
  )
}
