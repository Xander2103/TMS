import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Badge } from '../../../components/ui/Badge'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { getDossierAttentionCount } from '../../dossiers/api/dossiersApi'
import { TRIP_STATUS_TONE, type TripStatus } from '../../planning/types'
import { ORDER_STATUS_TONE, type TransportOrderStatus } from '../../transport-orders/types'
import { getDashboard } from '../api/dashboardApi'
import { WorkStatusCard } from '../../time-attendance/components/WorkStatusCard'
import { DASHBOARD_TILE_GROUPS, type DashboardExtras, type DashboardTile } from '../dashboardConfig'
import type { Dashboard } from '../types'
import { formatDateTime } from '../../../utils/dates'
import './dashboard.css'

/** Translation keys per status code; resolved via t() at render time. */
const TRIP_STATUS_KEYS: Record<TripStatus, string> = {
  Draft: 'appDashboard.tripStatus.Draft',
  Planned: 'appDashboard.tripStatus.Planned',
  InProgress: 'appDashboard.tripStatus.InProgress',
  Completed: 'appDashboard.tripStatus.Completed',
  Cancelled: 'appDashboard.tripStatus.Cancelled',
}

const ORDER_STATUS_KEYS: Record<TransportOrderStatus, string> = {
  Draft: 'appDashboard.orderStatus.Draft',
  Submitted: 'appDashboard.orderStatus.Submitted',
  Confirmed: 'appDashboard.orderStatus.Confirmed',
  Planned: 'appDashboard.orderStatus.Planned',
  InProgress: 'appDashboard.orderStatus.InProgress',
  Completed: 'appDashboard.orderStatus.Completed',
  Invoiced: 'appDashboard.orderStatus.Invoiced',
  Cancelled: 'appDashboard.orderStatus.Cancelled',
}

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
  const { hasAnyPermission, user } = useAuth()
  const { t } = useLocale()
  const [dashboard, setDashboard] = useState<Dashboard | null>(null)
  const [loadError, setLoadError] = useState(false)
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
        setLoadError(false)
      })
      .catch(() => {
        if (mounted) setLoadError(true)
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
        <PageHeader title={t('appDashboard.title')} />
        <p className="placeholder-text">{t('appDashboard.loadFailed')}</p>
      </>
    )
  }

  if (!dashboard) {
    return (
      <>
        <PageHeader title={t('appDashboard.title')} />
        <p className="placeholder-text">{t('appDashboard.loading')}</p>
      </>
    )
  }

  const extras: DashboardExtras = { dossierAttentionCount: attentionCount }
  const showTripsPanel = hasAnyPermission(['planning.view'])
  const showOrdersPanel = hasAnyPermission(['orders.view', 'orders.manage'])

  return (
    <div>
      <PageHeader title={t('appDashboard.title')} subtitle={t('appDashboard.subtitle')} />

      {user?.employeeId && hasAnyPermission(['attendance.self']) && <WorkStatusCard />}

      {visibleGroups.map((group) => {
        const tiles = group.tiles(dashboard, extras, t)
        if (tiles.length === 0) return null
        return (
          <section key={group.id} className="db-kpi-section" aria-label={t(group.title)}>
            <h2>{t(group.title)}</h2>
            <KpiGrid tiles={tiles} onNavigate={navigate} />
          </section>
        )
      })}

      {dashboard.pinnedEmployeeNotes.length > 0 && (
        <section className="db-panel db-panel-alert" aria-label={t('appDashboard.pinnedNotes.title')}>
          <h2>{t('appDashboard.pinnedNotes.title')}</h2>
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
                    {formatPinnedAt(note.pinnedAt)} · {note.authorName ?? t('appDashboard.pinnedNotes.systemAuthor')}
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
              <h2>{t('appDashboard.tripsPanel.title')}</h2>
              {dashboard.tripsToday.length === 0 && <p className="placeholder-text">{t('appDashboard.tripsPanel.empty')}</p>}
              {dashboard.tripsToday.length > 0 && (
                <ul className="db-list">
                  {dashboard.tripsToday.map((trip) => (
                    <li key={trip.id}>
                      <button type="button" className="db-row" onClick={() => navigate(`/planning/${trip.id}`)}>
                        <code>{trip.tripNumber}</code>
                        <span className="db-row-main">
                          {trip.driverName ?? t('appDashboard.tripsPanel.noDriver')} · {trip.vehicleNumber ?? t('appDashboard.tripsPanel.noVehicle')} ·{' '}
                          {t('appDashboard.tripsPanel.orders', { count: trip.orderCount })}
                        </span>
                        <span className="db-row-meta">
                          {trip.blockingConflictCount > 0 && <Badge tone="danger">⚠ {trip.blockingConflictCount}</Badge>}
                          <Badge tone={TRIP_STATUS_TONE[trip.status]}>{t(TRIP_STATUS_KEYS[trip.status])}</Badge>
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
              <h2>{t('appDashboard.ordersPanel.title')}</h2>
              {dashboard.recentOrders.length === 0 && <p className="placeholder-text">{t('appDashboard.ordersPanel.empty')}</p>}
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
                          <Badge tone={ORDER_STATUS_TONE[order.status]}>{t(ORDER_STATUS_KEYS[order.status])}</Badge>
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
