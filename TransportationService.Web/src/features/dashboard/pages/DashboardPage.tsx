import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { euro } from '../../invoices/types'
import { TRIP_STATUS_LABELS, TRIP_STATUS_TONE } from '../../planning/types'
import { ORDER_STATUS_LABELS, ORDER_STATUS_TONE } from '../../transport-orders/types'
import { getDashboard } from '../api/dashboardApi'
import type { Dashboard } from '../types'
import './dashboard.css'

interface Kpi {
  label: string
  value: string
  hint?: string
  to: string
  alert?: boolean
}

function formatPinnedAt(iso: string): string {
  const date = new Date(iso.endsWith('Z') || iso.includes('+') ? iso : `${iso}Z`)
  return date.toLocaleString('nl-BE', { dateStyle: 'short', timeStyle: 'short' })
}

/** One clickable KPI tile-grid, reused by the main block and the Voorraad/Taken sections. */
function KpiGrid({ kpis, onNavigate }: { kpis: Kpi[]; onNavigate: (to: string) => void }) {
  return (
    <div className="db-kpis">
      {kpis.map((kpi) => (
        <button
          key={kpi.label}
          type="button"
          className={kpi.alert ? 'db-kpi db-kpi-alert' : 'db-kpi'}
          onClick={() => onNavigate(kpi.to)}
        >
          <span className="db-kpi-label">{kpi.label}</span>
          <span className="db-kpi-value">{kpi.value}</span>
          {kpi.hint && <span className="db-kpi-hint">{kpi.hint}</span>}
        </button>
      ))}
    </div>
  )
}

export function DashboardPage() {
  const navigate = useNavigate()
  const [dashboard, setDashboard] = useState<Dashboard | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    let mounted = true
    getDashboard()
      .then((data) => {
        if (!mounted) return
        setDashboard(data)
        setLoadError(null)
      })
      .catch(() => {
        if (mounted) setLoadError('Het dashboard kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [])

  if (loadError) {
    return (
      <>
        <PageHeader title="Dashboard" />
        <p className="placeholder-text">{loadError}</p>
      </>
    )
  }

  if (!dashboard) {
    return (
      <>
        <PageHeader title="Dashboard" />
        <p className="placeholder-text">Dashboard laden…</p>
      </>
    )
  }

  const kpis: Kpi[] = [
    {
      label: 'Open opdrachten',
      value: String(dashboard.ordersOpenCount),
      hint: `${dashboard.ordersInExecutionCount} in uitvoering`,
      to: '/transport-orders',
    },
    {
      label: 'Ritten vandaag',
      value: String(dashboard.tripsTodayTotal),
      hint: `${dashboard.tripsTodayInProgress} onderweg`,
      to: '/planning',
      alert: dashboard.tripsTodayWithConflicts > 0,
    },
    {
      label: 'Omzet deze maand',
      value: euro(dashboard.revenueInvoicedThisMonth),
      hint: `${dashboard.ordersCompletedThisMonth} opdrachten afgerond`,
      to: '/invoices',
    },
    {
      label: 'Openstaand',
      value: euro(dashboard.outstandingAmount),
      hint: dashboard.overdueInvoiceCount > 0 ? `${dashboard.overdueInvoiceCount} vervallen` : 'Geen vervallen facturen',
      to: '/invoices',
      alert: dashboard.overdueInvoiceCount > 0,
    },
    {
      label: 'Chauffeurs afwezig vandaag',
      value: String(dashboard.driversAbsentToday),
      to: '/absences',
    },
    {
      label: 'Voertuigen inzetbaar',
      value: String(dashboard.vehiclesAvailable),
      hint: `${dashboard.maintenanceDueCount + dashboard.inspectionsDueCount} onderhoud/keuring gepland`,
      to: '/fleet',
      alert: dashboard.documentsExpiringCount + dashboard.openDamageCount > 0,
    },
    {
      label: 'Kwalificaties',
      value: String(dashboard.qualificationsExpiring30d),
      hint:
        dashboard.qualificationsExpired > 0
          ? `vervallen binnen 30 dagen · ${dashboard.qualificationsExpired} verlopen`
          : 'vervallen binnen 30 dagen',
      to: '/qualifications',
      alert: dashboard.qualificationsExpiring30d + dashboard.qualificationsExpired > 0,
    },
    {
      label: 'Open incidenten',
      value: String(dashboard.openIncidentCount),
      hint: 'nieuw of in behandeling',
      to: '/incidents',
      alert: dashboard.openIncidentCount > 0,
    },
    {
      label: 'POD ontbreekt',
      value: String(dashboard.missingPodCount),
      hint: 'afgeronde opdrachten zonder afleverbewijs',
      to: '/transport-orders',
      alert: dashboard.missingPodCount > 0,
    },
    {
      label: 'Mislukte scans',
      value: String(dashboard.failedScanCount),
      hint: 'laatste 7 dagen',
      to: '/exceptions',
      alert: dashboard.failedScanCount > 0,
    },
    {
      label: 'Onderhoud te laat',
      value: String(dashboard.overdueMaintenanceCount),
      hint: 'geplande onderhoudsbeurten over datum',
      to: '/fleet',
      alert: dashboard.overdueMaintenanceCount > 0,
    },
    {
      label: 'Nieuwe berichten',
      value: String(dashboard.unreadInternalMessages),
      hint: 'ongelezen interne berichten',
      to: '/inbox',
    },
  ]

  const inventory = dashboard.inventory
  const inventoryKpis: Kpi[] = inventory
    ? [
        { label: 'Lage voorraad', value: String(inventory.lowStock), to: '/inventory?status=LowStock' },
        {
          label: 'Kritieke voorraad',
          value: String(inventory.criticalStock),
          to: '/inventory?status=CriticalStock',
          alert: inventory.criticalStock > 0,
        },
        { label: 'Niet op voorraad', value: String(inventory.outOfStock), to: '/inventory?status=OutOfStock' },
        {
          label: 'Negatieve voorraad',
          value: String(inventory.negativeStock),
          to: '/inventory?status=NegativeStock',
          alert: inventory.negativeStock > 0,
        },
        { label: 'Open bestelvoorstellen', value: String(inventory.openReorderProposals), to: '/inventory' },
        {
          label: 'Achterstallige retouren',
          value: String(inventory.overdueReturns),
          to: '/inventory',
          alert: inventory.overdueReturns > 0,
        },
      ]
    : []

  const tasks = dashboard.tasks
  const taskKpis: Kpi[] = tasks
    ? [
        { label: 'Mijn open taken', value: String(tasks.myOpen), to: '/tasks?mine=1' },
        { label: 'Vandaag te doen', value: String(tasks.myDueToday), to: '/tasks?mine=1' },
        {
          label: 'Achterstallig',
          value: String(tasks.myOverdue),
          to: '/tasks?mine=1&overdue=1',
          alert: tasks.myOverdue > 0,
        },
        { label: 'Te bevestigen berichten', value: String(tasks.myToAcknowledge), to: '/inbox' },
        ...(tasks.teamOpen != null
          ? ([
              { label: 'Open teamtaken', value: String(tasks.teamOpen), to: '/tasks' },
              {
                label: 'Team achterstallig',
                value: String(tasks.teamOverdue ?? 0),
                to: '/tasks?overdue=1',
                alert: (tasks.teamOverdue ?? 0) > 0,
              },
              { label: 'Geblokkeerd', value: String(tasks.teamBlocked ?? 0), to: '/tasks?status=Blocked' },
              { label: 'Wacht op controle', value: String(tasks.teamWaitingReview ?? 0), to: '/tasks?review=1' },
            ] satisfies Kpi[])
          : []),
      ]
    : []

  return (
    <div>
      <PageHeader title="Dashboard" subtitle="Overzicht van vandaag en deze maand." />

      <KpiGrid kpis={kpis} onNavigate={navigate} />

      {inventoryKpis.length > 0 && (
        <section className="db-kpi-section" aria-label="Voorraad">
          <h2>Voorraad</h2>
          <KpiGrid kpis={inventoryKpis} onNavigate={navigate} />
        </section>
      )}

      {taskKpis.length > 0 && (
        <section className="db-kpi-section" aria-label="Taken">
          <h2>Taken</h2>
          <KpiGrid kpis={taskKpis} onNavigate={navigate} />
        </section>
      )}

      {dashboard.pinnedEmployeeNotes.length > 0 && (
        <section className="db-panel db-panel-alert" aria-label="Aandachtspunten personeel">
          <h2>Aandachtspunten personeel</h2>
          <ul className="db-list">
            {dashboard.pinnedEmployeeNotes.map((note) => (
              <li key={note.noteId}>
                <button
                  type="button"
                  className="db-row"
                  onClick={() => navigate(`/employees/${note.employeeId}?tab=profiel`)}
                >
                  <span className="db-row-main">
                    <strong>{note.employeeName}</strong> — {note.excerpt}
                  </span>
                  <span className="db-row-meta">
                    {formatPinnedAt(note.pinnedAt)} · {note.authorName ?? 'Systeem'}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}

      <div className="db-grid">
        <section className="db-panel">
          <h2>Ritten vandaag</h2>
          {dashboard.tripsToday.length === 0 && <p className="placeholder-text">Geen ritten gepland vandaag.</p>}
          {dashboard.tripsToday.length > 0 && (
            <ul className="db-list">
              {dashboard.tripsToday.map((trip) => (
                <li key={trip.id}>
                  <button type="button" className="db-row" onClick={() => navigate(`/planning/${trip.id}`)}>
                    <code>{trip.tripNumber}</code>
                    <span className="db-row-main">
                      {trip.driverName ?? 'Geen chauffeur'} · {trip.vehicleNumber ?? 'geen voertuig'} · {trip.orderCount} opdracht(en)
                    </span>
                    <span className="db-row-meta">
                      {trip.blockingConflictCount > 0 && <Badge tone="danger">⚠ {trip.blockingConflictCount}</Badge>}
                      <Badge tone={TRIP_STATUS_TONE[trip.status]}>{TRIP_STATUS_LABELS[trip.status]}</Badge>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section className="db-panel">
          <h2>Recente opdrachten</h2>
          {dashboard.recentOrders.length === 0 && <p className="placeholder-text">Nog geen opdrachten.</p>}
          {dashboard.recentOrders.length > 0 && (
            <ul className="db-list">
              {dashboard.recentOrders.map((order) => (
                <li key={order.id}>
                  <button type="button" className="db-row" onClick={() => navigate(`/transport-orders/${order.id}`)}>
                    <code>{order.orderNumber}</code>
                    <span className="db-row-main" title={order.goodsDescription}>
                      {order.customerName}
                    </span>
                    <span className="db-row-meta">
                      {order.orderDate}
                      <Badge tone={ORDER_STATUS_TONE[order.status]}>{ORDER_STATUS_LABELS[order.status]}</Badge>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>
    </div>
  )
}
