import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { describeApiError } from '../../../api/problemDetails'
import { Badge } from '../../../components/ui/Badge'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { TRIP_STATUS_LABELS, TRIP_STATUS_TONE } from '../../planning/types'
import { acknowledgeAlert, getOperationsOverview, listAlerts, resolveAlert } from '../api/operationsApi'
import {
  ALERT_CATEGORY_LABELS, ALERT_SEVERITY_META, ETA_SOURCE_LABELS, ETA_STATUS_META,
  LOCATION_SOURCE_LABELS, formatDelay,
  type AlertStatus, type OperationalAlert, type OperationsOverview,
} from '../types'
import { formatDateTime } from '../../../utils/dates'
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
  const navigate = useNavigate()
  const canManageAlerts = hasPermission('operations.manage_alerts')

  const [overview, setOverview] = useState<OperationsOverview | null>(null)
  const [alerts, setAlerts] = useState<OperationalAlert[]>([])
  const [alertFilter, setAlertFilter] = useState<AlertStatus | 'Open'>('Open')
  const [tick, setTick] = useState(0)
  const [busyAlertId, setBusyAlertId] = useState<string | null>(null)

  useEffect(() => {
    const handle = setInterval(() => setTick((t) => t + 1), POLL_INTERVAL_MS)
    return () => clearInterval(handle)
  }, [])

  useEffect(() => {
    let cancelled = false
    getOperationsOverview()
      .then((data) => {
        if (!cancelled) setOverview(data)
      })
      .catch((error: unknown) => showError(describeApiError(error, 'Overzicht laden mislukt.').message))
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
      showSuccess(action === 'acknowledge' ? 'Melding bevestigd.' : 'Melding afgehandeld.')
    } catch (error) {
      showError(describeApiError(error, 'Actie mislukt.').message)
    } finally {
      setBusyAlertId(null)
    }
  }

  const counters = overview?.counters

  return (
    <div className="ops-page">
      <header className="ops-header">
        <h1>Operationeel centrum</h1>
        <span className="ops-refresh">
          {overview ? `Bijgewerkt ${new Date(overview.generatedAt).toLocaleTimeString('nl-BE')}` : 'Laden…'}
          {' · '}ververst elke 30 s
        </span>
      </header>

      {counters && (
        <div className="ops-kpis">
          <KpiTile label="Actieve ritten" value={counters.activeTrips} />
          <KpiTile label="Vertraagd" value={counters.delayedTrips} alert={counters.delayedTrips > 0} />
          <KpiTile label="Open afwijkingen" value={counters.openExceptions} alert={counters.openExceptions > 0} />
          <KpiTile label="Kritieke incidenten" value={counters.openCriticalIncidents} alert={counters.openCriticalIncidents > 0} />
          <KpiTile label="POD ontbreekt" value={counters.missingPods} alert={counters.missingPods > 0} />
          <KpiTile label="Open meldingen" value={counters.activeAlerts} alert={counters.criticalAlerts > 0} />
        </div>
      )}

      <div className="ops-columns">
        <section className="ops-trips" aria-label="Actieve ritten">
          <h2>Ritten vandaag &amp; onderweg</h2>
          <div className="ops-table-wrap">
            <table className="ops-table">
              <thead>
                <tr>
                  <th>Rit</th>
                  <th>Status</th>
                  <th>Chauffeur / materieel</th>
                  <th>Voortgang</th>
                  <th>Huidige stop</th>
                  <th>Volgende stop</th>
                  <th>ETA</th>
                  <th>Laatste scan</th>
                  <th>Signalen</th>
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
                      <td><Badge tone={TRIP_STATUS_TONE[trip.status]}>{TRIP_STATUS_LABELS[trip.status]}</Badge></td>
                      <td>
                        <div>{trip.driverName ?? '—'}</div>
                        <div className="ops-sub">
                          {trip.vehicleNumber ?? '—'}{trip.trailerNumber ? ` + ${trip.trailerNumber}` : ''}
                        </div>
                      </td>
                      <td>{trip.completedStopCount}/{trip.stopCount} stops</td>
                      <td>{trip.currentStop ? `${trip.currentStop.city ?? trip.currentStop.locationName ?? '?'} (${trip.currentStop.status})` : '—'}</td>
                      <td>
                        {trip.nextStop ? (trip.nextStop.city ?? trip.nextStop.locationName ?? '?') : '—'}
                        {trip.nextStop?.currentEta && (
                          <div className="ops-sub">
                            ETA {new Date(trip.nextStop.currentEta).toLocaleTimeString('nl-BE', { hour: '2-digit', minute: '2-digit' })}
                          </div>
                        )}
                      </td>
                      <td>
                        {trip.etaStatus ? (
                          <>
                            <Badge tone={ETA_STATUS_META[trip.etaStatus].tone}>
                              {ETA_STATUS_META[trip.etaStatus].label}{delay ? ` +${delay}` : ''}
                            </Badge>
                            {trip.etaSource && <div className="ops-sub">bron: {ETA_SOURCE_LABELS[trip.etaSource]}</div>}
                          </>
                        ) : (
                          <span className="ops-sub">geen ETA</span>
                        )}
                      </td>
                      <td>
                        {trip.lastScanAt
                          ? `${new Date(trip.lastScanAt).toLocaleTimeString('nl-BE', { hour: '2-digit', minute: '2-digit' })} (${trip.lastScanResult})`
                          : '—'}
                      </td>
                      <td>
                        <div className="ops-signals">
                          {trip.openExceptionCount > 0 && <Badge tone="warning">{trip.openExceptionCount} afwijking(en)</Badge>}
                          {trip.missingPodCount > 0 && <Badge tone="danger">POD ontbreekt</Badge>}
                          <span className="ops-sub" title={trip.position.description ?? undefined}>
                            {LOCATION_SOURCE_LABELS[trip.position.source]}
                            {trip.position.latitude !== null && ` (${trip.position.latitude.toFixed(4)}, ${trip.position.longitude?.toFixed(4)})`}
                          </span>
                        </div>
                      </td>
                    </tr>
                  )
                })}
                {overview && overview.trips.length === 0 && (
                  <tr><td colSpan={9} className="ops-sub">Geen actieve ritten.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </section>

        <section className="ops-alerts" aria-label="Meldingen">
          <div className="ops-alerts-head">
            <h2>Meldingen</h2>
            <select value={alertFilter} onChange={(event) => setAlertFilter(event.target.value as AlertStatus | 'Open')} aria-label="Filter meldingen">
              <option value="Open">Open</option>
              <option value="Active">Nieuw</option>
              <option value="Acknowledged">Bevestigd</option>
              <option value="Resolved">Afgehandeld</option>
            </select>
          </div>
          <ul className="ops-alert-list">
            {visibleAlerts.map((alert) => {
              const meta = ALERT_SEVERITY_META[alert.severity]
              return (
                <li key={alert.id} className={`ops-alert ops-alert-${alert.severity.toLowerCase()}`}>
                  <div className="ops-alert-top">
                    <Badge tone={meta.tone}>{meta.label}</Badge>
                    <span className="ops-sub">{ALERT_CATEGORY_LABELS[alert.category] ?? alert.category}</span>
                    <span className="ops-sub">{formatDateTime(alert.createdAt)}</span>
                  </div>
                  <strong>{alert.title}</strong>
                  <p>{alert.message}</p>
                  {alert.status === 'Acknowledged' && alert.acknowledgedByName && (
                    <p className="ops-sub">Bevestigd door {alert.acknowledgedByName}</p>
                  )}
                  <div className="ops-alert-actions">
                    {alert.linkPath && (
                      <button type="button" className="ops-link" onClick={() => navigate(alert.linkPath!)}>
                        Openen
                      </button>
                    )}
                    {canManageAlerts && alert.status === 'Active' && (
                      <button type="button" className="ops-link" disabled={busyAlertId === alert.id}
                              onClick={() => void handleAlertAction(alert, 'acknowledge')}>
                        Bevestigen
                      </button>
                    )}
                    {canManageAlerts && alert.status !== 'Resolved' && (
                      <button type="button" className="ops-link" disabled={busyAlertId === alert.id}
                              onClick={() => void handleAlertAction(alert, 'resolve')}>
                        Afhandelen
                      </button>
                    )}
                  </div>
                </li>
              )
            })}
            {visibleAlerts.length === 0 && <li className="ops-sub ops-alert-empty">Geen meldingen.</li>}
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
