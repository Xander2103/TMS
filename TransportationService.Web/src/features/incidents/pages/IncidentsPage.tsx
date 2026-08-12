import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { EmptyState } from '../../../components/ui/EmptyState'
import { FilterBar } from '../../../components/ui/FilterBar'
import { useAuth } from '../../auth/authContextValue'
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
        setError('De incidenten konden niet worden geladen.')
        setLoaded(true)
      })
  }, [search, statusFilter, severityFilter])

  useEffect(() => {
    const timer = window.setTimeout(reload, 250)
    return () => window.clearTimeout(timer)
  }, [reload])

  const columns: Column<IncidentListItem>[] = [
    { key: 'title', header: 'Titel', render: (row) => row.title },
    { key: 'type', header: 'Type', render: (row) => incidentTypeLabel(row) },
    {
      key: 'severity',
      header: 'Ernst',
      render: (row) => <Badge tone={INCIDENT_SEVERITY_TONE[row.severity]}>{INCIDENT_SEVERITY_LABELS[row.severity]}</Badge>,
    },
    { key: 'customer', header: 'Klant', render: (row) => row.customerName ?? '—' },
    { key: 'dossier', header: 'Dossier', render: (row) => (row.dossierNumber ? <code>{row.dossierNumber}</code> : '—') },
    { key: 'responsible', header: 'Verantwoordelijke', render: (row) => row.responsibleName ?? '—' },
    {
      key: 'due',
      header: 'Deadline',
      render: (row) =>
        row.dueDate ? (
          row.isOverdue ? (
            <Badge tone="danger">{row.dueDate} · te laat</Badge>
          ) : (
            row.dueDate
          )
        ) : (
          '—'
        ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => <Badge tone={INCIDENT_STATUS_TONE[row.status]}>{INCIDENT_STATUS_LABELS[row.status]}</Badge>,
    },
  ]

  return (
    <div>
      <PageHeader
        title="Incidenten"
        subtitle="Registreer en volg schade, vertragingen en klachten op."
        action={canManage ? <Button onClick={() => navigate('/incidents/new')}>Nieuw incident</Button> : undefined}
      />

      <FilterBar search={search} onSearchChange={setSearch} searchPlaceholder="Zoek op titel...">
        <select
          value={statusFilter}
          onChange={(event) => setStatusFilter(event.target.value as '' | IncidentStatus)}
          aria-label="Status"
        >
          <option value="">Alle statussen</option>
          {Object.entries(INCIDENT_STATUS_LABELS).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
        <select
          value={severityFilter}
          onChange={(event) => setSeverityFilter(event.target.value as '' | IncidentSeverity)}
          aria-label="Ernst"
        >
          <option value="">Alle ernstniveaus</option>
          {Object.entries(INCIDENT_SEVERITY_LABELS).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </FilterBar>

      {error && <p className="placeholder-text">{error}</p>}
      {!error && loaded && incidents.length === 0 && <EmptyState message="Geen incidenten gevonden." />}
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
        {open ? 'Alle open problemen verbergen' : 'Alle open problemen (incidenten + uitvoeringsuitzonderingen)'}
      </button>
      {open && problems !== null && (
        problems.length === 0
          ? <p className="placeholder-text">Geen open problemen.</p>
          : (
            <table className="issued-items-table">
              <thead>
                <tr><th>Soort</th><th>Omschrijving</th><th>Ernst</th><th>Status</th><th>Order</th><th>Rit</th><th>Doorrekening</th></tr>
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
                    <td>{problem.kind === 'Incident' ? 'Incident' : 'Uitzondering'}</td>
                    <td>{problem.title}</td>
                    <td>{problem.severity}</td>
                    <td>{problem.status}</td>
                    <td>{problem.orderNumber ?? '—'}</td>
                    <td>{problem.tripNumber ?? '—'}</td>
                    <td>{problem.kind === 'Incident' ? (CHARGE_DECISION_LABELS[problem.chargeDecision] ?? problem.chargeDecision) : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )
      )}
    </section>
  )
}
