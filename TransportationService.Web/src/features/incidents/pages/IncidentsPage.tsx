import { useCallback, useEffect, useMemo, useState } from 'react'
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
import { formatDate } from '../../../utils/dates'
import { EXCEPTION_SEVERITY_LABELS, EXCEPTION_STATUS_LABELS, type ExceptionSeverity, type ExecutionExceptionStatus } from '../../exceptions/types'
import { labelFor } from '../../../utils/labels'
import { TruncatedText } from '../../../components/ui/TruncatedText'
import { Pagination } from '../../../components/ui/Pagination'
import { DensityToggle } from '../../../components/ui/DensityToggle'
import { useListDensity } from '../../../components/ui/listDensity'
import { tableSortOf, useUrlListState } from '../../../hooks/useUrlListState'

const PAGE_SIZE = 25
const SEVERITY_RANK: Record<string, number> = { Low: 0, Medium: 1, High: 2, Critical: 3 }

/** B26: the register's view in the URL. The API returns the filtered set (≤ 500), sorted and paged here. */
const LIST_STATE = {
  defaults: { search: '', status: '', severity: '', sort: '', dir: '' },
  allowed: { sort: ['title', 'severity', 'customer', 'due', 'status'], dir: ['asc', 'desc'] },
}
const DESC_BY_DEFAULT = ['severity']

function compareIncidents(a: IncidentListItem, b: IncidentListItem, sort: string): number {
  switch (sort) {
    case 'title':
      return a.title.localeCompare(b.title)
    case 'severity':
      return (SEVERITY_RANK[a.severity] ?? 0) - (SEVERITY_RANK[b.severity] ?? 0)
    case 'customer':
      return (a.customerName ?? '').localeCompare(b.customerName ?? '')
    case 'due':
      // No due date sorts last in both directions is not needed here: it sorts as "latest".
      return (a.dueDate ?? '9999').localeCompare(b.dueDate ?? '9999')
    case 'status':
      return a.status.localeCompare(b.status)
    default:
      return 0
  }
}

/** Incidentenregister: schade, vertragingen, klachten en andere operationele meldingen. */
type TranslateFn = (key: string) => string
interface ProblemLike { kind: string; severity: string; status: string }

/** Combined problems table: incident and exception enums each through their own label map. */
function problemSeverityLabel(t: TranslateFn, problem: ProblemLike): string {
  if (problem.kind === 'Incident') return labelFor(t, problem.severity, 'incidents.severity')
  const key = EXCEPTION_SEVERITY_LABELS[problem.severity as ExceptionSeverity]
  return key ? t(key) : labelFor(t, problem.severity, 'exceptions.severity')
}

function problemStatusLabel(t: TranslateFn, problem: ProblemLike): string {
  if (problem.kind === 'Incident') return labelFor(t, problem.status, 'incidents.status')
  const key = EXCEPTION_STATUS_LABELS[problem.status as ExecutionExceptionStatus]
  return key ? t(key) : labelFor(t, problem.status, 'exceptions.status')
}

export function IncidentsPage() {
  const navigate = useNavigate()
  const { hasPermission } = useAuth()
  const { t } = useLocale()
  const canManage = hasPermission('incidents.manage')

  const [incidents, setIncidents] = useState<IncidentListItem[]>([])
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const { state, apply, reset, isFiltered } = useUrlListState(LIST_STATE)
  const { search } = state
  const statusFilter = state.status as '' | IncidentStatus
  const severityFilter = state.severity as '' | IncidentSeverity
  const [density, setDensity] = useListDensity('incidents')

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

  // The whole filtered set is in memory, so sorting before paging is correct (never "this page only").
  const sorted = useMemo(() => {
    if (!state.sort) return incidents
    const factor = (tableSortOf(state.sort, state.dir, DESC_BY_DEFAULT)?.dir === 'desc' ? -1 : 1)
    return [...incidents].sort((a, b) => factor * compareIncidents(a, b, state.sort))
  }, [incidents, state.sort, state.dir])
  const pageRows = sorted.slice((state.page - 1) * PAGE_SIZE, state.page * PAGE_SIZE)

  const columns: Column<IncidentListItem>[] = [
    { key: 'title', header: t('incidents.list.colTitle'), sortKey: 'title', render: (row) => <TruncatedText tooltip={row.title}>{row.title}</TruncatedText> },
    { key: 'type', header: t('incidents.list.colType'), width: '140px', render: (row) => incidentTypeLabel(row, t) },
    {
      key: 'severity',
      header: t('incidents.list.colSeverity'),
      width: '100px',
      sortKey: 'severity',
      render: (row) => <Badge tone={INCIDENT_SEVERITY_TONE[row.severity]}>{t(INCIDENT_SEVERITY_LABELS[row.severity])}</Badge>,
    },
    { key: 'customer', header: t('incidents.list.colCustomer'), width: '170px', sortKey: 'customer', render: (row) => (row.customerName ? <TruncatedText tooltip={row.customerName}>{row.customerName}</TruncatedText> : '—') },
    { key: 'dossier', header: t('incidents.list.colDossier'), width: '105px', render: (row) => (row.dossierNumber ? <code className="ui-nowrap">{row.dossierNumber}</code> : '—') },
    { key: 'responsible', header: t('incidents.list.colResponsible'), width: '120px', render: (row) => (row.responsibleName ? <TruncatedText tooltip={row.responsibleName}>{row.responsibleName}</TruncatedText> : '—') },
    {
      key: 'due',
      header: t('incidents.list.colDue'),
      width: '150px',
      sortKey: 'due',
      render: (row) =>
        row.dueDate ? (
          row.isOverdue ? (
            <Badge tone="danger">{formatDate(row.dueDate)} · {t('incidents.list.overdueSuffix')}</Badge>
          ) : (
            <span className="ui-nowrap">{formatDate(row.dueDate)}</span>
          )
        ) : (
          '—'
        ),
    },
    {
      key: 'status',
      header: t('incidents.list.colStatus'),
      width: '130px',
      sortKey: 'status',
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

      <FilterBar
        search={search}
        onSearchChange={(value) => apply({ search: value }, { replace: true })}
        searchPlaceholder={t('incidents.list.searchPlaceholder')}
        onClear={isFiltered ? reset : undefined}
        trailing={<DensityToggle value={density} onChange={setDensity} />}
      >
        <select
          className="ui-filter-select"
          value={statusFilter}
          onChange={(event) => apply({ status: event.target.value })}
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
          className="ui-filter-select"
          value={severityFilter}
          onChange={(event) => apply({ severity: event.target.value })}
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
        <DataTable
          fixedLayout
          density={density}
          sort={tableSortOf(state.sort, state.dir, DESC_BY_DEFAULT)}
          onSortChange={(next) => apply({ sort: next.key, dir: next.dir })}
          columns={columns}
          rows={pageRows}
          rowKey={(row) => row.id}
          onRowClick={(row) => navigate(`/incidents/${row.id}`)}
          emptyAction={canManage ? <Button onClick={() => navigate('/incidents/new')}>{t('incidents.list.newIncident')}</Button> : undefined}
        />
      )}
      {!error && <Pagination page={state.page} pageSize={PAGE_SIZE} totalCount={sorted.length} onPageChange={(next) => apply({ page: next })} />}

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
      <Button variant="secondary" onClick={() => setOpen((o) => !o)}>
        {open ? t('incidents.problems.toggleHide') : t('incidents.problems.toggleShow')}
      </Button>
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
                    <td>{problemSeverityLabel(t, problem)}</td>
                    <td>{problemStatusLabel(t, problem)}</td>
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
