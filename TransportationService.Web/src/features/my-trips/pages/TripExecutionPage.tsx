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
import { ApiError } from '../../../api/apiClient'
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

function formatTime(value: string | null): string {
  return value ? value.slice(11, 16) : ''
}

function formatWindow(from: string | null, to: string | null): string {
  const fmt = (v: string) => v.slice(11, 16)
  if (from && to) return `${fmt(from)}–${fmt(to)}`
  if (from) return `vanaf ${fmt(from)}`
  if (to) return `tot ${fmt(to)}`
  return ''
}

/** Whether this transition records the arrival moment (explicit or via the backend's arrival bridge). */
function recordsArrival(stop: ExecutionStop, to: StopExecutionStatus): boolean {
  if (to === 'Arrived') return true
  return !stop.arrivedAt && ['Loading', 'Unloading', 'Loaded', 'Completed', 'PartiallyCompleted'].includes(to)
}

function lateBoundPassed(stop: ExecutionStop): boolean {
  const bound = effectiveLatestBound(stop)
  return bound !== null && new Date(bound).getTime() < Date.now()
}

const REASON_MODAL_TITLES: Record<string, string> = {
  Skipped: 'Stop overslaan',
  Failed: 'Stop als mislukt melden',
  PartiallyCompleted: 'Stop deels afronden',
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

  useEffect(() => {
    let mounted = true
    getTripExecution(id)
      .then((data) => {
        if (!mounted) return
        setExecution(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('De rit kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [id])

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
    showSuccess(updated.tripStatus === 'Completed' ? `${message} — de rit is volledig afgewerkt!` : message)
  }

  async function doTransition(stop: ExecutionStop, toStatus: StopExecutionStatus, transitionReason: string | null) {
    setBusy(true)
    try {
      const updated = await transitionStop(id, stop.transportOrderStopId, {
        toStatus,
        reason: transitionReason,
      })
      afterUpdate(updated, `${STOP_EXECUTION_LABELS[toStatus]} geregistreerd.`)
      setReasonTarget(null)
      setReason('')
    } catch (err) {
      if (err instanceof ApiError && err.status === 400 && err.message.toLowerCase().includes('reden')) {
        // The server demands a reason (e.g. late arrival detected server-side): ask for it.
        setReasonTarget({ stop, toStatus, lateReason: true })
        showError(err.message)
      } else {
        showError(err instanceof ApiError ? err.message : 'De status kon niet worden gewijzigd.')
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
      showError('Een reden is verplicht.')
      return
    }
    await doTransition(reasonTarget.stop, reasonTarget.toStatus, reason.trim())
  }

  async function handleComplete(event: FormEvent) {
    event.preventDefault()
    if (!completeTarget) return
    const needsLateReason = recordsArrival(completeTarget, 'Completed') && lateBoundPassed(completeTarget)
    if (needsLateReason && !completeLateReason.trim()) {
      showError('Je komt aan na het uiterste tijdstip; een reden voor de late aankomst is verplicht.')
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
      afterUpdate(updated, 'Stop afgerond.')
      setCompleteTarget(null)
      setPackageGateMessage(null)
      setPackageOverrideReason('')
    } catch (err) {
      // The package gate keeps the dialog open: override holders get a reason field.
      if (err instanceof ApiError && err.status === 400 && (err.message.includes('colli zonder uitkomst') || err.message.includes('vrijgave'))) {
        setPackageGateMessage(err.message)
      }
      showError(err instanceof ApiError ? err.message : 'De stop kon niet worden afgerond.')
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
        showError('De historiek kon niet worden geladen.')
        setHistoryStopId(null)
      }
    }
  }

  if (loadError) return <ErrorState message={loadError} />
  if (!execution) return <LoadingState message="Rit laden..." />

  const executable = execution.tripStatus === 'InProgress' && canExecute

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Mijn ritten', to: '/my-trips' }, { label: execution.tripNumber }]} />
      <PageHeader
        title={`${execution.tripNumber} — ${execution.tripDate}`}
        subtitle={`${execution.vehicleNumber ? `${execution.vehicleNumber} (${execution.vehicleLicensePlate})` : 'Geen voertuig'} · ${execution.completedCount}/${execution.totalCount} stops afgehandeld`}
        action={<Badge tone={TRIP_STATUS_TONE[execution.tripStatus]}>{TRIP_STATUS_LABELS[execution.tripStatus]}</Badge>}
      />

      {execution.tripStatus === 'Planned' && (
        <p className="mt-hint">De rit is nog niet gestart. De planner start de rit, daarna kun je stops registreren.</p>
      )}

      {canReportException && execution.tripStatus === 'InProgress' && (
        <div className="mt-trip-actions">
          <Button variant="ghost" onClick={() => setExceptionTarget({ stop: null })} disabled={busy}>
            ⚠ Probleem met rit/voertuig melden
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
                  <Badge tone={stop.stopType === 'Loading' ? 'info' : 'success'}>{STOP_TYPE_LABELS[stop.stopType]}</Badge>
                </span>
                <span className="mt-stop-title">
                  {stop.locationName}
                  {stop.city && stop.locationName !== stop.city ? ` — ${stop.city}` : ''}
                </span>
                <Badge tone={STOP_EXECUTION_TONE[stop.status]}>
                  {STOP_EXECUTION_ICONS[stop.status]} {STOP_EXECUTION_LABELS[stop.status]}
                </Badge>
              </div>
              <div className="mt-stop-meta">
                {stop.orderNumber} · {stop.customerName}
              </div>
              {(stop.address || stop.postalCode) && (
                <div className="mt-stop-address">{[stop.address, [stop.postalCode, stop.city].filter(Boolean).join(' ')].filter(Boolean).join(', ')}</div>
              )}

              {(stop.plannedFrom || stop.plannedTo || stop.requestedFrom || stop.requestedTo ||
                stop.confirmedFrom || stop.confirmedTo || stop.latestAllowed || stop.appointmentRequired) && (
                <dl className="mt-stop-windows">
                  {(stop.confirmedFrom || stop.confirmedTo) && (
                    <div className="mt-window-confirmed">
                      <dt>Bevestigd</dt>
                      <dd>{formatWindow(stop.confirmedFrom, stop.confirmedTo)}</dd>
                    </div>
                  )}
                  {(stop.plannedFrom || stop.plannedTo) && (
                    <div>
                      <dt>Gepland</dt>
                      <dd>{formatWindow(stop.plannedFrom, stop.plannedTo)}</dd>
                    </div>
                  )}
                  {(stop.requestedFrom || stop.requestedTo) && (
                    <div>
                      <dt>Gevraagd</dt>
                      <dd>{formatWindow(stop.requestedFrom, stop.requestedTo)}</dd>
                    </div>
                  )}
                  {stop.latestAllowed && (
                    <div>
                      <dt>Uiterlijk</dt>
                      <dd>{formatTime(stop.latestAllowed)}</dd>
                    </div>
                  )}
                  {stop.appointmentRequired && (
                    <div>
                      <dt>Afspraak</dt>
                      <dd>{stop.appointmentReference ?? 'verplicht'}</dd>
                    </div>
                  )}
                </dl>
              )}

              {stop.instructions && <div className="mt-stop-instructions">📋 {stop.instructions}</div>}
              {stop.accessInstructions && <div className="mt-stop-instructions">🔑 Toegang: {stop.accessInstructions}</div>}
              {handlingInstructions && (
                <div className="mt-stop-instructions">
                  {stop.stopType === 'Loading' ? '📦 Laden: ' : '📦 Lossen: '}
                  {handlingInstructions}
                </div>
              )}

              {stop.arrivedAt && (
                <div className="mt-stop-times">
                  Aangekomen om {formatTime(stop.arrivedAt)}
                  {stop.waitingMinutes !== null && ` · ${stop.waitingMinutes} min wachttijd`}
                  {stop.lateArrivalReason && ` · te laat: ${stop.lateArrivalReason}`}
                </div>
              )}
              {stop.completedAt && (
                <div className="mt-stop-times">
                  {STOP_EXECUTION_LABELS[stop.status]} om {formatTime(stop.completedAt)}
                  {stop.podSignedBy && ` · getekend door ${stop.podSignedBy}`}
                </div>
              )}
              {stop.statusReason && <div className="mt-stop-times">Reden: {stop.statusReason}</div>}
              {stop.remarks && <div className="mt-stop-times">Opmerking: {stop.remarks}</div>}

              {executable && !isTerminal && (
                <div className="mt-stop-actions">
                  {primary && (
                    <Button className="mt-primary-action" onClick={() => initiateTransition(stop, primary)} disabled={busy}>
                      {STOP_TRANSITION_ACTION_LABELS[primary]}
                    </Button>
                  )}
                  {canScan && (
                    <Button variant="secondary" onClick={() => setScanStop(stop)} disabled={busy}>
                      📷 Scannen
                    </Button>
                  )}
                  {stop.allowedTransitions
                    .filter((s) => STOP_PRIMARY_ACTION_ORDER.includes(s) && s !== primary)
                    .map((s) => (
                      <Button key={s} variant="secondary" onClick={() => initiateTransition(stop, s)} disabled={busy}>
                        {STOP_TRANSITION_ACTION_LABELS[s]}
                      </Button>
                    ))}
                  {exceptions.map((s) => (
                    <Button key={s} variant="ghost" onClick={() => initiateTransition(stop, s)} disabled={busy}>
                      {STOP_TRANSITION_ACTION_LABELS[s]}
                    </Button>
                  ))}
                  {canReportException && (
                    <Button variant="ghost" onClick={() => setExceptionTarget({ stop })} disabled={busy}>
                      ⚠ Probleem melden
                    </Button>
                  )}
                </div>
              )}
              {executable && canFinalizePod && !stop.hasPod && (stop.arrivedAt || isTerminal) && (
                <div className="mt-stop-actions">
                  <Button variant="secondary" onClick={() => setPodStop(stop)} disabled={busy}>
                    ✍ POD opnemen
                  </Button>
                </div>
              )}
              {stop.hasPod && <div className="mt-stop-times">✍ POD opgenomen</div>}

              <button type="button" className="mt-history-toggle" onClick={() => void toggleHistory(stop)}>
                {historyStopId === stop.transportOrderStopId ? 'Historiek verbergen' : 'Historiek tonen'}
              </button>
              {historyStopId === stop.transportOrderStopId && (
                <ul className="mt-history">
                  {!stopHistory && <li>Historiek laden…</li>}
                  {stopHistory?.length === 0 && <li>Nog geen statuswijzigingen.</li>}
                  {stopHistory?.map((entry, index) => (
                    <li key={index}>
                      <span className="mt-history-time">{formatTime(entry.occurredAt)}</span>{' '}
                      {STOP_EXECUTION_LABELS[entry.fromStatus]} → {STOP_EXECUTION_LABELS[entry.toStatus]}
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
          title={`Stop afronden — ${completeTarget.locationName}`}
          onClose={() => setCompleteTarget(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCompleteTarget(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="mt-complete-form" disabled={busy}>
                {busy ? 'Bezig…' : 'Afronden'}
              </Button>
            </>
          }
        >
          <form id="mt-complete-form" className="mt-form" onSubmit={handleComplete} noValidate>
            {recordsArrival(completeTarget, 'Completed') && lateBoundPassed(completeTarget) && (
              <FormField label="Reden late aankomst" htmlFor="mt-late-reason" required hint="Je bent later dan het uiterste tijdstip van deze stop.">
                <input id="mt-late-reason" value={completeLateReason} onChange={(e) => setCompleteLateReason(e.target.value)} disabled={busy} maxLength={500} />
              </FormField>
            )}
            <FormField label="Getekend door" htmlFor="mt-pod" hint="Naam van wie tekende voor ontvangst/lading">
              <input id="mt-pod" value={podSignedBy} onChange={(e) => setPodSignedBy(e.target.value)} disabled={busy} maxLength={200} />
            </FormField>
            <FormField label="Opmerkingen" htmlFor="mt-remarks">
              <textarea id="mt-remarks" rows={2} value={completeRemarks} onChange={(e) => setCompleteRemarks(e.target.value)} disabled={busy} maxLength={2000} />
            </FormField>
            {packageGateMessage && (
              <>
                <p className="mt-package-gate" role="alert">
                  {packageGateMessage}
                </p>
                {hasPermission('scanning.override') && (
                  <FormField
                    label="Vrijgavereden colli"
                    htmlFor="mt-package-override"
                    required
                    hint="Afronden zonder uitkomst per colli vereist een reden (vrijgaverecht)."
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
            <p className="mt-pod-note">Foto's en gescande documenten koppelen volgt in een latere versie.</p>
          </form>
        </Modal>
      )}

      {reasonTarget && (
        <Modal
          title={
            reasonTarget.lateReason
              ? `Late aankomst — ${reasonTarget.stop.locationName}`
              : `${REASON_MODAL_TITLES[reasonTarget.toStatus] ?? 'Reden opgeven'} — ${reasonTarget.stop.locationName}`
          }
          onClose={() => setReasonTarget(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setReasonTarget(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="mt-reason-form" disabled={busy}>
                {busy ? 'Bezig…' : STOP_TRANSITION_ACTION_LABELS[reasonTarget.toStatus]}
              </Button>
            </>
          }
        >
          <form id="mt-reason-form" className="mt-form" onSubmit={handleReasonSubmit} noValidate>
            <FormField
              label={reasonTarget.lateReason ? 'Reden late aankomst' : 'Reden'}
              htmlFor="mt-reason"
              required
              hint={
                reasonTarget.lateReason
                  ? 'Je bent later dan het uiterste tijdstip van deze stop.'
                  : undefined
              }
            >
              <input
                id="mt-reason"
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                disabled={busy}
                maxLength={500}
                placeholder={reasonTarget.toStatus === 'Skipped' ? 'bv. locatie gesloten' : 'bv. lading geweigerd'}
                autoFocus
              />
            </FormField>
          </form>
        </Modal>
      )}
    </div>
  )
}
