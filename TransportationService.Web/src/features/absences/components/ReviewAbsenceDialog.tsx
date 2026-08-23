import { useEffect, useState } from 'react'
import { ApiError } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import {
  decideAbsence,
  getAbsenceReviewContext,
  requestAbsenceChanges,
  setAbsenceInternalNote,
  startAbsenceReview,
} from '../api/absencesApi'
import {
  ABSENCE_STATUS_TONE,
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
  const { t } = useLocale()
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
      showError(err instanceof ApiError ? err.message : t('absences.review.actionFailed'))
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
      showError(t('absences.review.attachmentFailed'))
    }
  }

  return (
    <Modal
      title={t('absences.review.title', { name: absence.employeeName })}
      onClose={onClose}
      busy={busy}
    >
      <div className="rev-dialog">
        <p className="rev-summary">
          <Badge tone={ABSENCE_STATUS_TONE[absence.status]}>{t(`absences.status.${absence.status}`)}</Badge>{' '}
          <strong>{t(`absences.type.${absence.type}`)}</strong>{' '}
          {t('absences.review.period', { from: absence.startDate, to: absence.endDate })}
          {absence.partDay !== 'FullDay' && ` (${t(`absences.partDay.${absence.partDay}`)})`}
          {absence.reason && ` — "${absence.reason}"`}
        </p>
        {absence.hasAttachment && (
          <button type="button" className="rev-attachment" onClick={() => void openAttachment()}>
            📎{' '}
            {t('absences.review.attachmentView', {
              name: absence.attachmentFileName ?? t('absences.review.attachmentFallback'),
            })}
          </button>
        )}

        {context && (
          <div className="rev-context">
            <h3>{t('absences.review.contextTitle')}</h3>
            <ul>
              <li>
                {context.overlappingShifts.length === 0
                  ? t('absences.review.noShifts')
                  : t('absences.review.shifts', {
                      count: context.overlappingShifts.length,
                      list: context.overlappingShifts
                        .map((s) => `${s.date} ${s.startTime.slice(0, 5)}–${s.endTime.slice(0, 5)}`)
                        .join(', '),
                    })}
              </li>
              <li>
                {context.overlappingTrips.length === 0
                  ? t('absences.review.noTrips')
                  : t('absences.review.trips', {
                      count: context.overlappingTrips.length,
                      list: context.overlappingTrips.map((trip) => `${trip.tripNumber} (${trip.tripDate})`).join(', '),
                    })}
              </li>
              <li>
                {context.overlappingColleagues.length === 0
                  ? t('absences.review.noColleagues')
                  : t('absences.review.colleagues', {
                      list: context.overlappingColleagues
                        .map(
                          (c) =>
                            `${c.employeeName} (${c.startDate}–${c.endDate}, ${t(`absences.status.${c.status}`)})`,
                        )
                        .join(', '),
                    })}
              </li>
              <li>
                {t('absences.review.usedVacation', { count: context.usedVacationDaysThisYear })}
                <span className="rev-balance-note"> {t('absences.review.balanceNote')}</span>
              </li>
            </ul>
          </div>
        )}

        {pending && (
          <>
            <FormField label={t('absences.review.noteForEmployee')} htmlFor="rev-note" hint={t('absences.review.noteHint')}>
              <textarea id="rev-note" rows={2} value={note} onChange={(e) => setNote(e.target.value)} disabled={busy} maxLength={1000} />
            </FormField>
            <div className="rev-proposal">
              <FormField label={t('absences.review.proposedFrom')} htmlFor="rev-prop-start" hint={t('absences.review.proposedFromHint')}>
                <input id="rev-prop-start" type="date" value={proposedStart} onChange={(e) => setProposedStart(e.target.value)} disabled={busy} />
              </FormField>
              <FormField label={t('absences.review.proposedTo')} htmlFor="rev-prop-end">
                <input id="rev-prop-end" type="date" value={proposedEnd} onChange={(e) => setProposedEnd(e.target.value)} disabled={busy} />
              </FormField>
            </div>
            <div className="rev-actions">
              {absence.status === 'Requested' && (
                <Button
                  variant="secondary"
                  onClick={() => void run(() => startAbsenceReview(absence.id), t('absences.review.startReviewSuccess'))}
                  disabled={busy}
                >
                  {t('absences.review.startReview')}
                </Button>
              )}
              <Button
                onClick={() =>
                  void run(() => decideAbsence(absence.id, true, note.trim() || null), t('absences.review.approveSuccess'))
                }
                disabled={busy}
              >
                {t('absences.review.approve')}
              </Button>
              <Button
                variant="danger"
                onClick={() => {
                  if (!note.trim()) {
                    showError(t('absences.review.rejectNoteRequired'))
                    return
                  }
                  void run(() => decideAbsence(absence.id, false, note.trim()), t('absences.review.rejectSuccess'))
                }}
                disabled={busy}
              >
                {t('absences.review.reject')}
              </Button>
              <Button
                variant="secondary"
                onClick={() => {
                  if (!note.trim()) {
                    showError(t('absences.review.requestChangesNoteRequired'))
                    return
                  }
                  void run(
                    () =>
                      requestAbsenceChanges(absence.id, note.trim(), proposedStart || null, proposedEnd || null),
                    t('absences.review.requestChangesSuccess'),
                  )
                }}
                disabled={busy}
              >
                {t('absences.review.requestChanges')}
              </Button>
            </div>
          </>
        )}

        <FormField label={t('absences.review.internalNote')} htmlFor="rev-internal" hint={t('absences.review.internalNoteHint')}>
          <textarea id="rev-internal" rows={2} value={internalNote} onChange={(e) => setInternalNote(e.target.value)} disabled={busy} maxLength={2000} />
        </FormField>
        <div className="rev-internal-actions">
          <Button
            variant="secondary"
            onClick={() =>
              void run(() => setAbsenceInternalNote(absence.id, internalNote.trim() || null), t('absences.review.internalNoteSaved'))
            }
            disabled={busy}
          >
            {t('absences.review.saveInternalNote')}
          </Button>
        </div>
      </div>
    </Modal>
  )
}
