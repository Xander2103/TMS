import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { listAbsences } from '../api/absencesApi'
import { ReviewAbsenceDialog } from '../components/ReviewAbsenceDialog'
import {
  ABSENCE_STATUS_TONE,
  ABSENCE_STATUSES,
  ABSENCE_TYPES,
  type Absence,
  type AbsenceStatus,
  type AbsenceType,
} from '../types'
import './absences-page.css'

function todayIso(): string {
  return new Date().toISOString().slice(0, 10)
}

function plusDaysIso(days: number): string {
  const date = new Date()
  date.setDate(date.getDate() + days)
  return date.toISOString().slice(0, 10)
}

/** Planning overview of absences across all employees within a date window. */
export function AbsencesPage() {
  const { t } = useLocale()
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const canApprove = hasPermission('absences.approve')
  const [reviewTarget, setReviewTarget] = useState<Absence | null>(null)

  const [from, setFrom] = useState(todayIso)
  const [to, setTo] = useState(() => plusDaysIso(60))
  const [typeFilter, setTypeFilter] = useState<AbsenceType | ''>('')
  const [statusFilter, setStatusFilter] = useState<AbsenceStatus | ''>('')

  const [absences, setAbsences] = useState<Absence[] | null>(null)
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    listAbsences({
      from: from || undefined,
      to: to || undefined,
      type: typeFilter || undefined,
      status: statusFilter || undefined,
    })
      .then((data) => {
        if (!mounted) return
        setAbsences(data)
        setLoadErrorKey(null)
      })
      .catch(() => {
        if (mounted) setLoadErrorKey('absences.page.loadFailed')
      })
    return () => {
      mounted = false
    }
  }, [from, to, typeFilter, statusFilter])

  return (
    <div>
      <Breadcrumbs items={[{ label: t('absences.page.title') }]} />
      <PageHeader title={t('absences.page.title')} subtitle={t('absences.page.subtitle')} />

      <div className="absp-filters">
        <label>
          {t('absences.page.from')}
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label>
          {t('absences.page.to')}
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <select
          value={typeFilter}
          onChange={(e) => setTypeFilter(e.target.value as AbsenceType | '')}
          aria-label={t('absences.page.typeFilter')}
        >
          <option value="">{t('absences.page.allTypes')}</option>
          {ABSENCE_TYPES.map((type) => (
            <option key={type} value={type}>
              {t(`absences.type.${type}`)}
            </option>
          ))}
        </select>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value as AbsenceStatus | '')}
          aria-label={t('absences.page.statusFilter')}
        >
          <option value="">{t('absences.page.allStatuses')}</option>
          {ABSENCE_STATUSES.map((status) => (
            <option key={status} value={status}>
              {t(`absences.status.${status}`)}
            </option>
          ))}
        </select>
      </div>

      {loadErrorKey && <p className="placeholder-text">{t(loadErrorKey)}</p>}
      {!loadErrorKey && absences === null && <p className="placeholder-text">{t('absences.page.loading')}</p>}
      {!loadErrorKey && absences !== null && absences.length === 0 && (
        <p className="placeholder-text">{t('absences.page.empty')}</p>
      )}

      {!loadErrorKey && absences !== null && absences.length > 0 && (
        <table className="absp-table">
          <thead>
            <tr>
              <th>{t('absences.page.colEmployee')}</th>
              <th>{t('absences.tab.colType')}</th>
              <th>{t('absences.tab.colFrom')}</th>
              <th>{t('absences.tab.colTo')}</th>
              <th>{t('absences.tab.colStatus')}</th>
              <th>{t('absences.tab.colReason')}</th>
              {canApprove && <th aria-label={t('absences.tab.colActions')} />}
            </tr>
          </thead>
          <tbody>
            {absences.map((absence) => (
              <tr key={absence.id} className="absp-row" onClick={() => navigate(`/employees/${absence.employeeId}`)}>
                <td>
                  {absence.employeeName}{' '}
                  <span className="absp-number">({absence.employeeNumber})</span>
                  {absence.isDriver && <Badge tone="info">{t('absences.page.driver')}</Badge>}
                </td>
                <td>
                  {t(`absences.type.${absence.type}`)}
                  {absence.hasAttachment && ' 📎'}
                </td>
                <td>{absence.startDate}</td>
                <td>{absence.endDate}</td>
                <td>
                  <Badge tone={ABSENCE_STATUS_TONE[absence.status]}>{t(`absences.status.${absence.status}`)}</Badge>
                </td>
                <td className="absp-reason" title={absence.reason ?? undefined}>
                  {absence.reason ?? '—'}
                </td>
                {canApprove && (
                  <td>
                    <Button
                      variant="ghost"
                      onClick={(event) => {
                        event.stopPropagation()
                        setReviewTarget(absence)
                      }}
                    >
                      {t('absences.page.review')}
                    </Button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {reviewTarget && (
        <ReviewAbsenceDialog
          absence={reviewTarget}
          onClose={() => setReviewTarget(null)}
          onChanged={(updated) => {
            setReviewTarget(updated)
            setAbsences((current) => current?.map((a) => (a.id === updated.id ? updated : a)) ?? null)
          }}
        />
      )}
    </div>
  )
}
