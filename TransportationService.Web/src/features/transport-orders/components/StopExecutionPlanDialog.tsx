import { useState, type FormEvent } from 'react'
import { ApiError } from '../../../api/apiClient'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { updateStopExecutionPlan } from '../api/transportOrdersApi'
import { STOP_TYPE_LABELS, type TransportOrderDetail, type TransportOrderStop } from '../types'
import { useLocale } from '../../../i18n/localeContext'
import { fromDateTimeLocalInput, toDateTimeLocalInput } from '../../../utils/dates'

// C-03: the datetime-local fields hold TENANT wall clock, the wire holds UTC instants. Both
// directions go through utils/dates so this dialog, the order form and the detail table agree
// on what "08:00" means.
const toLocalInput = toDateTimeLocalInput
const toApiValue = fromDateTimeLocalInput

interface StopExecutionPlanDialogProps {
  orderId: string
  stop: TransportOrderStop
  onClose: () => void
  onSaved: (order: TransportOrderDetail) => void
}

/**
 * Dispatcher dialog to confirm the time window, bounds, appointment and instructions of one
 * stop. Uses the dedicated execution-plan endpoint, so it also works after planning locked
 * the rest of the order.
 */
export function StopExecutionPlanDialog({ orderId, stop, onClose, onSaved }: StopExecutionPlanDialogProps) {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()

  const [confirmedFrom, setConfirmedFrom] = useState(toLocalInput(stop.confirmedFrom))
  const [confirmedTo, setConfirmedTo] = useState(toLocalInput(stop.confirmedTo))
  const [earliestAllowed, setEarliestAllowed] = useState(toLocalInput(stop.earliestAllowed))
  const [latestAllowed, setLatestAllowed] = useState(toLocalInput(stop.latestAllowed))
  const [appointmentRequired, setAppointmentRequired] = useState(stop.appointmentRequired)
  const [appointmentReference, setAppointmentReference] = useState(stop.appointmentReference ?? '')
  const [accessInstructions, setAccessInstructions] = useState(stop.accessInstructions ?? '')
  const [loadingInstructions, setLoadingInstructions] = useState(stop.loadingInstructions ?? '')
  const [unloadingInstructions, setUnloadingInstructions] = useState(stop.unloadingInstructions ?? '')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    if (confirmedFrom && confirmedTo && confirmedTo < confirmedFrom) {
      setError('Het einde van het bevestigde venster moet na het begin liggen.')
      return
    }
    if (earliestAllowed && latestAllowed && latestAllowed < earliestAllowed) {
      setError('Het uiterste tijdstip moet na het vroegst toegelaten tijdstip liggen.')
      return
    }

    setBusy(true)
    try {
      const updated = await updateStopExecutionPlan(orderId, stop.id, {
        confirmedFrom: toApiValue(confirmedFrom),
        confirmedTo: toApiValue(confirmedTo),
        earliestAllowed: toApiValue(earliestAllowed),
        latestAllowed: toApiValue(latestAllowed),
        appointmentRequired,
        appointmentReference: appointmentReference.trim() || null,
        accessInstructions: accessInstructions.trim() || null,
        loadingInstructions: loadingInstructions.trim() || null,
        unloadingInstructions: unloadingInstructions.trim() || null,
      })
      showSuccess('Uitvoeringsplan van de stop bijgewerkt.')
      onSaved(updated)
    } catch (err) {
      const message = err instanceof ApiError ? err.message : 'Het uitvoeringsplan kon niet worden opgeslagen.'
      setError(message)
      showError(message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={`Stop ${stop.sequence} (${t(STOP_TYPE_LABELS[stop.stopType])}) — venster & instructies`}
      onClose={onClose}
      busy={busy}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Annuleren
          </Button>
          <Button type="submit" form="stop-plan-form" disabled={busy}>
            {busy ? 'Opslaan…' : 'Opslaan'}
          </Button>
        </>
      }
    >
      <form id="stop-plan-form" className="to-stop-plan-form" onSubmit={handleSubmit} noValidate>
        {error && (
          <div className="tof-error" role="alert">
            {error}
          </div>
        )}
        <div className="to-stop-plan-grid">
          <FormField label="Bevestigd van" htmlFor="sp-conf-from" hint="Het venster dat aan de klant is bevestigd">
            <input id="sp-conf-from" type="datetime-local" value={confirmedFrom} onChange={(e) => setConfirmedFrom(e.target.value)} disabled={busy} />
          </FormField>
          <FormField label="Bevestigd tot" htmlFor="sp-conf-to">
            <input id="sp-conf-to" type="datetime-local" value={confirmedTo} onChange={(e) => setConfirmedTo(e.target.value)} disabled={busy} />
          </FormField>
          <FormField label="Vroegst toegelaten" htmlFor="sp-earliest">
            <input id="sp-earliest" type="datetime-local" value={earliestAllowed} onChange={(e) => setEarliestAllowed(e.target.value)} disabled={busy} />
          </FormField>
          <FormField label="Uiterste tijdstip" htmlFor="sp-latest" hint="Later aankomen vereist een reden van de chauffeur">
            <input id="sp-latest" type="datetime-local" value={latestAllowed} onChange={(e) => setLatestAllowed(e.target.value)} disabled={busy} />
          </FormField>
        </div>
        <label className="tof-checkbox">
          <input type="checkbox" checked={appointmentRequired} onChange={(e) => setAppointmentRequired(e.target.checked)} disabled={busy} />
          Afspraak verplicht
        </label>
        <FormField label="Afspraakreferentie" htmlFor="sp-appref">
          <input id="sp-appref" value={appointmentReference} onChange={(e) => setAppointmentReference(e.target.value)} disabled={busy} maxLength={100} placeholder="bv. slotnummer of boekingscode" />
        </FormField>
        <FormField label="Toegangsinstructies" htmlFor="sp-access">
          <textarea id="sp-access" rows={2} value={accessInstructions} onChange={(e) => setAccessInstructions(e.target.value)} disabled={busy} maxLength={2000} />
        </FormField>
        {stop.stopType === 'Loading' ? (
          <FormField label="Laadinstructies" htmlFor="sp-loading">
            <textarea id="sp-loading" rows={2} value={loadingInstructions} onChange={(e) => setLoadingInstructions(e.target.value)} disabled={busy} maxLength={2000} />
          </FormField>
        ) : (
          <FormField label="Losinstructies" htmlFor="sp-unloading">
            <textarea id="sp-unloading" rows={2} value={unloadingInstructions} onChange={(e) => setUnloadingInstructions(e.target.value)} disabled={busy} maxLength={2000} />
          </FormField>
        )}
      </form>
    </Modal>
  )
}
