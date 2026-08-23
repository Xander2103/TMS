import { useCallback, useEffect, useMemo, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { Modal } from '../../../components/ui/Modal'
import { FormField } from '../../../components/ui/FormField'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError } from '../../../api/problemDetails'
import { useAuth } from '../../auth/authContextValue'
import {
  formatDate, formatDateLong, formatDateTime, formatDurationMinutes, formatSignedDurationMinutes,
} from '../../../utils/dates'
import { useLocale } from '../../../i18n/localeContext'
import {
  cancelSession, correctBreak, correctSession, createManualSession, disableAttendanceCredential,
  getAttendanceCredentialStatus, getEmployeeAttendance, setAttendancePin,
} from '../api/timeAttendanceApi'
import { SessionTimeline } from './SessionTimeline'
import type { AttendanceCredentialStatus, AttendanceHistory, AttendanceSession } from '../types'
import '../pages/time-attendance.css'

function toIsoDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

/** ISO-UTC → waarde voor <input type="datetime-local"> in lokale tijd. */
function toLocalInput(iso: string | null): string {
  if (!iso) return ''
  const date = new Date(iso.endsWith('Z') || iso.includes('+') ? iso : `${iso}Z`)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

/** datetime-local (lokale tijd) → ISO-UTC, of null bij leeg. */
function fromLocalInput(value: string): string | null {
  if (!value) return null
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : date.toISOString()
}

interface CorrectionDraft {
  session: AttendanceSession
  clockInAt: string
  clockOutAt: string
  breaks: { id: string; startedAt: string; endedAt: string }[]
  reason: string
}

/**
 * Urenregistratie-tab op de medewerkerfiche: historiek met timeline en correcties
 * (attendance.correct: tijden aanpassen met verplichte reden, annuleren, manuele
 * sessie) plus prikklokcode-beheer (attendance.manage_credentials: genereren/zetten/
 * intrekken — nooit uitlezen).
 */
export function AttendanceTab({ employeeId }: { employeeId: string }) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canCorrect = hasPermission('attendance.correct')
  const canManagePin = hasPermission('attendance.manage_credentials')

  const [rangeDays, setRangeDays] = useState(14)
  const [reloadToken, setReloadToken] = useState(0)
  const [history, setHistory] = useState<AttendanceHistory | null>(null)
  const [error, setError] = useState<string | null>(null)

  const range = useMemo(() => {
    const today = new Date()
    const from = new Date(today)
    from.setDate(today.getDate() - (rangeDays - 1))
    return { from: toIsoDate(from), to: toIsoDate(today) }
  }, [rangeDays])

  const reload = useCallback(() => setReloadToken((t) => t + 1), [])

  useEffect(() => {
    let mounted = true
    setError(null)
    getEmployeeAttendance(employeeId, range.from, range.to)
      .then((data) => {
        if (mounted) setHistory(data)
      })
      .catch(() => {
        if (mounted) setError('attendance.tab.loadFailed')
      })
    return () => {
      mounted = false
    }
  }, [employeeId, range.from, range.to, reloadToken])

  // ── Correctiedialoog ───────────────────────────────────────────────────────────
  const [draft, setDraft] = useState<CorrectionDraft | null>(null)
  const [draftBusy, setDraftBusy] = useState(false)
  const [cancelTarget, setCancelTarget] = useState<AttendanceSession | null>(null)
  const [cancelReason, setCancelReason] = useState('')
  const [manualOpen, setManualOpen] = useState(false)
  const [manual, setManual] = useState({ clockInAt: '', clockOutAt: '', breakStart: '', breakEnd: '', reason: '' })

  const openCorrection = (session: AttendanceSession) =>
    setDraft({
      session,
      clockInAt: toLocalInput(session.clockInAt),
      clockOutAt: toLocalInput(session.clockOutAt),
      breaks: session.breaks.map((b) => ({ id: b.id, startedAt: toLocalInput(b.startedAt), endedAt: toLocalInput(b.endedAt) })),
      reason: '',
    })

  const submitCorrection = async () => {
    if (!draft) return
    if (!draft.reason.trim()) {
      showError(t('attendance.tab.reasonRequired'))
      return
    }

    setDraftBusy(true)
    try {
      const { session } = draft
      let version: string | null = session.version

      // Vergelijk op invoerniveau (minuutgranulariteit): alleen velden die de gebruiker
      // écht wijzigde worden verstuurd — anders zou elke submit de seconden wegknippen
      // en de correctiehistoriek vervuilen.
      const clockInChanged = draft.clockInAt !== toLocalInput(session.clockInAt)
      const clockOutChanged = draft.clockOutAt !== toLocalInput(session.clockOutAt)
      if (clockInChanged || clockOutChanged) {
        const updated = await correctSession(session.id, {
          clockInAt: clockInChanged ? fromLocalInput(draft.clockInAt) : null,
          clockOutAt: clockOutChanged ? fromLocalInput(draft.clockOutAt) : null,
          reason: draft.reason.trim(),
          version,
        })
        version = updated.version
      }

      for (const breakDraft of draft.breaks) {
        const original = session.breaks.find((b) => b.id === breakDraft.id)
        if (!original) continue
        const startChanged = breakDraft.startedAt !== toLocalInput(original.startedAt)
        const endChanged = breakDraft.endedAt !== toLocalInput(original.endedAt)
        if (startChanged || endChanged) {
          const updated = await correctBreak(session.id, breakDraft.id, {
            startedAt: startChanged ? fromLocalInput(breakDraft.startedAt) : null,
            endedAt: endChanged ? fromLocalInput(breakDraft.endedAt) : null,
            reason: draft.reason.trim(),
            version,
          })
          version = updated.version
        }
      }

      showSuccess(t('attendance.tab.correctionSaved'))
      setDraft(null)
      reload()
    } catch (err) {
      showError(describeApiError(err, t('attendance.tab.correctionFailed')).message)
    } finally {
      setDraftBusy(false)
    }
  }

  const submitCancel = async () => {
    if (!cancelTarget) return
    if (!cancelReason.trim()) {
      showError(t('attendance.tab.cancelReasonRequired'))
      return
    }

    try {
      await cancelSession(cancelTarget.id, cancelReason.trim(), cancelTarget.version)
      showSuccess(t('attendance.tab.cancelSaved'))
      setCancelTarget(null)
      setCancelReason('')
      reload()
    } catch (err) {
      showError(describeApiError(err, t('attendance.tab.cancelFailed')).message)
    }
  }

  const submitManual = async () => {
    const clockInAt = fromLocalInput(manual.clockInAt)
    const clockOutAt = fromLocalInput(manual.clockOutAt)
    if (!clockInAt || !clockOutAt || !manual.reason.trim()) {
      showError(t('attendance.tab.manualRequired'))
      return
    }

    const breakStart = fromLocalInput(manual.breakStart)
    const breakEnd = fromLocalInput(manual.breakEnd)
    try {
      await createManualSession({
        employeeId,
        clockInAt,
        clockOutAt,
        breaks: breakStart && breakEnd ? [{ startedAt: breakStart, endedAt: breakEnd }] : null,
        reason: manual.reason.trim(),
      })
      showSuccess(t('attendance.tab.manualSaved'))
      setManualOpen(false)
      setManual({ clockInAt: '', clockOutAt: '', breakStart: '', breakEnd: '', reason: '' })
      reload()
    } catch (err) {
      showError(describeApiError(err, t('attendance.tab.manualFailed')).message)
    }
  }

  return (
    <div>
      {canManagePin && <PinManagement employeeId={employeeId} />}

      <div className="ta-tab-toolbar">
        <div className="ta-period" role="group" aria-label={t('attendance.myTime.periodLabel')}>
          {[14, 31, 92].map((days) => (
            <button
              key={days}
              type="button"
              className={days === rangeDays ? 'ta-period-btn ta-period-btn-active' : 'ta-period-btn'}
              onClick={() => setRangeDays(days)}
            >
              {days === 14 ? t('attendance.tab.range2Weeks') : days === 31 ? t('attendance.tab.rangeMonth') : t('attendance.tab.range3Months')}
            </button>
          ))}
        </div>
        {canCorrect && (
          <Button variant="ghost" onClick={() => setManualOpen(true)}>
            {t('attendance.tab.manualEntry')}
          </Button>
        )}
      </div>

      {error && <p className="placeholder-text">{t(error)}</p>}
      {!error && !history && <p className="placeholder-text">{t('attendance.tab.loading')}</p>}

      {history && (
        <>
          <dl className="ta-totals">
            <div className="ta-total">
              <dt>{t('attendance.myTime.netWorked')}</dt>
              <dd>{formatDurationMinutes(history.totalNetMinutes)}</dd>
            </div>
            <div className="ta-total">
              <dt>{t('attendance.myTime.break')}</dt>
              <dd>{formatDurationMinutes(history.totalBreakMinutes)}</dd>
            </div>
            {history.totalPlannedMinutes != null && (
              <div className="ta-total">
                <dt>{t('attendance.tab.deviationVsPlanning')}</dt>
                <dd>{formatSignedDurationMinutes(history.totalNetMinutes - history.totalPlannedMinutes)}</dd>
              </div>
            )}
          </dl>

          {history.days.length === 0 && <p className="placeholder-text">{t('attendance.myTime.emptyPeriod')}</p>}

          {[...history.days].reverse().map((day) => (
            <section key={day.date} className="ta-day">
              <header className="ta-day-head">
                <h2>{formatDateLong(day.date)}</h2>
                <div className="ta-day-figures">
                  <span>{t('attendance.myTime.net')} <strong>{formatDurationMinutes(day.netMinutes)}</strong></span>
                  {day.plannedMinutes != null && (
                    <span>{t('attendance.myTime.planned')} <strong>{formatDurationMinutes(day.plannedMinutes)}</strong></span>
                  )}
                </div>
              </header>
              {day.sessions.map((session) => (
                <div key={`${day.date}-${session.id}`}>
                  <SessionTimeline session={session} />
                  {canCorrect && session.status !== 'Cancelled' && (
                    <div className="ta-session-actions">
                      <Button variant="ghost" onClick={() => openCorrection(session)}>
                        {t('attendance.tab.correct')}
                      </Button>
                      <Button variant="ghost" onClick={() => setCancelTarget(session)}>
                        {t('attendance.tab.cancel')}
                      </Button>
                    </div>
                  )}
                </div>
              ))}
            </section>
          ))}
        </>
      )}

      {draft && (
        <Modal
          title={t('attendance.tab.correctionTitle', { date: formatDate(draft.session.clockInAt) })}
          onClose={() => setDraft(null)}
          busy={draftBusy}
          footer={
            <>
              <Button variant="ghost" onClick={() => setDraft(null)} disabled={draftBusy}>
                {t('attendance.tab.cancel')}
              </Button>
              <Button onClick={submitCorrection} disabled={draftBusy}>
                {t('attendance.tab.saveCorrection')}
              </Button>
            </>
          }
        >
          <FormField label={t('attendance.tab.clockedInField')} htmlFor="corr-in">
            <input
              id="corr-in"
              type="datetime-local"
              value={draft.clockInAt}
              onChange={(event) => setDraft({ ...draft, clockInAt: event.target.value })}
            />
          </FormField>
          <FormField label={t('attendance.tab.clockedOutField')} htmlFor="corr-out" hint={t('attendance.tab.clockedOutHint')}>
            <input
              id="corr-out"
              type="datetime-local"
              value={draft.clockOutAt}
              onChange={(event) => setDraft({ ...draft, clockOutAt: event.target.value })}
            />
          </FormField>
          {draft.breaks.map((breakDraft, index) => (
            <div key={breakDraft.id} className="ta-break-row">
              <FormField label={t('attendance.tab.breakStartField', { index: index + 1 })} htmlFor={`corr-bs-${breakDraft.id}`}>
                <input
                  id={`corr-bs-${breakDraft.id}`}
                  type="datetime-local"
                  value={breakDraft.startedAt}
                  onChange={(event) =>
                    setDraft({
                      ...draft,
                      breaks: draft.breaks.map((b) => (b.id === breakDraft.id ? { ...b, startedAt: event.target.value } : b)),
                    })}
                />
              </FormField>
              <FormField label={t('attendance.tab.breakEndField', { index: index + 1 })} htmlFor={`corr-be-${breakDraft.id}`}>
                <input
                  id={`corr-be-${breakDraft.id}`}
                  type="datetime-local"
                  value={breakDraft.endedAt}
                  onChange={(event) =>
                    setDraft({
                      ...draft,
                      breaks: draft.breaks.map((b) => (b.id === breakDraft.id ? { ...b, endedAt: event.target.value } : b)),
                    })}
                />
              </FormField>
            </div>
          ))}
          <FormField label={t('attendance.tab.reasonField')} htmlFor="corr-reason" required>
            <textarea
              id="corr-reason"
              rows={2}
              value={draft.reason}
              onChange={(event) => setDraft({ ...draft, reason: event.target.value })}
              placeholder={t('attendance.tab.reasonPlaceholderCorrection')}
            />
          </FormField>
        </Modal>
      )}

      {cancelTarget && (
        <Modal
          title={t('attendance.tab.cancelTitle')}
          onClose={() => setCancelTarget(null)}
          footer={
            <>
              <Button variant="ghost" onClick={() => setCancelTarget(null)}>
                {t('attendance.tab.closeAction')}
              </Button>
              <Button onClick={submitCancel}>{t('attendance.tab.cancelConfirm')}</Button>
            </>
          }
        >
          <p>
            {t('attendance.tab.cancelExplanation', { dateTime: formatDateTime(cancelTarget.clockInAt) })}
          </p>
          <FormField label={t('attendance.tab.reasonField')} htmlFor="cancel-reason" required>
            <textarea
              id="cancel-reason"
              rows={2}
              value={cancelReason}
              onChange={(event) => setCancelReason(event.target.value)}
            />
          </FormField>
        </Modal>
      )}

      {manualOpen && (
        <Modal
          title={t('attendance.tab.manualTitle')}
          onClose={() => setManualOpen(false)}
          footer={
            <>
              <Button variant="ghost" onClick={() => setManualOpen(false)}>
                {t('attendance.tab.cancel')}
              </Button>
              <Button onClick={submitManual}>{t('attendance.tab.manualCreate')}</Button>
            </>
          }
        >
          <FormField label={t('attendance.tab.clockedInField')} htmlFor="man-in" required>
            <input
              id="man-in"
              type="datetime-local"
              value={manual.clockInAt}
              onChange={(event) => setManual({ ...manual, clockInAt: event.target.value })}
            />
          </FormField>
          <FormField label={t('attendance.tab.clockedOutField')} htmlFor="man-out" required>
            <input
              id="man-out"
              type="datetime-local"
              value={manual.clockOutAt}
              onChange={(event) => setManual({ ...manual, clockOutAt: event.target.value })}
            />
          </FormField>
          <div className="ta-break-row">
            <FormField label={t('attendance.tab.manualBreakStart')} htmlFor="man-bs">
              <input
                id="man-bs"
                type="datetime-local"
                value={manual.breakStart}
                onChange={(event) => setManual({ ...manual, breakStart: event.target.value })}
              />
            </FormField>
            <FormField label={t('attendance.tab.manualBreakEnd')} htmlFor="man-be">
              <input
                id="man-be"
                type="datetime-local"
                value={manual.breakEnd}
                onChange={(event) => setManual({ ...manual, breakEnd: event.target.value })}
              />
            </FormField>
          </div>
          <FormField label={t('attendance.tab.reasonField')} htmlFor="man-reason" required>
            <textarea
              id="man-reason"
              rows={2}
              value={manual.reason}
              onChange={(event) => setManual({ ...manual, reason: event.target.value })}
              placeholder={t('attendance.tab.reasonPlaceholderManual')}
            />
          </FormField>
        </Modal>
      )}
    </div>
  )
}

/** Prikklokcode-blok: status + genereren/zetten/intrekken; de code is nooit opvraagbaar. */
function PinManagement({ employeeId }: { employeeId: string }) {
  const { t } = useLocale()
  const { showSuccess, showError } = useToast()
  const [status, setStatus] = useState<AttendanceCredentialStatus | null>(null)
  const [reloadToken, setReloadToken] = useState(0)
  const [customPin, setCustomPin] = useState('')
  const [generatedPin, setGeneratedPin] = useState<string | null>(null)
  const [confirmDisable, setConfirmDisable] = useState(false)

  useEffect(() => {
    let mounted = true
    getAttendanceCredentialStatus(employeeId)
      .then((data) => {
        if (mounted) setStatus(data)
      })
      .catch(() => {
        if (mounted) setStatus(null)
      })
    return () => {
      mounted = false
    }
  }, [employeeId, reloadToken])

  const applyPin = async (pin: string | null) => {
    try {
      const result = await setAttendancePin(employeeId, pin)
      if (result.outcome !== 'Success') {
        showError(result.error ?? t('attendance.pin.setFailed'))
        return
      }

      if (result.generatedPin) {
        setGeneratedPin(result.generatedPin)
      } else {
        showSuccess(t('attendance.pin.setSaved'))
      }

      setCustomPin('')
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(describeApiError(err, t('attendance.pin.setFailed')).message)
    }
  }

  const disable = async () => {
    try {
      await disableAttendanceCredential(employeeId)
      showSuccess(t('attendance.pin.revokeSaved'))
      setConfirmDisable(false)
      setReloadToken((token) => token + 1)
    } catch (err) {
      showError(describeApiError(err, t('attendance.pin.revokeFailed')).message)
    }
  }

  return (
    <section className="ta-pin" aria-label={t('attendance.pin.title')}>
      <div className="ta-pin-status">
        <h3>{t('attendance.pin.title')}</h3>
        {!status?.hasCredential && <Badge tone="neutral">{t('attendance.pin.noCode')}</Badge>}
        {status?.hasCredential && status.isActive && <Badge tone="success">{t('attendance.pin.active')}</Badge>}
        {status?.hasCredential && !status.isActive && <Badge tone="danger">{t('attendance.pin.revoked')}</Badge>}
        {status?.lockedUntil && new Date(status.lockedUntil) > new Date() && (
          <Badge tone="warning">{t('attendance.pin.lockedUntil', { time: formatDateTime(status.lockedUntil) })}</Badge>
        )}
        {status?.lastUsedAt && (
          <span className="ta-pin-last">{t('attendance.pin.lastUsed', { time: formatDateTime(status.lastUsedAt) })}</span>
        )}
      </div>
      <div className="ta-pin-actions">
        <Button variant="ghost" onClick={() => applyPin(null)}>
          {status?.hasCredential ? t('attendance.pin.generateNew') : t('attendance.pin.generate')}
        </Button>
        <input
          type="text"
          inputMode="numeric"
          value={customPin}
          onChange={(event) => setCustomPin(event.target.value.replace(/\D/g, ''))}
          placeholder={t('attendance.pin.ownCode')}
          aria-label={t('attendance.pin.ownCode')}
          className="ta-pin-input"
        />
        <Button variant="ghost" onClick={() => applyPin(customPin)} disabled={customPin.length === 0}>
          {t('attendance.pin.setCode')}
        </Button>
        {status?.hasCredential && status.isActive && (
          <Button variant="ghost" onClick={() => setConfirmDisable(true)}>
            {t('attendance.pin.revoke')}
          </Button>
        )}
      </div>

      {generatedPin && (
        <Modal
          title={t('attendance.pin.generatedTitle')}
          onClose={() => setGeneratedPin(null)}
          footer={<Button onClick={() => setGeneratedPin(null)}>{t('attendance.pin.generatedConfirm')}</Button>}
        >
          <p>
            {t('attendance.pin.generatedExplanation')}
          </p>
          <p className="ta-pin-generated">{generatedPin}</p>
        </Modal>
      )}

      {confirmDisable && (
        <ConfirmDialog
          title={t('attendance.pin.revokeTitle')}
          message={t('attendance.pin.revokeExplanation')}
          confirmLabel={t('attendance.pin.revokeConfirm')}
          destructive
          onConfirm={disable}
          onCancel={() => setConfirmDisable(false)}
        />
      )}
    </section>
  )
}
