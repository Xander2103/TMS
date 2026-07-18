import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { Badge } from '../../../components/ui/Badge'
import { listAbsences } from '../api/absencesApi'
import {
  ABSENCE_STATUS_LABELS,
  ABSENCE_STATUS_TONE,
  ABSENCE_STATUSES,
  ABSENCE_TYPE_LABELS,
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
  const navigate = useNavigate()

  const [from, setFrom] = useState(todayIso)
  const [to, setTo] = useState(() => plusDaysIso(60))
  const [typeFilter, setTypeFilter] = useState<AbsenceType | ''>('')
  const [statusFilter, setStatusFilter] = useState<AbsenceStatus | ''>('')

  const [absences, setAbsences] = useState<Absence[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

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
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Afwezigheden konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [from, to, typeFilter, statusFilter])

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Afwezigheden' }]} />
      <PageHeader title="Afwezigheden" subtitle="Overzicht voor planning — beheer per medewerker via Personeel." />

      <div className="absp-filters">
        <label>
          Van
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label>
          Tot en met
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <select value={typeFilter} onChange={(e) => setTypeFilter(e.target.value as AbsenceType | '')} aria-label="Typefilter">
          <option value="">Alle types</option>
          {ABSENCE_TYPES.map((type) => (
            <option key={type} value={type}>
              {ABSENCE_TYPE_LABELS[type]}
            </option>
          ))}
        </select>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value as AbsenceStatus | '')}
          aria-label="Statusfilter"
        >
          <option value="">Alle statussen</option>
          {ABSENCE_STATUSES.map((status) => (
            <option key={status} value={status}>
              {ABSENCE_STATUS_LABELS[status]}
            </option>
          ))}
        </select>
      </div>

      {loadError && <p className="placeholder-text">{loadError}</p>}
      {!loadError && absences === null && <p className="placeholder-text">Afwezigheden laden…</p>}
      {!loadError && absences !== null && absences.length === 0 && (
        <p className="placeholder-text">Geen afwezigheden in deze periode.</p>
      )}

      {!loadError && absences !== null && absences.length > 0 && (
        <table className="absp-table">
          <thead>
            <tr>
              <th>Medewerker</th>
              <th>Type</th>
              <th>Van</th>
              <th>Tot en met</th>
              <th>Status</th>
              <th>Reden</th>
            </tr>
          </thead>
          <tbody>
            {absences.map((absence) => (
              <tr key={absence.id} className="absp-row" onClick={() => navigate(`/employees/${absence.employeeId}`)}>
                <td>
                  {absence.employeeName}{' '}
                  <span className="absp-number">({absence.employeeNumber})</span>
                  {absence.isDriver && <Badge tone="info">Chauffeur</Badge>}
                </td>
                <td>{ABSENCE_TYPE_LABELS[absence.type]}</td>
                <td>{absence.startDate}</td>
                <td>{absence.endDate}</td>
                <td>
                  <Badge tone={ABSENCE_STATUS_TONE[absence.status]}>{ABSENCE_STATUS_LABELS[absence.status]}</Badge>
                </td>
                <td className="absp-reason" title={absence.reason ?? undefined}>
                  {absence.reason ?? '—'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
