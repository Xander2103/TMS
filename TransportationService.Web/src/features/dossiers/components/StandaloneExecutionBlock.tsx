import { useEffect, useMemo, useRef, useState } from 'react'
import { ApiError } from '../../../api/apiClient'
import { describeApiError } from '../../../api/problemDetails'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { ConflictDialog } from '../../planning-center/components/ConflictDialog'
import type { PlanningRejection } from '../../planning-center/types'
import { buildDriverOptions, buildVehicleOptions, joinParts } from '../../planning/resourceOptions'
import { CONFLICT_SEVERITY_META, type PlanningConflict } from '../../planning/types'
import { usePlanningResources } from '../../planning/usePlanningResources'
import {
  assignStandaloneActivity,
  cancelStandaloneActivity,
  transitionStandaloneActivity,
  type StandaloneActivityTransition,
} from '../api/dossiersApi'
import { EXECUTION_STATUS_TONE } from '../activityDisplay'
import { formatDate } from '../dossierDisplay'
import type { DossierActivity, DossierActivityExecutionStatus, DossierDetail } from '../types'
import { DossierReasonDialog } from './DossierReasonDialog'
import './activity-planning.css'

interface StandaloneExecutionBlockProps {
  dossier: DossierDetail
  /** The LIVE standalone activity (re-resolved from the dossier after every apply). */
  activity: DossierActivity
  /** dossiers.manage — cancel / reopen, and start/complete together with planning.edit. */
  canManage: boolean
  /** Unsaved fields in the hosting drawer: a status change would silently drop them. */
  drawerDirty: boolean
  /** Receives the refreshed dossier (no toast from the caller, the drawer stays open). */
  onApply: (dossier: DossierDetail) => void
  /** 409 on the dossier token: the page shows the rebase banner. Returns true when handled. */
  onConflict: (err: unknown) => boolean
  /** Unsaved driver/vehicle picks, so the hosting drawer guards its close. */
  onDirtyChange?: (dirty: boolean) => void
}

/**
 * T-H7 — execution of a STANDALONE activity (crane work on site, plateau, storage): its status
 * (Gepland → Bezig → Afgerond, or Geannuleerd with a reason), the actions to move it, and — for
 * operational (planning-relevant) types — the driver/operator and vehicle/equipment, judged by the
 * same planning conflict engine and override-with-reason flow as a trip.
 */
export function StandaloneExecutionBlock({
  dossier, activity, canManage, drawerDirty, onApply, onConflict, onDirtyChange,
}: StandaloneExecutionBlockProps) {
  const { t } = useLocale()
  const toast = useToast()
  const { hasPermission } = useAuth()
  const status: DossierActivityExecutionStatus = activity.executionStatus ?? 'Planned'
  const isOpen = dossier.status === 'Open'
  const canPlanEdit = hasPermission('planning.edit')
  const canTransition = isOpen && (canManage || canPlanEdit)
  const canAssign = isOpen && activity.isExecutable === true && (canPlanEdit || hasPermission('planning.create'))
  const resolved = status === 'Completed' || status === 'Cancelled'

  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmCancel, setConfirmCancel] = useState(false)
  const [warnings, setWarnings] = useState<PlanningConflict[]>([])
  const [conflictState, setConflictState] = useState<{ message: string; conflicts: PlanningConflict[] } | null>(null)

  const [driverId, setDriverId] = useState(activity.driverId ?? '')
  const [vehicleId, setVehicleId] = useState(activity.vehicleId ?? '')
  const assignEditable = canAssign && !resolved
  const { drivers, vehicles, status: listStatus } = usePlanningResources(assignEditable, activity.plannedDate)
  const driverOptions = useMemo(() => buildDriverOptions(drivers, t), [drivers, t])
  const vehicleOptions = useMemo(() => buildVehicleOptions(vehicles, t), [vehicles, t])
  const assignDirty = driverId !== (activity.driverId ?? '') || vehicleId !== (activity.vehicleId ?? '')

  const dirtyRef = useRef(onDirtyChange)
  useEffect(() => {
    dirtyRef.current = onDirtyChange
  })
  useEffect(() => {
    dirtyRef.current?.(assignEditable && assignDirty)
    return () => dirtyRef.current?.(false)
  }, [assignEditable, assignDirty])

  function guardDirty(): boolean {
    if (drawerDirty || assignDirty) {
      setError(t('dossierActivities.execution.saveFirst'))
      return false
    }
    return true
  }

  async function run(action: () => Promise<DossierDetail>, successKey: string) {
    setBusy(true)
    setError(null)
    try {
      const updated = await action()
      onApply(updated)
      toast.showSuccess(t(successKey))
      if (updated.status === 'Closed') toast.showSuccess(t('dossierActivities.execution.dossierConfirmed'))
    } catch (err) {
      if (onConflict(err)) return
      setError(describeApiError(err, t('dossierActivities.execution.failed')).message)
    } finally {
      setBusy(false)
    }
  }

  function transition(kind: StandaloneActivityTransition) {
    if (!guardDirty()) return
    const successKey = kind === 'start' ? 'dossierActivities.execution.started' : kind === 'complete' ? 'dossierActivities.execution.completed' : 'dossierActivities.execution.reopened'
    void run(() => transitionStandaloneActivity(dossier.id, activity.id, kind, dossier.version), successKey)
  }

  async function assign(override = false, reason: string | null = null) {
    setBusy(true)
    setError(null)
    try {
      const result = await assignStandaloneActivity(dossier.id, activity.id, {
        driverId: driverId || null,
        vehicleId: vehicleId || null,
        override,
        overrideReason: reason,
        version: dossier.version,
      })
      setConflictState(null)
      setWarnings(result.warnings ?? [])
      onApply(result.dossier)
      toast.showSuccess(t('dossierActivities.execution.assigned'))
    } catch (err) {
      const body = err instanceof ApiError && err.status === 409 ? ((err.body ?? {}) as PlanningRejection) : null
      if (body?.conflicts && body.conflicts.length > 0) {
        setConflictState({ message: body.message ?? t('planningCenter.page.rejectedByConflicts'), conflicts: body.conflicts })
      } else if (!onConflict(err)) {
        setConflictState(null)
        setError(describeApiError(err, t('dossierActivities.execution.assignFailed')).message)
      }
    } finally {
      setBusy(false)
    }
  }

  const facts: string[] = []
  if (status === 'Completed' && activity.completedAt) {
    facts.push(t('dossierActivities.execution.completedOn', { date: formatDate(activity.completedAt.slice(0, 10)) }))
    if (activity.completedByName) facts.push(activity.completedByName)
  }

  return (
    <section className="dossier-planning" aria-label={t('dossierActivities.execution.title')}>
      <h3>
        {t('dossierActivities.execution.title')}{' '}
        <Badge tone={EXECUTION_STATUS_TONE[status]}>{t(`dossierActivities.execution.status.${status}`)}</Badge>
      </h3>
      {facts.length > 0 && <p className="dossier-planning-hint">{facts.join(' · ')}</p>}
      {status === 'Cancelled' && activity.cancellationReason && (
        <p className="dossier-planning-hint">{t('dossierActivities.execution.cancelledReason', { reason: activity.cancellationReason })}</p>
      )}

      {error && (
        <p className="dossier-planning-error" role="alert">
          {error}
        </p>
      )}

      {activity.isExecutable && (
        <div className="dossier-planning-fields">
          <FormField label={t('dossierActivities.card.driver')} htmlFor="ae-driver">
            {assignEditable ? (
              <SearchableSelect
                id="ae-driver"
                value={driverId || null}
                onChange={(next) => setDriverId(next ?? '')}
                options={driverOptions}
                selectedLabel={driverId === activity.driverId ? (activity.driverName ?? undefined) : undefined}
                placeholder={t('planning.detail.driverSearchPlaceholder')}
                isLoading={listStatus.drivers === 'loading'}
                errorMessage={listStatus.drivers === 'error' ? t('planning.detail.driversLoadError') : null}
                disabled={busy}
              />
            ) : (
              <span id="ae-driver">{activity.driverName ?? t('dossierActivities.card.notAssigned')}</span>
            )}
          </FormField>
          <FormField label={t('dossierActivities.execution.vehicle')} htmlFor="ae-vehicle">
            {assignEditable ? (
              <SearchableSelect
                id="ae-vehicle"
                value={vehicleId || null}
                onChange={(next) => setVehicleId(next ?? '')}
                options={vehicleOptions}
                selectedLabel={
                  vehicleId === activity.vehicleId && activity.vehicleNumber
                    ? `${activity.vehicleNumber} (${activity.vehiclePlate ?? ''})`
                    : undefined
                }
                placeholder={t('planning.detail.vehicleSearchPlaceholder')}
                isLoading={listStatus.vehicles === 'loading'}
                errorMessage={listStatus.vehicles === 'error' ? t('planning.detail.vehiclesLoadError') : null}
                disabled={busy}
              />
            ) : (
              <span id="ae-vehicle">
                {activity.vehicleNumber ? `${activity.vehicleNumber} (${activity.vehiclePlate ?? ''})` : t('dossierActivities.card.notAssigned')}
              </span>
            )}
          </FormField>
        </div>
      )}
      {activity.isExecutable && activity.requiresCrane && (
        <p className="dossier-planning-hint">{t('dossierActivities.execution.requiresCrane')}</p>
      )}

      {warnings.length > 0 && (
        <ul className="dossier-planning-conflicts" aria-label={t('planning.detail.conflictsTitle')}>
          {warnings.map((conflict, index) => (
            <li key={`${conflict.code}-${index}`}>
              <Badge tone={CONFLICT_SEVERITY_META[conflict.severity]?.tone ?? 'warning'}>
                {t(CONFLICT_SEVERITY_META[conflict.severity]?.label ?? 'planning.conflictSeverity.Warning')}
              </Badge>{' '}
              {joinParts([conflict.description, conflict.suggestedAction], ' — ')}
            </li>
          ))}
        </ul>
      )}

      <p className="dossier-planning-actions">
        {assignEditable && (
          <Button variant="secondary" onClick={() => void assign()} disabled={busy || !assignDirty}>
            {t('dossierActivities.execution.assign')}
          </Button>
        )}
        {canTransition && status === 'Planned' && activity.isExecutable && (
          <Button variant="secondary" onClick={() => transition('start')} disabled={busy}>
            {t('dossierActivities.execution.start')}
          </Button>
        )}
        {canTransition && !resolved && (
          <Button onClick={() => transition('complete')} disabled={busy}>
            {t('dossierActivities.execution.complete')}
          </Button>
        )}
        {isOpen && canManage && !resolved && (
          <Button
            variant="danger"
            onClick={() => {
              if (guardDirty()) setConfirmCancel(true)
            }}
            disabled={busy}
          >
            {t('dossierActivities.execution.cancel')}
          </Button>
        )}
        {isOpen && canManage && resolved && (
          <Button variant="secondary" onClick={() => transition('reopen')} disabled={busy}>
            {t('dossierActivities.execution.reopen')}
          </Button>
        )}
      </p>

      {confirmCancel && (
        <DossierReasonDialog
          title={t('dossierActivities.execution.cancelTitle')}
          intro={t('dossierActivities.execution.cancelIntro')}
          reasonLabel={t('dossierActivities.execution.cancelReason')}
          confirmLabel={t('dossierActivities.execution.cancelTitle')}
          destructive
          onClose={() => setConfirmCancel(false)}
          onSubmit={async (reason) => {
            try {
              const updated = await cancelStandaloneActivity(dossier.id, activity.id, reason, dossier.version)
              setConfirmCancel(false)
              onApply(updated)
              toast.showSuccess(t('dossierActivities.execution.cancelled'))
            } catch (err) {
              if (onConflict(err)) {
                setConfirmCancel(false)
                return
              }
              throw new Error(describeApiError(err, t('dossierActivities.execution.failed')).message, { cause: err })
            }
          }}
        />
      )}

      {conflictState && (
        <ConflictDialog
          message={conflictState.message}
          conflicts={conflictState.conflicts}
          busy={busy}
          onClose={() => setConflictState(null)}
          onOverride={(reason) => void assign(true, reason)}
        />
      )}
    </section>
  )
}
