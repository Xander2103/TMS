import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { DataTable, type Column } from '../../../components/ui/DataTable'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { formatDurationMinutes, formatTime } from '../../../utils/dates'
import { getAttendanceOverview } from '../api/timeAttendanceApi'
import {
  ATTENDANCE_OVERVIEW_STATUSES, ATTENDANCE_OVERVIEW_STATUS_LABELS, ATTENDANCE_OVERVIEW_STATUS_TONE,
} from '../types'
import type { AttendanceOverview, AttendanceOverviewRow, AttendanceOverviewStatus } from '../types'
import './time-attendance.css'

const POLL_INTERVAL_MS = 30_000

function todayIso(): string {
  const now = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

/**
 * HR-liveoverzicht "Aanwezigheid": wie is aan het werk, in pauze, afwezig of ontbreekt.
 * Ververst elke 30 s (OperationsPage-patroon); rij-klik opent de Urenregistratie-tab
 * van de medewerker.
 */
export function AttendanceOverviewPage() {
  const navigate = useNavigate()
  const [date, setDate] = useState(todayIso)
  const [departmentId, setDepartmentId] = useState('')
  const [statusFilter, setStatusFilter] = useState<AttendanceOverviewStatus | ''>('')
  const [search, setSearch] = useState('')
  const [overview, setOverview] = useState<AttendanceOverview | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)
  const departments = useLookupOptions('/api/departments')

  useEffect(() => {
    const handle = setInterval(() => setTick((t) => t + 1), POLL_INTERVAL_MS)
    return () => clearInterval(handle)
  }, [])

  useEffect(() => {
    let mounted = true
    getAttendanceOverview({ date, departmentId: departmentId || undefined, search: search || undefined })
      .then((data) => {
        if (!mounted) return
        setOverview(data)
        setError(null)
      })
      .catch(() => {
        if (mounted) setError('Het aanwezigheidsoverzicht kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [date, departmentId, search, tick])

  const rows = useMemo(() => {
    if (!overview) return []
    return statusFilter ? overview.rows.filter((r) => r.status === statusFilter) : overview.rows
  }, [overview, statusFilter])

  const columns: Column<AttendanceOverviewRow>[] = [
    { key: 'name', header: 'Naam', render: (row) => row.name },
    { key: 'department', header: 'Afdeling', width: '160px', render: (row) => row.departmentName ?? '—' },
    {
      key: 'status',
      header: 'Status',
      width: '190px',
      render: (row) => (
        <span className="ta-cell-status">
          <Badge tone={ATTENDANCE_OVERVIEW_STATUS_TONE[row.status]}>
            {row.status === 'Absent' && row.absenceLabel
              ? row.absenceLabel
              : ATTENDANCE_OVERVIEW_STATUS_LABELS[row.status]}
          </Badge>
          {row.plannedNotClockedIn && <span className="ta-flag">Gepland, niet ingepunt</span>}
          {row.hasCorrections && <span className="ta-flag" style={{ color: 'var(--text)' }}>Gecorrigeerd</span>}
        </span>
      ),
    },
    { key: 'since', header: 'Sinds', width: '90px', render: (row) => (row.since ? formatTime(row.since) : '—') },
    {
      key: 'worked',
      header: 'Gewerkt',
      width: '100px',
      render: (row) => (row.workedMinutes > 0 || row.since ? formatDurationMinutes(row.workedMinutes) : '—'),
    },
    {
      key: 'break',
      header: 'Pauze',
      width: '100px',
      render: (row) => (row.breakMinutes > 0 ? formatDurationMinutes(row.breakMinutes) : '—'),
    },
    {
      key: 'planned',
      header: 'Gepland',
      width: '140px',
      render: (row) =>
        row.plannedStart && row.plannedEnd
          ? `${row.plannedStart.slice(0, 5)} – ${row.plannedEnd.slice(0, 5)}`
          : '—',
    },
  ]

  const summaryTiles = overview
    ? [
        { label: 'Aan het werk', value: overview.summary.working, alert: false },
        { label: 'Pauze', value: overview.summary.onBreak, alert: false },
        { label: 'Uitgepunt', value: overview.summary.clockedOut, alert: false },
        { label: 'Afwezig (goedgekeurd)', value: overview.summary.absent, alert: false },
        { label: 'Gepland, niet ingepunt', value: overview.summary.plannedNotClockedIn, alert: overview.summary.plannedNotClockedIn > 0 },
        { label: 'Vergeten uit te punten?', value: overview.summary.forgottenClockOut, alert: overview.summary.forgottenClockOut > 0 },
      ]
    : []

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Personeel' }, { label: 'Aanwezigheid' }]} />
      <PageHeader title="Aanwezigheid" subtitle="Live urenregistratie per medewerker." />

      <div className="ta-summary" aria-label="Samenvatting">
        {summaryTiles.map((tile) => (
          <div key={tile.label} className={tile.alert ? 'ta-summary-tile ta-summary-tile-alert' : 'ta-summary-tile'}>
            <span className="ta-summary-value">{tile.value}</span>
            <span className="ta-summary-label">{tile.label}</span>
          </div>
        ))}
      </div>

      <div className="ta-filters">
        <input
          type="search"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder="Zoek op naam of personeelsnummer..."
          aria-label="Zoeken"
        />
        <input
          type="date"
          value={date}
          onChange={(event) => setDate(event.target.value || todayIso())}
          aria-label="Datum"
        />
        <select
          value={departmentId}
          onChange={(event) => setDepartmentId(event.target.value)}
          aria-label="Afdeling"
        >
          <option value="">Alle afdelingen</option>
          {departments.options.map((dept) => (
            <option key={dept.id} value={dept.id}>
              {dept.name}
            </option>
          ))}
        </select>
        <select
          value={statusFilter}
          onChange={(event) => setStatusFilter(event.target.value as AttendanceOverviewStatus | '')}
          aria-label="Status"
        >
          <option value="">Alle statussen</option>
          {ATTENDANCE_OVERVIEW_STATUSES.map((status) => (
            <option key={status} value={status}>
              {ATTENDANCE_OVERVIEW_STATUS_LABELS[status]}
            </option>
          ))}
        </select>
      </div>

      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(row) => row.employeeId}
        isLoading={!overview && !error}
        error={error}
        emptyMessage="Geen medewerkers gevonden."
        onRowClick={(row) => navigate(`/employees/${row.employeeId}?tab=uren`)}
      />
    </div>
  )
}
