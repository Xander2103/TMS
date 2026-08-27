import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { describeApiError } from '../../../api/problemDetails'
import { Badge } from '../../../components/ui/Badge'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { TRIP_STATUS_LABELS, TRIP_STATUS_TONE } from '../../planning/types'
import { acknowledgeAlert, getOperationsOverview, listAlerts, resolveAlert } from '../api/operationsApi'
import {
  ALERT_CATEGORY_LABELS, ALERT_SEVERITY_META, ETA_SOURCE_LABELS, ETA_STATUS_META,
  LOCATION_SOURCE_LABELS, formatDelay,
  type AlertStatus, type OperationalAlert, type OperationsOverview,
} from '../types'
import { formatDateTime, formatTime } from '../../../utils/dates'
import './operations.css'

const POLL_INTERVAL_MS = 30_000

/**
 * The live control center. Refresh strategy is deliberate controlled polling: the overview
 * endpoint is a full, idempotent projection (it also runs the deduped alert sync), so
 * recovery after connection loss is simply the next poll — no event replay to get wrong.
 */
export function OperationsPage() {
  const { showError, showSuccess } = useToast()
  const { hasPermission } = useAuth()
  const { t } = useLocale()
  const navigate = useNavigate()
  const canManageAlerts = hasPermission('operations.manage_alerts')

  const [overview, setOverview] = useState<OperationsOverview | null>(null)
  const [alerts, setAlerts] = useState<OperationalAlert[]>([])
  const [alertFilter, setAlertFilter] = useState<AlertStatus | 'Open'>('Open')
  const [tick, setTick] = useState(0)
  const [busyAlertId, setBusyAlertId] = useState<string | null>(null)

  useEffect(() => {
    const handle = setInterval(() => setTick((value) => value + 1), POLL_INTERVAL_MS)
    return () => clearInterval(handle)
  }, [])

  useEffect(() => {
    let cancelled = false
    getOperationsOverview()
      .then((data) => {
        if (!cancelled) setOverview(data)
      })
      .catch((error: unknown) => showError(describeApiError(error, t('operations.page.overviewLoadFailed')).message))
    listAlerts()
      .then((data) => {
        if (!cancelled) setAlerts(data)
      })
      .catch(() => undefined)
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tick])

  const visibleAlerts = useMemo(() => alerts.filter((alert) =>
    alertFilter === 'Open' ? alert.status !== 'Resolved' : alert.status === alertFilter), [alerts, alertFilter])

  async function handleAlertAction(alert: OperationalAlert, action: 'acknowledge' | 'resolve') {
    setBusyAlertId(alert.id)
    try {
      const updated = action === 'acknowledge' ? await acknowledgeAlert(alert.id) : await resolveAlert(alert.id)
      setAlerts((current) => current.map((a) => (a.id === updated.id ? updated : a)))
      showSuccess(action === 'acknowledge' ? t('operations.page.alertAcknowledged') : t('operations.page.alertResolved'))
    } catch (error) {
      showError(describeApiError(error, t('operations.page.actionFailed')).message)
    } finally {
      setBusyAlertId(null)
    }
  }

  const counters = overview?.counters

  return (
    <div className="ops-page">
      <header className="ops-header">
        <h1>{t('operations.page.title')}</h1>
        <span className="ops-refresh">
          {overview ? t('operations.page.updatedAt', { time: formatTime(overview.generatedAt) }) : t('operations.page.loading')}
          {' · '}{t('operations.page.refreshEvery')}
        </span>
      </header>

      {counters && (
        <div className="ops-kpis">
          <KpiTile label={t('operations.page.kpiActiveTrips')} value={counters.activeTrips} />
          <KpiTile label={t('operations.page.kpiDelayed')} value={counters.delayedTrips} alert={counters.delayedTrips > 0} />
          <KpiTile label={t('operations.page.kpiOpenExceptions')} value={counters.openExceptions} alert={counters.openExceptions > 0} />
          <KpiTile label={t('operations.page.kpiCriticalIncidents')} value={counters.openCriticalIncidents} alert={counters.openCriticalIncidents > 0} />
          <KpiTile label={t('operations.page.kpiMissingPods')} value={counters.missingPods} alert={counters.missingPods > 0} />
          <KpiTile label={t('operations.page.kpiOpenAlerts')} value={counters.activeAlerts} alert={counters.criticalAlerts > 0} />
        </div>
      )}

      <div className="ops-columns">
        <section className="ops-trips" aria-label={t('operations.page.tripsLabel')}>
          <h2>{t('operations.page.tripsTitle')}</h2>
          <div className="ops-table-wrap">
            <table className="ops-table">
              <thead>
                <tr>
                  <th>{t('operations.page.colTrip')}</th>
                  <th>{t('operations.page.colStatus')}</th>
                  <th>{t('operations.page.colDriverEquipment')}</th>
                  <th>{t('operations.page.colProgress')}</th>
                  <th>{t('operations.page.colCurrentStop')}</th>
                  <th>{t('operations.page.colNextStop')}</th>
                  <th>{t('operations.page.colEta')}</th>
                  <th>{t('operations.page.colLastScan')}</th>
                  <th>{t('operations.page.colSignals')}</th>
                </tr>
              </thead>
              <tbody>
                {(overview?.trips ?? []).map((trip) => {
                  const delay = formatDelay(trip.delayMinutes)
                  return (
                    <tr key={trip.id} onClick={() => navigate(`/planning/${trip.id}`)} className="ops-row" tabIndex={0}
                        onKeyDown={(event) => {
                          if (event.key === 'Enter') navigate(`/planning/${trip.id}`)
                        }}>
                      <td><strong>{trip.tripNumber}</strong></td>
                      <td><Badge tone={TRIP_STATUS_TONE[trip.status]}>{t(TRIP_STATUS_LABELS[trip.status])}</Badge></td>
                      <td>
                        <div>{trip.driverName ?? '—'}</div>
                        <div className="ops-sub">
                          {trip.vehicleNumber ?? '—'}{trip.trailerNumber ? ` + ${trip.trailerNumber}` : ''}
                        </div>
                      </td>
                      <td>{t('operations.page.stopsProgress', { completed: trip.completedStopCount, total: trip.stopCount })}</td>
                      <td>{trip.currentStop ? `${trip.currentStop.city ?? trip.currentStop.locationName ?? '?'} (${trip.currentStop.status})` : '—'}</td>
                      <td>
                        {trip.nextStop ? (trip.nextStop.city ?? trip.nextStop.locationName ?? '?') : '—'}
                        {trip.nextStop?.currentEta && (
                          <div className="ops-sub">
                            ETA {formatTime(trip.nextStop.currentEta)}
                          </div>
                        )}
                      </td>
                      <td>
                        {trip.etaStatus ? (
                          <>
                            <Badge tone={ETA_STATUS_META[trip.etaStatus].tone}>
                              {t(ETA_STATUS_META[trip.etaStatus].label)}{delay ? ` +${delay}` : ''}
                            </Badge>
                            {trip.etaSource && (
                              <div className="ops-sub">{t('operations.page.etaSourcePrefix', { source: t(ETA_SOURCE_LABELS[trip.etaSource]) })}</div>
                            )}
                          </>
                        ) : (
                          <span className="ops-sub">{t('operations.page.noEta')}</span>
                        )}
                      </td>
                      <td>
                        {trip.lastScanAt
                          ? `${formatTime(trip.lastScanAt)} (${trip.lastScanResult})`
                          : '—'}
                      </td>
                      <td>
                        <div className="ops-signals">
                          {trip.openExceptionCount > 0 && (
                            <Badge tone="warning">{t('operations.page.exceptions', { count: trip.openExceptionCount })}</Badge>
                          )}
                          {trip.missingPodCount > 0 && <Badge tone="danger">{t('operations.page.missingPod')}</Badge>}
                          <span className="ops-sub" title={trip.position.description ?? undefined}>
                            {t(LOCATION_SOURCE_LABELS[trip.position.source])}
                            {trip.position.latitude !== null && ` (${trip.position.latitude.toFixed(4)}, ${trip.position.longitude?.toFixed(4)})`}
                          </span>
                        </div>
                      </td>
                    </tr>
                  )
                })}
                {overview && overview.trips.length === 0 && (
                  <tr><td colSpan={9} className="ops-sub">{t('operations.page.noActiveTrips')}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </section>

        <section className="ops-alerts" aria-label={t('operations.page.alertsLabel')}>
          <div className="ops-alerts-head">
            <h2>{t('operations.page.alertsTitle')}</h2>
            <select value={alertFilter} onChange={(event) => setAlertFilter(event.target.value as AlertStatus | 'Open')} aria-label={t('operations.page.alertsFilterLabel')}>
              <option value="Open">{t('operations.page.filterOpen')}</option>
              <option value="Active">{t('operations.page.filterActive')}</option>
              <option value="Acknowledged">{t('operations.page.filterAcknowledged')}</option>
              <option value="Resolved">{t('operations.page.filterResolved')}</option>
            </select>
          </div>
          <ul className="ops-alert-list">
            {visibleAlerts.map((alert) => {
              const meta = ALERT_SEVERITY_META[alert.severity]
              return (
                <li key={alert.id} className={`ops-alert ops-alert-${alert.severity.toLowerCase()}`}>
                  <div className="ops-alert-top">
                    <Badge tone={meta.tone}>{t(meta.label)}</Badge>
                    <span className="ops-sub">{t(ALERT_CATEGORY_LABELS[alert.category] ?? alert.category)}</span>
                    <span className="ops-sub">{formatDateTime(alert.createdAt)}</span>
                  </div>
                  <strong>{alert.title}</strong>
                  <p>{alert.message}</p>
                  {alert.status === 'Acknowledged' && alert.acknowledgedByName && (
                    <p className="ops-sub">{t('operations.page.acknowledgedBy', { name: alert.acknowledgedByName })}</p>
                  )}
                  <div className="ops-alert-actions">
                    {alert.linkPath && (
                      <button type="button" className="ops-link" onClick={() => navigate(alert.linkPath!)}>
                        {t('operations.page.open')}
                      </button>
                    )}
                    {canManageAlerts && alert.status === 'Active' && (
                      <button type="button" className="ops-link" disabled={busyAlertId === alert.id}
                              onClick={() => void handleAlertAction(alert, 'acknowledge')}>
                        {t('operations.page.acknowledge')}
                      </button>
                    )}
                    {canManageAlerts && alert.status !== 'Resolved' && (
                      <button type="button" className="ops-link" disabled={busyAlertId === alert.id}
                              onClick={() => void handleAlertAction(alert, 'resolve')}>
                        {t('operations.page.resolve')}
                      </button>
                    )}
                  </div>
                </li>
              )
            })}
            {visibleAlerts.length === 0 && <li className="ops-sub ops-alert-empty">{t('operations.page.noAlerts')}</li>}
          </ul>
        </section>
      </div>
    </div>
  )
}

function KpiTile({ label, value, alert = false }: { label: string; value: number; alert?: boolean }) {
  return (
    <div className={`ops-kpi${alert ? ' ops-kpi-alert' : ''}`}>
      <span className="ops-kpi-value">{value}</span>
      <span className="ops-kpi-label">{label}</span>
    </div>
  )
}
