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
import { TRIP_STATUS_LABELS, TRIP_STATUS_TONE } from '../../planning/types'
import { STOP_TYPE_LABELS } from '../../transport-orders/types'
import { arriveAtStop, completeStop, getTripExecution, skipStop } from '../api/myTripsApi'
import { STOP_EXECUTION_LABELS, STOP_EXECUTION_TONE, type ExecutionStop, type TripExecution } from '../types'
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

/** Stop-by-stop execution: arrive, complete with POD signer, or skip with a reason. */
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
  const [skipTarget, setSkipTarget] = useState<ExecutionStop | null>(null)
  const [skipReason, setSkipReason] = useState('')

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

  async function handleArrive(stop: ExecutionStop) {
    setBusy(true)
    try {
      setExecution(await arriveAtStop(id, stop.transportOrderStopId))
      showSuccess('Aankomst geregistreerd.')
    } catch {
      showError('De aankomst kon niet worden geregistreerd.')
    } finally {
      setBusy(false)
    }
  }

  async function handleComplete(event: FormEvent) {
    event.preventDefault()
    if (!completeTarget) return
    setBusy(true)
    try {
      const updated = await completeStop(
        id,
        completeTarget.transportOrderStopId,
        podSignedBy.trim() || null,
        completeRemarks.trim() || null,
      )
      setExecution(updated)
      showSuccess(
        updated.tripStatus === 'Completed' ? 'Stop afgerond — de rit is volledig afgewerkt!' : 'Stop afgerond.',
      )
      setCompleteTarget(null)
      setPodSignedBy('')
      setCompleteRemarks('')
    } catch {
      showError('De stop kon niet worden afgerond.')
    } finally {
      setBusy(false)
    }
  }

  async function handleSkip(event: FormEvent) {
    event.preventDefault()
    if (!skipTarget) return
    if (!skipReason.trim()) {
      showError('Een reden is verplicht bij het overslaan van een stop.')
      return
    }
    setBusy(true)
    try {
      const updated = await skipStop(id, skipTarget.transportOrderStopId, skipReason.trim())
      setExecution(updated)
      showSuccess('Stop overgeslagen.')
      setSkipTarget(null)
      setSkipReason('')
    } catch {
      showError('De stop kon niet worden overgeslagen.')
    } finally {
      setBusy(false)
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

      <ol className="mt-stops">
        {execution.stops.map((stop) => (
          <li key={stop.transportOrderStopId} className={`mt-stop mt-stop-${stop.status.toLowerCase()}`}>
            <div className="mt-stop-head">
              <span className="mt-stop-type">
                <Badge tone={stop.stopType === 'Loading' ? 'info' : 'success'}>{STOP_TYPE_LABELS[stop.stopType]}</Badge>
              </span>
              <span className="mt-stop-title">
                {stop.locationName}
                {stop.city && stop.locationName !== stop.city ? ` — ${stop.city}` : ''}
              </span>
              <Badge tone={STOP_EXECUTION_TONE[stop.status]}>{STOP_EXECUTION_LABELS[stop.status]}</Badge>
            </div>
            <div className="mt-stop-meta">
              {stop.orderNumber} · {stop.customerName}
              {formatWindow(stop.plannedFrom, stop.plannedTo) && ` · venster ${formatWindow(stop.plannedFrom, stop.plannedTo)}`}
            </div>
            {(stop.address || stop.postalCode) && (
              <div className="mt-stop-address">{[stop.address, [stop.postalCode, stop.city].filter(Boolean).join(' ')].filter(Boolean).join(', ')}</div>
            )}
            {stop.instructions && <div className="mt-stop-instructions">📋 {stop.instructions}</div>}
            {stop.status === 'Arrived' && stop.arrivedAt && (
              <div className="mt-stop-times">Aangekomen om {formatTime(stop.arrivedAt)}</div>
            )}
            {stop.status === 'Completed' && (
              <div className="mt-stop-times">
                Afgerond om {formatTime(stop.completedAt)}
                {stop.podSignedBy && ` · getekend door ${stop.podSignedBy}`}
              </div>
            )}
            {stop.status === 'Skipped' && stop.remarks && <div className="mt-stop-times">Overgeslagen: {stop.remarks}</div>}

            {executable && stop.status !== 'Completed' && stop.status !== 'Skipped' && (
              <div className="mt-stop-actions">
                {stop.status === 'Pending' && (
                  <Button variant="secondary" onClick={() => void handleArrive(stop)} disabled={busy}>
                    Aangekomen
                  </Button>
                )}
                <Button
                  onClick={() => {
                    setCompleteTarget(stop)
                    setPodSignedBy('')
                    setCompleteRemarks('')
                  }}
                  disabled={busy}
                >
                  Afronden
                </Button>
                <Button
                  variant="secondary"
                  onClick={() => {
                    setSkipTarget(stop)
                    setSkipReason('')
                  }}
                  disabled={busy}
                >
                  Overslaan
                </Button>
              </div>
            )}
          </li>
        ))}
      </ol>

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
            <FormField label="Getekend door" htmlFor="mt-pod" hint="Naam van wie tekende voor ontvangst/lading">
              <input id="mt-pod" value={podSignedBy} onChange={(e) => setPodSignedBy(e.target.value)} disabled={busy} maxLength={200} />
            </FormField>
            <FormField label="Opmerkingen" htmlFor="mt-remarks">
              <textarea id="mt-remarks" rows={2} value={completeRemarks} onChange={(e) => setCompleteRemarks(e.target.value)} disabled={busy} maxLength={2000} />
            </FormField>
            <p className="mt-pod-note">Foto's en gescande documenten koppelen volgt in een latere versie.</p>
          </form>
        </Modal>
      )}

      {skipTarget && (
        <Modal
          title={`Stop overslaan — ${skipTarget.locationName}`}
          onClose={() => setSkipTarget(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setSkipTarget(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="mt-skip-form" disabled={busy}>
                {busy ? 'Bezig…' : 'Overslaan'}
              </Button>
            </>
          }
        >
          <form id="mt-skip-form" className="mt-form" onSubmit={handleSkip} noValidate>
            <FormField label="Reden" htmlFor="mt-skip-reason" required>
              <input
                id="mt-skip-reason"
                value={skipReason}
                onChange={(e) => setSkipReason(e.target.value)}
                disabled={busy}
                maxLength={2000}
                placeholder="bv. locatie gesloten"
              />
            </FormField>
          </form>
        </Modal>
      )}
    </div>
  )
}
