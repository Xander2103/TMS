import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { EmptyState } from '../../../components/ui/EmptyState'
import { FilterBar } from '../../../components/ui/FilterBar'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { listIncidents, listProblems, type ProblemListItem } from '../api/incidentsApi'
import { CHARGE_DECISION_LABELS } from '../types'
import {
  INCIDENT_SEVERITY_LABELS,
  INCIDENT_SEVERITY_TONE,
  INCIDENT_STATUS_LABELS,
  INCIDENT_STATUS_TONE,
  incidentTypeLabel,
  type IncidentListItem,
  type IncidentSeverity,
  type IncidentStatus,
} from '../types'

/** Incidentenregister: schade, vertragingen, klachten en andere operationele meldingen. */
export function IncidentsPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const { t } = useLocale()
  const canManage = hasPermission('incidents.manage')

  const [incidents, setIncidents] = useState<IncidentListItem[]>([])
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<'' | IncidentStatus>('')
  const [severityFilter, setSeverityFilter] = useState<'' | IncidentSeverity>('')

  const reload = useCallback(() => {
    listIncidents({
      search: search || undefined,
      status: statusFilter || undefined,
      severity: severityFilter || undefined,
    })
      .then((data) => {
        setIncidents(data)
        setError(null)
        setLoaded(true)
      })
      .catch(() => {
        setError(t('incidents.list.loadError'))
        setLoaded(true)
      })
  }, [search, statusFilter, severityFilter, t])

  useEffect(() => {
    const timer = window.setTimeout(reload, 250)
    return () => window.clearTimeout(timer)
  }, [reload])

  const columns: Column<IncidentListItem>[] = [
    { key: 'title', header: t('incidents.list.colTitle'), render: (row) => row.title },
    { key: 'type', header: t('incidents.list.colType'), render: (row) => incidentTypeLabel(row, t) },
    {
      key: 'severity',
      header: t('incidents.list.colSeverity'),
      render: (row) => <Badge tone={INCIDENT_SEVERITY_TONE[row.severity]}>{t(INCIDENT_SEVERITY_LABELS[row.severity])}</Badge>,
    },
    { key: 'customer', header: t('incidents.list.colCustomer'), render: (row) => row.customerName ?? '—' },
    { key: 'dossier', header: t('incidents.list.colDossier'), render: (row) => (row.dossierNumber ? <code>{row.dossierNumber}</code> : '—') },
    { key: 'responsible', header: t('incidents.list.colResponsible'), render: (row) => row.responsibleName ?? '—' },
    {
      key: 'due',
      header: t('incidents.list.colDue'),
      render: (row) =>
        row.dueDate ? (
          row.isOverdue ? (
            <Badge tone="danger">{row.dueDate} · {t('incidents.list.overdueSuffix')}</Badge>
          ) : (
            row.dueDate
          )
        ) : (
          '—'
        ),
    },
    {
      key: 'status',
      header: t('incidents.list.colStatus'),
      render: (row) => <Badge tone={INCIDENT_STATUS_TONE[row.status]}>{t(INCIDENT_STATUS_LABELS[row.status])}</Badge>,
    },
  ]

  return (
    <div>
      <PageHeader
        title={t('incidents.list.title')}
        subtitle={t('incidents.list.subtitle')}
        action={canManage ? <Button onClick={() => navigate('/incidents/new')}>{t('incidents.list.newIncident')}</Button> : undefined}
      />

      <FilterBar search={search} onSearchChange={setSearch} searchPlaceholder={t('incidents.list.searchPlaceholder')}>
        <select
          value={statusFilter}
          onChange={(event) => setStatusFilter(event.target.value as '' | IncidentStatus)}
          aria-label={t('incidents.list.statusLabel')}
        >
          <option value="">{t('incidents.list.allStatuses')}</option>
          {Object.entries(INCIDENT_STATUS_LABELS).map(([value, labelKey]) => (
            <option key={value} value={value}>
              {t(labelKey)}
            </option>
          ))}
        </select>
        <select
          value={severityFilter}
          onChange={(event) => setSeverityFilter(event.target.value as '' | IncidentSeverity)}
          aria-label={t('incidents.list.severityLabel')}
        >
          <option value="">{t('incidents.list.allSeverities')}</option>
          {Object.entries(INCIDENT_SEVERITY_LABELS).map(([value, labelKey]) => (
            <option key={value} value={value}>
              {t(labelKey)}
            </option>
          ))}
        </select>
      </FilterBar>

      {error && <p className="placeholder-text">{error}</p>}
      {!error && loaded && incidents.length === 0 && <EmptyState message={t('incidents.list.empty')} />}
      {!error && incidents.length > 0 && (
        <DataTable columns={columns} rows={incidents} rowKey={(row) => row.id} onRowClick={(row) => navigate(`/incidents/${row.id}`)} />
      )}

      <ProblemsPanel />
    </div>
  )
}

/**
 * Wave 6 §4: de verenigde problemenlijst — open incidenten én open uitvoerings-
 * uitzonderingen in één overzicht; elke rij linkt naar zijn eigen detail.
 */
function ProblemsPanel() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const [problems, setProblems] = useState<ProblemListItem[] | null>(null)
  const [open, setOpen] = useState(false)

  useEffect(() => {
    if (!open) return
    listProblems()
      .then(setProblems)
      .catch(() => setProblems([]))
  }, [open])

  return (
    <section className="ui-form-section">
      <button type="button" className="issued-items-link" onClick={() => setOpen((o) => !o)}>
        {open ? t('incidents.problems.toggleHide') : t('incidents.problems.toggleShow')}
      </button>
      {open && problems !== null && (
        problems.length === 0
          ? <p className="placeholder-text">{t('incidents.problems.empty')}</p>
          : (
            <table className="issued-items-table">
              <thead>
                <tr>
                  <th>{t('incidents.problems.colKind')}</th>
                  <th>{t('incidents.problems.colDescription')}</th>
                  <th>{t('incidents.problems.colSeverity')}</th>
                  <th>{t('incidents.problems.colStatus')}</th>
                  <th>{t('incidents.problems.colOrder')}</th>
                  <th>{t('incidents.problems.colTrip')}</th>
                  <th>{t('incidents.problems.colCharge')}</th>
                </tr>
              </thead>
              <tbody>
                {problems.map((problem) => (
                  <tr
                    key={`${problem.kind}-${problem.id}`}
                    className="inv-order-row"
                    onClick={() => navigate(problem.kind === 'Incident'
                      ? `/incidents/${problem.id}`
                      : problem.tripId
                        ? `/trips/${problem.tripId}`
                        : '/incidents')}
                  >
                    <td>{problem.kind === 'Incident' ? t('incidents.problems.kindIncident') : t('incidents.problems.kindException')}</td>
                    <td>{problem.title}</td>
                    <td>{problem.severity}</td>
                    <td>{problem.status}</td>
                    <td>{problem.orderNumber ?? '—'}</td>
                    <td>{problem.tripNumber ?? '—'}</td>
                    <td>{problem.kind === 'Incident' ? t(CHARGE_DECISION_LABELS[problem.chargeDecision] ?? problem.chargeDecision) : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )
      )}
    </section>
  )
}
