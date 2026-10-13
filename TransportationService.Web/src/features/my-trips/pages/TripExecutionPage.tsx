import { useEffect, useState, type FormEvent } from 'react'
import { useParams } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { ApiError } from '../../../api/apiClient'
import { localizeApiError } from '../../../api/problemDetails'
import { formatTime } from '../../../utils/dates'
import { OfflineQueuedError } from '../../driver/offlineActions'
import { TRIP_STATUS_LABELS, TRIP_STATUS_TONE } from '../../planning/types'
import { STOP_TYPE_LABELS } from '../../transport-orders/types'
import { ScanPanel } from '../../scanning/components/ScanPanel'
import { ReportExceptionDialog } from '../../exceptions/components/ReportExceptionDialog'
import { PodDialog } from '../../pod/components/PodDialog'
import { completeStop, getStopHistory, getTripExecution, transitionStop } from '../api/myTripsApi'
import {
  STOP_EXECUTION_ICONS,
  STOP_EXECUTION_LABELS,
  STOP_EXECUTION_TONE,
  STOP_PRIMARY_ACTION_ORDER,
  STOP_REASON_REQUIRED,
  STOP_TRANSITION_ACTION_LABELS,
  effectiveLatestBound,
  type ExecutionStop,
  type StopExecutionStatus,
  type StopStatusHistoryEntry,
  type TripExecution,
} from '../types'
import './my-trips.css'

/**
 * Wave 1 fix A (A11): this page used to shadow `formatTime` with `value.slice(11, 16)`, i.e. the
 * RAW UTC clock. That was accidentally right while the form stored the typed wall clock tagged
 * with a "Z"; since C-03 the wire carries a real instant, so the slice showed the driver every
 * window two hours early in summer. There is one clock in this app and it is the tenant's.
 */

/** Whether this transition records the arrival moment (explicit or via the backend's arrival bridge). */
function recordsArrival(stop: ExecutionStop, to: StopExecutionStatus): boolean {
  if (to === 'Arrived') return true
  return !stop.arrivedAt && ['Loading', 'Unloading', 'Loaded', 'Completed', 'PartiallyCompleted'].includes(to)
}

function lateBoundPassed(stop: ExecutionStop): boolean {
  const bound = effectiveLatestBound(stop)
  return bound !== null && new Date(bound).getTime() < Date.now()
}

/** Vertaalsleutels per uitzonderingsstatus voor de reden-modal. */
const REASON_MODAL_TITLE_KEYS: Record<string, string> = {
  Skipped: 'myTrips.execution.reasonTitle.Skipped',
  Failed: 'myTrips.execution.reasonTitle.Failed',
  PartiallyCompleted: 'myTrips.execution.reasonTitle.PartiallyCompleted',
}

interface ReasonTarget {
  stop: ExecutionStop
  toStatus: StopExecutionStatus
  lateReason: boolean
}

/** Stop-by-stop driver workflow over the controlled status machine, with POD capture on completion. */
export function TripExecutionPage() {
  const { id = '' } = useParams<{ id: string }>()
  const { showSuccess, showError } = useToast()
  const { hasPermission } = useAuth()
  const { t } = useLocale()

  const [execution, setExecution] = useState<TripExecution | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [completeTarget, setCompleteTarget] = useState<ExecutionStop | null>(null)
  const [podSignedBy, setPodSignedBy] = useState('')
  const [completeRemarks, setCompleteRemarks] = useState('')
  const [completeLateReason, setCompleteLateReason] = useState('')
  const [packageGateMessage, setPackageGateMessage] = useState<string | null>(null)
  const [packageOverrideReason, setPackageOverrideReason] = useState('')

  const [reasonTarget, setReasonTarget] = useState<ReasonTarget | null>(null)
  const [reason, setReason] = useState('')

  const [historyStopId, setHistoryStopId] = useState<string | null>(null)
  const [history, setHistory] = useState<Record<string, StopStatusHistoryEntry[]>>({})
  const [scanStop, setScanStop] = useState<ExecutionStop | null>(null)
  const [exceptionTarget, setExceptionTarget] = useState<{ stop: ExecutionStop | null } | null>(null)
  const [podStop, setPodStop] = useState<ExecutionStop | null>(null)

  function formatWindow(from: string | null, to: string | null): string {
    if (from && to) return `${formatTime(from)}–${formatTime(to)}`
    if (from) return t('myTrips.execution.windowFrom', { time: formatTime(from) })
    if (to) return t('myTrips.execution.windowUntil', { time: formatTime(to) })
    return ''
  }

  useEffect(() => {
    let mounted = true
    getTripExecution(id)
      .then((data) => {
        if (!mounted) return
        setExecution(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError(t('myTrips.execution.loadError'))
      })
    return () => {
      mounted = false
    }
  }, [id, t])

  const canExecute = hasPermission('driver_workflow.execute')
  const canScan = hasPermission('scanning.execute')
  const canCorrectScans = hasPermission('scanning.correct')
  const canReportException = hasPermission('exceptions.create')
  const canFinalizePod = hasPermission('pod.finalize')

  async function refreshExecution() {
    try {
      setExecution(await getTripExecution(id))
    } catch {
      // The next action re-fetches anyway.
    }
  }

  function afterUpdate(updated: TripExecution, message: string) {
    setExecution(updated)
    setHistory({})
    setHistoryStopId(null)
    showSuccess(updated.tripStatus === 'Completed' ? t('myTrips.execution.tripCompletedSuffix', { message }) : message)
  }

  async function doTransition(stop: ExecutionStop, toStatus: StopExecutionStatus, transitionReason: string | null) {
    setBusy(true)
    try {
      const updated = await transitionStop(id, stop.transportOrderStopId, {
        toStatus,
        reason: transitionReason,
      })
      afterUpdate(updated, t('myTrips.execution.statusRegistered', { status: t(STOP_EXECUTION_LABELS[toStatus]) }))
      setReasonTarget(null)
      setReason('')
    } catch (err) {
      if (err instanceof OfflineQueuedError) {
        // Actie staat veilig in de wachtrij — meld dit in de taal van de gebruiker.
        showError(t(err.translationKey))
      } else if (err instanceof ApiError && err.code === 'trips.reason_required') {
        // Stabiele foutcode (i18n-wave): de server eist een reden (bv. late aankomst) —
        // nooit meer op de Nederlandse fouttekst sniffen.
        setReasonTarget({ stop, toStatus, lateReason: true })
        showError(localizeApiError(t, err, err.message))
      } else {
        showError(localizeApiError(t, err, t('myTrips.execution.statusChangeFailed')))
      }
    } finally {
      setBusy(false)
    }
  }

  function initiateTransition(stop: ExecutionStop, toStatus: StopExecutionStatus) {
    if (toStatus === 'Completed') {
      setCompleteTarget(stop)
      setPodSignedBy('')
      setCompleteRemarks('')
      setCompleteLateReason('')
      return
    }

    const needsReason = STOP_REASON_REQUIRED.includes(toStatus)
    const needsLateReason = recordsArrival(stop, toStatus) && lateBoundPassed(stop)
    if (needsReason || needsLateReason) {
      setReasonTarget({ stop, toStatus, lateReason: !needsReason && needsLateReason })
      setReason('')
      return
    }

    void doTransition(stop, toStatus, null)
  }

  async function handleReasonSubmit(event: FormEvent) {
    event.preventDefault()
    if (!reasonTarget) return
    if (!reason.trim()) {
      showError(t('myTrips.execution.reasonRequired'))
      return
    }
    await doTransition(reasonTarget.stop, reasonTarget.toStatus, reason.trim())
  }

  async function handleComplete(event: FormEvent) {
    event.preventDefault()
    if (!completeTarget) return
    const needsLateReason = recordsArrival(completeTarget, 'Completed') && lateBoundPassed(completeTarget)
    if (needsLateReason && !completeLateReason.trim()) {
      showError(t('myTrips.execution.lateReasonRequired'))
      return
    }
    setBusy(true)
    try {
      const updated = await completeStop(
        id,
        completeTarget.transportOrderStopId,
        podSignedBy.trim() || null,
        completeRemarks.trim() || null,
        completeLateReason.trim() || null,
        packageOverrideReason.trim() || null,
      )
      afterUpdate(updated, t('myTrips.execution.stopCompleted'))
      setCompleteTarget(null)
      setPackageGateMessage(null)
      setPackageOverrideReason('')
    } catch (err) {
      if (err instanceof OfflineQueuedError) {
        showError(t(err.translationKey))
      } else {
        // The package gate keeps the dialog open: override holders get a reason field.
        // Stabiele foutcode i.p.v. tekst-sniffing (i18n-wave).
        if (err instanceof ApiError && err.code === 'trips.packages_unresolved') {
          setPackageGateMessage(localizeApiError(t, err, err.message))
        }
        showError(localizeApiError(t, err, t('myTrips.execution.completeFailed')))
      }
    } finally {
      setBusy(false)
    }
  }

  async function toggleHistory(stop: ExecutionStop) {
    const stopId = stop.transportOrderStopId
    if (historyStopId === stopId) {
      setHistoryStopId(null)
      return
    }
    setHistoryStopId(stopId)
    if (!history[stopId]) {
      try {
        const entries = await getStopHistory(id, stopId)
        setHistory((prev) => ({ ...prev, [stopId]: entries }))
      } catch {
        showError(t('myTrips.execution.historyLoadFailed'))
        setHistoryStopId(null)
      }
    }
  }

  if (loadError) return <ErrorState message={loadError} />
  if (!execution) return <LoadingState message={t('myTrips.execution.loading')} />

  const executable = execution.tripStatus === 'InProgress' && canExecute

  return (
    <div>
      <Breadcrumbs items={[{ label: t('myTrips.list.title'), to: '/my-trips' }, { label: execution.tripNumber }]} />
      <PageHeader
        title={`${execution.tripNumber} — ${execution.tripDate}`}
        subtitle={`${execution.vehicleNumber ? `${execution.vehicleNumber} (${execution.vehicleLicensePlate})` : t('myTrips.execution.noVehicle')} · ${t('myTrips.execution.stopsHandled', { completed: execution.completedCount, total: execution.totalCount })}`}
        action={<Badge tone={TRIP_STATUS_TONE[execution.tripStatus]}>{t(TRIP_STATUS_LABELS[execution.tripStatus])}</Badge>}
      />

      {execution.tripStatus === 'Planned' && (
        <p className="mt-hint">{t('myTrips.execution.notStartedHint')}</p>
      )}

      {canReportException && execution.tripStatus === 'InProgress' && (
        <div className="mt-trip-actions">
          <Button variant="ghost" onClick={() => setExceptionTarget({ stop: null })} disabled={busy}>
            ⚠ {t('myTrips.execution.reportTripProblem')}
          </Button>
        </div>
      )}

      <ol className="mt-stops">
        {execution.stops.map((stop) => {
          const primary = STOP_PRIMARY_ACTION_ORDER.find((s) => stop.allowedTransitions.includes(s))
          const exceptions = (['PartiallyCompleted', 'Failed', 'Skipped'] as StopExecutionStatus[]).filter((s) =>
            stop.allowedTransitions.includes(s),
          )
          const handlingInstructions = stop.stopType === 'Loading' ? stop.loadingInstructions : stop.unloadingInstructions
          const stopHistory = history[stop.transportOrderStopId]
          const isTerminal = stop.allowedTransitions.length === 0

          return (
            <li key={stop.transportOrderStopId} className={`mt-stop mt-stop-${stop.status.toLowerCase()}`}>
              <div className="mt-stop-head">
                <span className="mt-stop-type">
                  <Badge tone={stop.stopType === 'Loading' ? 'info' : 'success'}>{t(STOP_TYPE_LABELS[stop.stopType])}</Badge>
                </span>
                <span className="mt-stop-title">
                  {stop.locationName}
                  {stop.city && stop.locationName !== stop.city ? ` — ${stop.city}` : ''}
                </span>
                <Badge tone={STOP_EXECUTION_TONE[stop.status]}>
                  {STOP_EXECUTION_ICONS[stop.status]} {t(STOP_EXECUTION_LABELS[stop.status])}
                </Badge>
              </div>
              <div className="mt-stop-meta">
                {stop.orderNumber} · {stop.customerName}
              </div>
              {(stop.address || stop.postalCode) && (
                <div className="mt-stop-address">{[stop.address, [stop.postalCode, stop.city].filter(Boolean).join(' ')].filter(Boolean).join(', ')}</div>
              )}

              {/* Phase 7 location snapshot: contact, gate/dock, access code and route info for on site. */}
              {(stop.contactName || stop.contactPhone || stop.contactMobile) && (
                <div className="mt-stop-site">
                  👤 {stop.contactName ?? t('myTrips.execution.contactFallback')}
                  {(stop.contactPhone || stop.contactMobile) && (
                    <>
                      {' · '}
                      <a href={`tel:${(stop.contactPhone ?? stop.contactMobile ?? '').replace(/\s+/g, '')}`}>
                        {stop.contactPhone ?? stop.contactMobile}
                      </a>
                    </>
                  )}
                </div>
              )}
              {(stop.gate || stop.dock) && (
                <div className="mt-stop-site">
                  🚪 {[
                    stop.gate ? t('myTrips.execution.gate', { gate: stop.gate }) : null,
                    stop.dock ? t('myTrips.execution.dock', { dock: stop.dock }) : null,
                  ]
                    .filter(Boolean)
                    .join(' · ')}
                </div>
              )}
              {stop.accessCode && <div className="mt-stop-site">🔐 {t('myTrips.execution.accessCode', { code: stop.accessCode })}</div>}
              {stop.routeDescription && <div className="mt-stop-site">🧭 {stop.routeDescription}</div>}
              {stop.openingHoursSummary && <div className="mt-stop-site">🕒 {stop.openingHoursSummary}</div>}

              {(stop.plannedFrom || stop.plannedTo || stop.requestedFrom || stop.requestedTo ||
                stop.confirmedFrom || stop.confirmedTo || stop.latestAllowed || stop.appointmentRequired) && (
                <dl className="mt-stop-windows">
                  {(stop.confirmedFrom || stop.confirmedTo) && (
                    <div className="mt-window-confirmed">
                      <dt>{t('myTrips.execution.windowConfirmed')}</dt>
                      <dd>{formatWindow(stop.confirmedFrom, stop.confirmedTo)}</dd>
                    </div>
                  )}
                  {(stop.plannedFrom || stop.plannedTo) && (
                    <div>
                      <dt>{t('myTrips.execution.windowPlanned')}</dt>
                      <dd>{formatWindow(stop.plannedFrom, stop.plannedTo)}</dd>
                    </div>
                  )}
                  {(stop.requestedFrom || stop.requestedTo) && (
                    <div>
                      <dt>{t('myTrips.execution.windowRequested')}</dt>
                      <dd>{formatWindow(stop.requestedFrom, stop.requestedTo)}</dd>
                    </div>
                  )}
                  {stop.latestAllowed && (
                    <div>
                      <dt>{t('myTrips.execution.windowLatest')}</dt>
                      <dd>{formatTime(stop.latestAllowed)}</dd>
                    </div>
                  )}
                  {stop.appointmentRequired && (
                    <div>
                      <dt>{t('myTrips.execution.appointment')}</dt>
                      <dd>{stop.appointmentReference ?? t('myTrips.execution.appointmentRequired')}</dd>
                    </div>
                  )}
                </dl>
              )}

              {stop.instructions && <div className="mt-stop-instructions">📋 {stop.instructions}</div>}
              {stop.accessInstructions && (
                <div className="mt-stop-instructions">🔑 {t('myTrips.execution.accessInstructions', { text: stop.accessInstructions })}</div>
              )}
              {handlingInstructions && (
                <div className="mt-stop-instructions">
                  📦 {stop.stopType === 'Loading'
                    ? t('myTrips.execution.handlingLoading', { text: handlingInstructions })
                    : t('myTrips.execution.handlingUnloading', { text: handlingInstructions })}
                </div>
              )}

              {stop.arrivedAt && (
                <div className="mt-stop-times">
                  {t('myTrips.execution.arrivedAt', { time: formatTime(stop.arrivedAt) })}
                  {stop.waitingMinutes !== null && ` · ${t('myTrips.execution.waitingTime', { count: stop.waitingMinutes })}`}
                  {stop.lateArrivalReason && ` · ${t('myTrips.execution.lateArrival', { reason: stop.lateArrivalReason })}`}
                </div>
              )}
              {stop.completedAt && (
                <div className="mt-stop-times">
                  {t('myTrips.execution.statusAtTime', { status: t(STOP_EXECUTION_LABELS[stop.status]), time: formatTime(stop.completedAt) })}
                  {stop.podSignedBy && ` · ${t('myTrips.execution.signedBy', { name: stop.podSignedBy })}`}
                </div>
              )}
              {stop.statusReason && <div className="mt-stop-times">{t('myTrips.execution.reasonLine', { reason: stop.statusReason })}</div>}
              {stop.remarks && <div className="mt-stop-times">{t('myTrips.execution.remarkLine', { remark: stop.remarks })}</div>}

              {executable && !isTerminal && (
                <div className="mt-stop-actions">
                  {primary && (
                    <Button className="mt-primary-action" onClick={() => initiateTransition(stop, primary)} disabled={busy}>
                      {t(STOP_TRANSITION_ACTION_LABELS[primary])}
                    </Button>
                  )}
                  {canScan && (
                    <Button variant="secondary" onClick={() => setScanStop(stop)} disabled={busy}>
                      📷 {t('myTrips.execution.scan')}
                    </Button>
                  )}
                  {stop.allowedTransitions
                    .filter((s) => STOP_PRIMARY_ACTION_ORDER.includes(s) && s !== primary)
                    .map((s) => (
                      <Button key={s} variant="secondary" onClick={() => initiateTransition(stop, s)} disabled={busy}>
                        {t(STOP_TRANSITION_ACTION_LABELS[s])}
                      </Button>
                    ))}
                  {exceptions.map((s) => (
                    <Button key={s} variant="ghost" onClick={() => initiateTransition(stop, s)} disabled={busy}>
                      {t(STOP_TRANSITION_ACTION_LABELS[s])}
                    </Button>
                  ))}
                  {canReportException && (
                    <Button variant="ghost" onClick={() => setExceptionTarget({ stop })} disabled={busy}>
                      ⚠ {t('myTrips.execution.reportProblem')}
                    </Button>
                  )}
                </div>
              )}
              {executable && canFinalizePod && !stop.hasPod && (stop.arrivedAt || isTerminal) && (
                <div className="mt-stop-actions">
                  <Button variant="secondary" onClick={() => setPodStop(stop)} disabled={busy}>
                    ✍ {t('myTrips.execution.recordPod')}
                  </Button>
                </div>
              )}
              {stop.hasPod && <div className="mt-stop-times">✍ {t('myTrips.execution.podRecorded')}</div>}

              <button type="button" className="mt-history-toggle" onClick={() => void toggleHistory(stop)}>
                {historyStopId === stop.transportOrderStopId ? t('myTrips.execution.historyHide') : t('myTrips.execution.historyShow')}
              </button>
              {historyStopId === stop.transportOrderStopId && (
                <ul className="mt-history">
                  {!stopHistory && <li>{t('myTrips.execution.historyLoading')}</li>}
                  {stopHistory?.length === 0 && <li>{t('myTrips.execution.historyEmpty')}</li>}
                  {stopHistory?.map((entry, index) => (
                    <li key={index}>
                      <span className="mt-history-time">{formatTime(entry.occurredAt)}</span>{' '}
                      {t(STOP_EXECUTION_LABELS[entry.fromStatus])} → {t(STOP_EXECUTION_LABELS[entry.toStatus])}
                      {entry.userName && ` · ${entry.userName}`}
                      {entry.reason && ` · ${entry.reason}`}
                    </li>
                  ))}
                </ul>
              )}
            </li>
          )
        })}
      </ol>

      {podStop && (
        <PodDialog
          tripId={id}
          stopId={podStop.transportOrderStopId}
          stopLabel={podStop.locationName}
          onClose={() => setPodStop(null)}
          onFinalized={() => {
            setPodStop(null)
            void refreshExecution()
          }}
        />
      )}

      {exceptionTarget && (
        <ReportExceptionDialog
          tripId={id}
          stopId={exceptionTarget.stop?.transportOrderStopId ?? null}
          stopLabel={exceptionTarget.stop?.locationName ?? null}
          onClose={() => setExceptionTarget(null)}
          onReported={() => setExceptionTarget(null)}
        />
      )}

      {scanStop && (
        <ScanPanel
          tripId={id}
          stopId={scanStop.transportOrderStopId}
          stopLabel={scanStop.locationName}
          scanType={scanStop.stopType === 'Loading' ? 'Load' : 'Unload'}
          canCorrect={canCorrectScans}
          onClose={() => setScanStop(null)}
        />
      )}

      {completeTarget && (
        <Modal
          title={t('myTrips.execution.completeTitle', { location: completeTarget.locationName })}
          onClose={() => setCompleteTarget(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCompleteTarget(null)} disabled={busy}>
                {t('myTrips.execution.cancel')}
              </Button>
              <Button type="submit" form="mt-complete-form" disabled={busy}>
                {busy ? t('myTrips.execution.busy') : t('myTrips.execution.complete')}
              </Button>
            </>
          }
        >
          <form id="mt-complete-form" className="mt-form" onSubmit={handleComplete} noValidate>
            {recordsArrival(completeTarget, 'Completed') && lateBoundPassed(completeTarget) && (
              <FormField label={t('myTrips.execution.lateReasonLabel')} htmlFor="mt-late-reason" required hint={t('myTrips.execution.lateReasonHint')}>
                <input id="mt-late-reason" value={completeLateReason} onChange={(e) => setCompleteLateReason(e.target.value)} disabled={busy} maxLength={500} />
              </FormField>
            )}
            <FormField label={t('myTrips.execution.signedByLabel')} htmlFor="mt-pod" hint={t('myTrips.execution.signedByHint')}>
              <input id="mt-pod" value={podSignedBy} onChange={(e) => setPodSignedBy(e.target.value)} disabled={busy} maxLength={200} />
            </FormField>
            <FormField label={t('myTrips.execution.remarksLabel')} htmlFor="mt-remarks">
              <textarea id="mt-remarks" rows={2} value={completeRemarks} onChange={(e) => setCompleteRemarks(e.target.value)} disabled={busy} maxLength={2000} />
            </FormField>
            {packageGateMessage && (
              <>
                <p className="mt-package-gate" role="alert">
                  {packageGateMessage}
                </p>
                {hasPermission('scanning.override') && (
                  <FormField
                    label={t('myTrips.execution.packageOverrideLabel')}
                    htmlFor="mt-package-override"
                    required
                    hint={t('myTrips.execution.packageOverrideHint')}
                  >
                    <input
                      id="mt-package-override"
                      value={packageOverrideReason}
                      onChange={(e) => setPackageOverrideReason(e.target.value)}
                      disabled={busy}
                      maxLength={500}
                    />
                  </FormField>
                )}
              </>
            )}
            <p className="mt-pod-note">{t('myTrips.execution.podNote')}</p>
          </form>
        </Modal>
      )}

      {reasonTarget && (
        <Modal
          title={
            reasonTarget.lateReason
              ? t('myTrips.execution.lateTitle', { location: reasonTarget.stop.locationName })
              : `${t(REASON_MODAL_TITLE_KEYS[reasonTarget.toStatus] ?? 'myTrips.execution.reasonTitleFallback')} — ${reasonTarget.stop.locationName}`
          }
          onClose={() => setReasonTarget(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setReasonTarget(null)} disabled={busy}>
                {t('myTrips.execution.cancel')}
              </Button>
              <Button type="submit" form="mt-reason-form" disabled={busy}>
                {busy ? t('myTrips.execution.busy') : t(STOP_TRANSITION_ACTION_LABELS[reasonTarget.toStatus])}
              </Button>
            </>
          }
        >
          <form id="mt-reason-form" className="mt-form" onSubmit={handleReasonSubmit} noValidate>
            <FormField
              label={reasonTarget.lateReason ? t('myTrips.execution.lateReasonLabel') : t('myTrips.execution.reasonLabel')}
              htmlFor="mt-reason"
              required
              hint={
                reasonTarget.lateReason
                  ? t('myTrips.execution.lateReasonHint')
                  : undefined
              }
            >
              <input
                id="mt-reason"
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                disabled={busy}
                maxLength={500}
                placeholder={reasonTarget.toStatus === 'Skipped' ? t('myTrips.execution.skipPlaceholder') : t('myTrips.execution.failPlaceholder')}
                autoFocus
              />
            </FormField>
          </form>
        </Modal>
      )}
    </div>
  )
}
