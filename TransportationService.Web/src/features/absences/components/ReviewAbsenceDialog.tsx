import { useEffect, useState } from 'react'
import { ApiError } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import {
  decideAbsence,
  getAbsenceReviewContext,
  requestAbsenceChanges,
  setAbsenceInternalNote,
  startAbsenceReview,
} from '../api/absencesApi'
import {
  ABSENCE_STATUS_LABELS,
  ABSENCE_STATUS_TONE,
  ABSENCE_TYPE_LABELS,
  PART_DAY_LABELS,
  type Absence,
  type AbsenceReviewContext,
} from '../types'
import './absences.css'

interface ReviewAbsenceDialogProps {
  absence: Absence
  onClose: () => void
  onChanged: (updated: Absence) => void
}

/**
 * HR review surface for one leave request: planning conflicts, colleagues off in the same
 * period, balance info and the full decision palette (review / approve / reject / request
 * changes) plus the HR-only internal note.
 */
export function ReviewAbsenceDialog({ absence, onClose, onChanged }: ReviewAbsenceDialogProps) {
  const { showSuccess, showError } = useToast()

  const [context, setContext] = useState<AbsenceReviewContext | null>(null)
  const [note, setNote] = useState('')
  const [proposedStart, setProposedStart] = useState('')
  const [proposedEnd, setProposedEnd] = useState('')
  const [internalNote, setInternalNote] = useState(absence.internalNote ?? '')
  const [busy, setBusy] = useState(false)

  const pending = absence.status === 'Requested' || absence.status === 'UnderReview'

  useEffect(() => {
    let mounted = true
    getAbsenceReviewContext(absence.id)
      .then((data) => {
        if (mounted) setContext(data)
      })
      .catch(() => {
        // The dialog stays usable without the context panel.
      })
    return () => {
      mounted = false
    }
  }, [absence.id])

  async function run(action: () => Promise<Absence>, message: string) {
    setBusy(true)
    try {
      const updated = await action()
      showSuccess(message)
      onChanged(updated)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De actie kon niet worden uitgevoerd.')
    } finally {
      setBusy(false)
    }
  }

  async function openAttachment() {
    try {
      const response = await fetch(`${apiBaseUrl}/api/absences/${absence.id}/attachment`, {
        headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
      })
      if (!response.ok) throw new Error()
      window.open(URL.createObjectURL(await response.blob()), '_blank', 'noopener')
    } catch {
      showError('De bijlage kon niet worden geopend.')
    }
  }

  return (
    <Modal
      title={`Aanvraag beoordelen — ${absence.employeeName}`}
      onClose={onClose}
      busy={busy}
    >
      <div className="rev-dialog">
        <p className="rev-summary">
          <Badge tone={ABSENCE_STATUS_TONE[absence.status]}>{ABSENCE_STATUS_LABELS[absence.status]}</Badge>{' '}
          <strong>{ABSENCE_TYPE_LABELS[absence.type]}</strong> van {absence.startDate} t/m {absence.endDate}
          {absence.partDay !== 'FullDay' && ` (${PART_DAY_LABELS[absence.partDay]})`}
          {absence.reason && ` — "${absence.reason}"`}
        </p>
        {absence.hasAttachment && (
          <button type="button" className="rev-attachment" onClick={() => void openAttachment()}>
            📎 {absence.attachmentFileName ?? 'Bijlage'} bekijken
          </button>
        )}

        {context && (
          <div className="rev-context">
            <h3>Context</h3>
            <ul>
              <li>
                {context.overlappingShifts.length === 0
                  ? '✓ Geen shifts in deze periode.'
                  : `⚠ ${context.overlappingShifts.length} shift(s): ${context.overlappingShifts
                      .map((s) => `${s.date} ${s.startTime.slice(0, 5)}–${s.endTime.slice(0, 5)}`)
                      .join(', ')}`}
              </li>
              <li>
                {context.overlappingTrips.length === 0
                  ? '✓ Geen geplande ritten in deze periode.'
                  : `⚠ ${context.overlappingTrips.length} rit(ten): ${context.overlappingTrips
                      .map((t) => `${t.tripNumber} (${t.tripDate})`)
                      .join(', ')}`}
              </li>
              <li>
                {context.overlappingColleagues.length === 0
                  ? '✓ Geen collega’s van dezelfde afdeling afwezig.'
                  : `⚠ Tegelijk afwezig: ${context.overlappingColleagues
                      .map((c) => `${c.employeeName} (${c.startDate}–${c.endDate}, ${ABSENCE_STATUS_LABELS[c.status]})`)
                      .join(', ')}`}
              </li>
              <li>
                Opgenomen verlof dit jaar: {context.usedVacationDaysThisYear} dag(en)
                <span className="rev-balance-note"> · saldo-administratie volgt later</span>
              </li>
            </ul>
          </div>
        )}

        {pending && (
          <>
            <FormField label="Notitie voor de medewerker" htmlFor="rev-note" hint="Verplicht bij afwijzen of wijzigingen vragen.">
              <textarea id="rev-note" rows={2} value={note} onChange={(e) => setNote(e.target.value)} disabled={busy} maxLength={1000} />
            </FormField>
            <div className="rev-proposal">
              <FormField label="Voorstel van" htmlFor="rev-prop-start" hint="Optioneel alternatief">
                <input id="rev-prop-start" type="date" value={proposedStart} onChange={(e) => setProposedStart(e.target.value)} disabled={busy} />
              </FormField>
              <FormField label="Voorstel tot" htmlFor="rev-prop-end">
                <input id="rev-prop-end" type="date" value={proposedEnd} onChange={(e) => setProposedEnd(e.target.value)} disabled={busy} />
              </FormField>
            </div>
            <div className="rev-actions">
              {absence.status === 'Requested' && (
                <Button
                  variant="secondary"
                  onClick={() => void run(() => startAbsenceReview(absence.id), 'Aanvraag in behandeling genomen.')}
                  disabled={busy}
                >
                  In behandeling
                </Button>
              )}
              <Button
                onClick={() => void run(() => decideAbsence(absence.id, true, note.trim() || null), 'Aanvraag goedgekeurd.')}
                disabled={busy}
              >
                ✓ Goedkeuren
              </Button>
              <Button
                variant="danger"
                onClick={() => {
                  if (!note.trim()) {
                    showError('Een notitie voor de medewerker is verplicht bij afwijzen.')
                    return
                  }
                  void run(() => decideAbsence(absence.id, false, note.trim()), 'Aanvraag afgewezen.')
                }}
                disabled={busy}
              >
                ✕ Afwijzen
              </Button>
              <Button
                variant="secondary"
                onClick={() => {
                  if (!note.trim()) {
                    showError('Een notitie voor de medewerker is verplicht bij wijzigingen vragen.')
                    return
                  }
                  void run(
                    () =>
                      requestAbsenceChanges(absence.id, note.trim(), proposedStart || null, proposedEnd || null),
                    'Wijziging gevraagd — de medewerker is verwittigd.',
                  )
                }}
                disabled={busy}
              >
                ⇄ Wijziging vragen
              </Button>
            </div>
          </>
        )}

        <FormField label="Interne HR-notitie" htmlFor="rev-internal" hint="Niet zichtbaar voor de medewerker.">
          <textarea id="rev-internal" rows={2} value={internalNote} onChange={(e) => setInternalNote(e.target.value)} disabled={busy} maxLength={2000} />
        </FormField>
        <div className="rev-internal-actions">
          <Button
            variant="secondary"
            onClick={() => void run(() => setAbsenceInternalNote(absence.id, internalNote.trim() || null), 'Interne notitie opgeslagen.')}
            disabled={busy}
          >
            Interne notitie opslaan
          </Button>
        </div>
      </div>
    </Modal>
  )
}
