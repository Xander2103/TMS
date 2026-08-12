import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { useAuth } from '../../auth/authContextValue'
import { getDossierAttentionCount } from '../../dossiers/api/dossiersApi'
import { TRIP_STATUS_LABELS, TRIP_STATUS_TONE } from '../../planning/types'
import { ORDER_STATUS_LABELS, ORDER_STATUS_TONE } from '../../transport-orders/types'
import { getDashboard } from '../api/dashboardApi'
import { DASHBOARD_TILE_GROUPS, type DashboardExtras, type DashboardTile } from '../dashboardConfig'
import type { Dashboard } from '../types'
import { formatDateTime } from '../../../utils/dates'
import './dashboard.css'

function formatPinnedAt(iso: string): string {
  return formatDateTime(iso)
}

/** One clickable KPI tile-grid, reused by every tile group. */
function KpiGrid({ tiles, onNavigate }: { tiles: DashboardTile[]; onNavigate: (to: string) => void }) {
  return (
    <div className="db-kpis">
      {tiles.map((tile) => (
        <button
          key={tile.label}
          type="button"
          className={tile.alert ? 'db-kpi db-kpi-alert' : 'db-kpi'}
          onClick={() => onNavigate(tile.to)}
        >
          <span className="db-kpi-label">{tile.label}</span>
          <span className="db-kpi-value">{tile.value}</span>
          {tile.hint && <span className="db-kpi-hint">{tile.hint}</span>}
        </button>
      ))}
    </div>
  )
}

/**
 * Rolgericht dashboard (Wave 1 §16): alleen de tegelgroepen waarvan de audience-permissies
 * matchen worden gerenderd, met duidelijke groepstitels. Data buiten /api/dashboard wordt
 * lazy opgehaald — enkel wanneer de groep die ze toont ook echt zichtbaar is.
 */
export function DashboardPage() {
  const navigate = useNavigate()
  const { hasAnyPermission } = useAuth()
  const [dashboard, setDashboard] = useState<Dashboard | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [attentionCount, setAttentionCount] = useState<number | null>(null)

  const visibleGroups = useMemo(
    () => DASHBOARD_TILE_GROUPS.filter((g) => g.audience.length === 0 || hasAnyPermission(g.audience)),
    [hasAnyPermission],
  )
  const planningVisible = visibleGroups.some((g) => g.id === 'planning')
  // Lazy: de dossierteller wordt alleen opgehaald wanneer de planninggroep rendert
  // voor iemand die dossiers mag zien.
  const wantsAttentionCount = planningVisible && hasAnyPermission(['dossiers.view', 'dossiers.manage'])

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

  useEffect(() => {
    if (!wantsAttentionCount) return
    let mounted = true
    getDossierAttentionCount()
      .then((count) => {
        if (mounted) setAttentionCount(count)
      })
      .catch(() => undefined)
    return () => {
      mounted = false
    }
  }, [wantsAttentionCount])

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

  const extras: DashboardExtras = { dossierAttentionCount: attentionCount }
  const showTripsPanel = hasAnyPermission(['planning.view'])
  const showOrdersPanel = hasAnyPermission(['orders.view', 'orders.manage'])

  return (
    <div>
      <PageHeader title="Dashboard" subtitle="Overzicht van vandaag en deze maand." />

      {visibleGroups.map((group) => {
        const tiles = group.tiles(dashboard, extras)
        if (tiles.length === 0) return null
        return (
          <section key={group.id} className="db-kpi-section" aria-label={group.title}>
            <h2>{group.title}</h2>
            <KpiGrid tiles={tiles} onNavigate={navigate} />
          </section>
        )
      })}

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

      {(showTripsPanel || showOrdersPanel) && (
        <div className="db-grid">
          {showTripsPanel && (
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
          )}

          {showOrdersPanel && (
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
          )}
        </div>
      )}
    </div>
  )
}
