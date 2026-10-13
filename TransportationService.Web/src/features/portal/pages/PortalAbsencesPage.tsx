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
import { useLocale } from '../../../i18n/localeContext'
import {
  ABSENCE_STATUS_TONE,
  ABSENCE_TYPES,
  type Absence,
  type AbsencePartDay,
  type AbsenceStatus,
  type AbsenceType,
} from '../../absences/types'
import { cancelMyAbsence, createMyAbsence, listMyAbsences, uploadMyAbsenceAttachment } from '../api/portalApi'
import { MyLeaveBalanceCard } from '../../leave-balance/components/MyLeaveBalanceCard'
import { getLeaveTypes } from '../../leave-balance/api/leaveBalanceApi'
import type { LeaveType } from '../../leave-balance/types'
import './portal.css'

/** Translation keys per enum code; rendered via t() so the portal follows the user's language. */
const ABSENCE_TYPE_KEYS: Record<AbsenceType, string> = {
  Vacation: 'portalHome.absenceType.Vacation',
  Sick: 'portalHome.absenceType.Sick',
  Training: 'portalHome.absenceType.Training',
  PersonalLeave: 'portalHome.absenceType.PersonalLeave',
  Unpaid: 'portalHome.absenceType.Unpaid',
  Other: 'portalHome.absenceType.Other',
}

const ABSENCE_STATUS_KEYS: Record<AbsenceStatus, string> = {
  Requested: 'portalHome.absenceStatus.Requested',
  UnderReview: 'portalHome.absenceStatus.UnderReview',
  Approved: 'portalHome.absenceStatus.Approved',
  Rejected: 'portalHome.absenceStatus.Rejected',
  Cancelled: 'portalHome.absenceStatus.Cancelled',
}

const PART_DAY_KEYS: Record<AbsencePartDay, string> = {
  FullDay: 'portalHome.partDay.FullDay',
  Morning: 'portalHome.partDay.Morning',
  Afternoon: 'portalHome.partDay.Afternoon',
}

/** Own leave/absence requests: view status, request new, withdraw pending ones. */
export function PortalAbsencesPage() {
  const { showSuccess, showError } = useToast()
  const { t } = useLocale()

  const [absences, setAbsences] = useState<Absence[] | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [busy, setBusy] = useState(false)

  const [dialogOpen, setDialogOpen] = useState(false)
  const [type, setType] = useState<AbsenceType>('Vacation')
  const [leaveTypes, setLeaveTypes] = useState<LeaveType[]>([])
  const [leaveTypeId, setLeaveTypeId] = useState<string>('')
  const [startDate, setStartDate] = useState('')
  const [endDate, setEndDate] = useState('')
  const [partDay, setPartDay] = useState<AbsencePartDay>('FullDay')
  const [reason, setReason] = useState('')

  function reload() {
    listMyAbsences()
      .then((data) => {
        setAbsences(data)
        setLoadError(false)
      })
      .catch(() => setLoadError(true))
  }

  useEffect(reload, [])

  useEffect(() => {
    let mounted = true
    getLeaveTypes({ activeOnly: true, selfServiceOnly: true })
      .then((types) => {
        if (!mounted) return
        setLeaveTypes(types)
        if (types.length > 0) setLeaveTypeId((current) => current || types[0].id)
      })
      .catch(() => {
        /* leave types optional; the plain type selector stays available */
      })
    return () => {
      mounted = false
    }
  }, [])

  async function handleCreate(event: FormEvent) {
    event.preventDefault()
    if (!startDate || !endDate) {
      showError(t('portalHome.absences.chooseDates'))
      return
    }
    if (endDate < startDate) {
      showError(t('portalHome.absences.endBeforeStart'))
      return
    }
    const selectedLeaveType = leaveTypes.find((t) => t.id === leaveTypeId)
    setBusy(true)
    try {
      await createMyAbsence({
        type: selectedLeaveType ? selectedLeaveType.absenceType : type,
        leaveTypeId: selectedLeaveType ? selectedLeaveType.id : null,
        startDate,
        endDate,
        reason: reason.trim() || null,
        partDay: startDate === endDate ? partDay : 'FullDay',
      })
      showSuccess(t('portalHome.absences.submitted'))
      setDialogOpen(false)
      setStartDate('')
      setEndDate('')
      setPartDay('FullDay')
      setReason('')
      reload()
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('portalHome.absences.submitFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function handleAttachment(id: string, file: File) {
    setBusy(true)
    try {
      await uploadMyAbsenceAttachment(id, file)
      showSuccess(t('portalHome.absences.attachmentAdded'))
      reload()
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('portalHome.absences.attachmentFailed'))
    } finally {
      setBusy(false)
    }
  }

  async function handleCancel(id: string) {
    setBusy(true)
    try {
      await cancelMyAbsence(id)
      showSuccess(t('portalHome.absences.withdrawn'))
      reload()
    } catch (err) {
      showError(err instanceof ApiError ? err.message : t('portalHome.absences.withdrawFailed'))
    } finally {
      setBusy(false)
    }
  }

  if (loadError) return <ErrorState message={t('portalHome.absences.loadFailed')} />
  if (!absences) return <LoadingState message={t('portalHome.absences.loading')} />

  return (
    <div>
      <PageHeader
        title={t('portalHome.absences.title')}
        subtitle={t('portalHome.absences.subtitle')}
        action={<Button onClick={() => setDialogOpen(true)}>{t('portalHome.absences.requestLeave')}</Button>}
      />

      <MyLeaveBalanceCard />

      {absences.length === 0 && <p className="portal-empty">{t('portalHome.absences.empty')}</p>}

      <ul className="portal-absences">
        {absences.map((absence) => (
          <li key={absence.id} className="portal-absence">
            <div className="portal-absence-head">
              <strong>{t(ABSENCE_TYPE_KEYS[absence.type])}</strong>
              <Badge tone={ABSENCE_STATUS_TONE[absence.status]}>{t(ABSENCE_STATUS_KEYS[absence.status])}</Badge>
            </div>
            <div className="portal-absence-dates">
              {t('portalHome.absences.period', { start: absence.startDate, end: absence.endDate })}
              {absence.partDay !== 'FullDay' && ` · ${t(PART_DAY_KEYS[absence.partDay])}`}
            </div>
            {absence.reason && <div className="portal-absence-reason">{absence.reason}</div>}
            {absence.decisionNote && (
              <div className="portal-absence-decision">{t('portalHome.absences.decisionNote', { note: absence.decisionNote })}</div>
            )}
            {absence.hasAttachment && (
              <div className="portal-absence-reason">📎 {absence.attachmentFileName ?? t('portalHome.absences.attachmentPresent')}</div>
            )}
            {(absence.status === 'Requested' || absence.status === 'UnderReview') && (
              <div className="portal-absence-actions">
                {absence.status === 'Requested' && (
                  <Button variant="ghost" onClick={() => void handleCancel(absence.id)} disabled={busy}>
                    {t('portalHome.absences.withdraw')}
                  </Button>
                )}
                <label className="portal-attachment-label">
                  📎 {absence.hasAttachment ? t('portalHome.absences.replaceAttachment') : t('portalHome.absences.addAttachment')}
                  <input
                    type="file"
                    accept=".pdf,image/jpeg,image/png"
                    hidden
                    disabled={busy}
                    onChange={(e) => {
                      const file = e.target.files?.[0]
                      if (file) void handleAttachment(absence.id, file)
                      e.target.value = ''
                    }}
                  />
                </label>
              </div>
            )}
          </li>
        ))}
      </ul>

      {dialogOpen && (
        <Modal
          title={t('portalHome.absences.dialogTitle')}
          onClose={() => setDialogOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDialogOpen(false)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="portal-absence-form" disabled={busy}>
                {busy ? t('portalHome.absences.submitting') : t('portalHome.absences.submit')}
              </Button>
            </>
          }
        >
          <form id="portal-absence-form" className="portal-form" onSubmit={handleCreate} noValidate>
            <FormField label={t('portalHome.absences.typeField')} htmlFor="pa-type" required>
              {leaveTypes.length > 0 ? (
                <select id="pa-type" value={leaveTypeId} onChange={(e) => setLeaveTypeId(e.target.value)} disabled={busy}>
                  {leaveTypes.map((t) => (
                    <option key={t.id} value={t.id}>{t.name}</option>
                  ))}
                </select>
              ) : (
                <select id="pa-type" value={type} onChange={(e) => setType(e.target.value as AbsenceType)} disabled={busy}>
                  {ABSENCE_TYPES.map((absenceType) => (
                    <option key={absenceType} value={absenceType}>
                      {t(ABSENCE_TYPE_KEYS[absenceType])}
                    </option>
                  ))}
                </select>
              )}
            </FormField>
            <FormField label={t('portalHome.absences.fromField')} htmlFor="pa-start" required>
              <input id="pa-start" type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} disabled={busy} />
            </FormField>
            <FormField label={t('portalHome.absences.toField')} htmlFor="pa-end" required>
              <input id="pa-end" type="date" value={endDate} onChange={(e) => setEndDate(e.target.value)} disabled={busy} />
            </FormField>
            {startDate !== '' && startDate === endDate && (
              <FormField label={t('portalHome.absences.partDayField')} htmlFor="pa-partday">
                <select id="pa-partday" value={partDay} onChange={(e) => setPartDay(e.target.value as AbsencePartDay)} disabled={busy}>
                  {(Object.keys(PART_DAY_KEYS) as AbsencePartDay[]).map((p) => (
                    <option key={p} value={p}>
                      {t(PART_DAY_KEYS[p])}
                    </option>
                  ))}
                </select>
              </FormField>
            )}
            <FormField label={t('portalHome.absences.reasonField')} htmlFor="pa-reason">
              <textarea id="pa-reason" rows={2} value={reason} onChange={(e) => setReason(e.target.value)} disabled={busy} maxLength={500} />
            </FormField>
          </form>
        </Modal>
      )}
    </div>
  )
}
