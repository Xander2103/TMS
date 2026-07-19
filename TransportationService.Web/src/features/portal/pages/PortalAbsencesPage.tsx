import { useEffect, useState, type FormEvent } from 'react'
import { PageHeader } from '../../../components/layout/PageHeader'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { ApiError } from '../../../api/apiClient'
import {
  ABSENCE_STATUS_LABELS,
  ABSENCE_STATUS_TONE,
  ABSENCE_TYPES,
  ABSENCE_TYPE_LABELS,
  type Absence,
  type AbsenceType,
} from '../../absences/types'
import { cancelMyAbsence, createMyAbsence, listMyAbsences } from '../api/portalApi'
import './portal.css'

/** Own leave/absence requests: view status, request new, withdraw pending ones. */
export function PortalAbsencesPage() {
  const { showSuccess, showError } = useToast()

  const [absences, setAbsences] = useState<Absence[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [dialogOpen, setDialogOpen] = useState(false)
  const [type, setType] = useState<AbsenceType>('Vacation')
  const [startDate, setStartDate] = useState('')
  const [endDate, setEndDate] = useState('')
  const [reason, setReason] = useState('')

  function reload() {
    listMyAbsences()
      .then((data) => {
        setAbsences(data)
        setLoadError(null)
      })
      .catch(() => setLoadError('Je afwezigheden konden niet worden geladen.'))
  }

  useEffect(reload, [])

  async function handleCreate(event: FormEvent) {
    event.preventDefault()
    if (!startDate || !endDate) {
      showError('Kies een begin- en einddatum.')
      return
    }
    if (endDate < startDate) {
      showError('De einddatum moet op of na de begindatum liggen.')
      return
    }
    setBusy(true)
    try {
      await createMyAbsence({ type, startDate, endDate, reason: reason.trim() || null })
      showSuccess('Aanvraag ingediend — HR bekijkt ze zo snel mogelijk.')
      setDialogOpen(false)
      setStartDate('')
      setEndDate('')
      setReason('')
      reload()
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De aanvraag kon niet worden ingediend.')
    } finally {
      setBusy(false)
    }
  }

  async function handleCancel(id: string) {
    setBusy(true)
    try {
      await cancelMyAbsence(id)
      showSuccess('Aanvraag ingetrokken.')
      reload()
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De aanvraag kon niet worden ingetrokken.')
    } finally {
      setBusy(false)
    }
  }

  if (loadError) return <ErrorState message={loadError} />
  if (!absences) return <LoadingState message="Afwezigheden laden..." />

  return (
    <div>
      <PageHeader
        title="Mijn afwezigheden"
        subtitle="Verlof, ziekte en andere afwezigheden — met hun status."
        action={<Button onClick={() => setDialogOpen(true)}>+ Verlof aanvragen</Button>}
      />

      {absences.length === 0 && <p className="portal-empty">Nog geen afwezigheden.</p>}

      <ul className="portal-absences">
        {absences.map((absence) => (
          <li key={absence.id} className="portal-absence">
            <div className="portal-absence-head">
              <strong>{ABSENCE_TYPE_LABELS[absence.type]}</strong>
              <Badge tone={ABSENCE_STATUS_TONE[absence.status]}>{ABSENCE_STATUS_LABELS[absence.status]}</Badge>
            </div>
            <div className="portal-absence-dates">
              {absence.startDate} t/m {absence.endDate}
            </div>
            {absence.reason && <div className="portal-absence-reason">{absence.reason}</div>}
            {absence.decisionNote && <div className="portal-absence-decision">Beslissing: {absence.decisionNote}</div>}
            {absence.status === 'Requested' && (
              <Button variant="ghost" onClick={() => void handleCancel(absence.id)} disabled={busy}>
                Intrekken
              </Button>
            )}
          </li>
        ))}
      </ul>

      {dialogOpen && (
        <Modal
          title="Verlof aanvragen"
          onClose={() => setDialogOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDialogOpen(false)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="portal-absence-form" disabled={busy}>
                {busy ? 'Bezig…' : 'Aanvragen'}
              </Button>
            </>
          }
        >
          <form id="portal-absence-form" className="portal-form" onSubmit={handleCreate} noValidate>
            <FormField label="Type" htmlFor="pa-type" required>
              <select id="pa-type" value={type} onChange={(e) => setType(e.target.value as AbsenceType)} disabled={busy}>
                {ABSENCE_TYPES.map((t) => (
                  <option key={t} value={t}>
                    {ABSENCE_TYPE_LABELS[t]}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="Van" htmlFor="pa-start" required>
              <input id="pa-start" type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} disabled={busy} />
            </FormField>
            <FormField label="Tot en met" htmlFor="pa-end" required>
              <input id="pa-end" type="date" value={endDate} onChange={(e) => setEndDate(e.target.value)} disabled={busy} />
            </FormField>
            <FormField label="Reden / toelichting" htmlFor="pa-reason">
              <textarea id="pa-reason" rows={2} value={reason} onChange={(e) => setReason(e.target.value)} disabled={busy} maxLength={500} />
            </FormField>
          </form>
        </Modal>
      )}
    </div>
  )
}
