import { PageHeader } from '../../../components/layout/PageHeader'
import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { describeApiError } from '../../../api/problemDetails'
import { isForbiddenError, isRetryableError } from '../../../api/notFound'
import { AccessDeniedState } from '../../../components/feedback/AccessDeniedState'
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
import { labelFor } from '../../../utils/labels'

const POLL_INTERVAL_MS = 30_000
/** Newest first; more than this would push the trip table off the screen. */
const FAILED_VISIBLE = 5

/**
 * The live control center. Refresh strategy is deliberate controlled polling: the overview
 * endpoint is a full, idempotent, read-only projection (PERF-H4: the deduped alert sync runs in
 * a server-side background job, not per poll), so recovery after connection loss is simply the
 * next poll — no event replay to get wrong.
 */
export function OperationsPage() {
  const { showError, showSuccess } = useToast()
  const { hasPermission, hasAnyPermission } = useAuth()
  const { t } = useLocale()
  const navigate = useNavigate()
  const canManageAlerts = hasPermission('operations.manage_alerts')
  const canOpenIncidents = hasAnyPermission(['incidents.view', 'incidents.manage'])
  const failedRef = useRef<HTMLElement>(null)
  const alertsRef = useRef<HTMLElement>(null)
  /** UX-009: the delay and POD tiles filter the table below them. */
  const [tripFilter, setTripFilter] = useState<'delayed' | 'missingPod' | null>(null)
  const [showAllFailed, setShowAllFailed] = useState(false)

  const [overview, setOverview] = useState<OperationsOverview | null>(null)
  const [alerts, setAlerts] = useState<OperationalAlert[]>([])
  const [alertFilter, setAlertFilter] = useState<AlertStatus | 'Open'>('Open')
  const [tick, setTick] = useState(0)
  const [busyAlertId, setBusyAlertId] = useState<string | null>(null)
  // SEC-M5: a 4xx answer (403 after a role change mid-session, …) never changes on the next poll,
  // so it stops the polling; a 403 replaces the page with "Geen toegang". Network/5xx keep polling.
  const [forbidden, setForbidden] = useState(false)
  const [pollingStopped, setPollingStopped] = useState(false)
  const [overviewFailed, setOverviewFailed] = useState(false)
  const [alertsFailed, setAlertsFailed] = useState(false)
  const [alertsRetryable, setAlertsRetryable] = useState(true)
  /** Toast once per outage, not once per 30-second poll. */
  const overviewFailedRef = useRef(false)

  useEffect(() => {
    if (pollingStopped) return
    const handle = setInterval(() => setTick((value) => value + 1), POLL_INTERVAL_MS)
    return () => clearInterval(handle)
  }, [pollingStopped])

  useEffect(() => {
    let cancelled = false
    // ONE refresh cycle: the alert list is read AFTER the overview, so the "Open meldingen" tile
    // and the panel describe the same reconciled rows (B21; the reconciliation itself runs in a
    // server-side job). A failed alert read is shown, never silently rendered as "no alerts".
    // A refusal on either call ends the cycle: no alert read after a 403/4xx overview.
    let refused = false
    const handleRefusal = (error: unknown) => {
      if (isRetryableError(error)) return false
      refused = true
      if (cancelled) return true
      setPollingStopped(true)
      if (isForbiddenError(error)) setForbidden(true)
      return true
    }
    getOperationsOverview()
      .then((data) => {
        if (cancelled) return
        setOverview(data)
        setOverviewFailed(false)
        overviewFailedRef.current = false
      })
      .catch((error: unknown) => {
        if (cancelled) return
        setOverviewFailed(true)
        if (handleRefusal(error) && isForbiddenError(error)) return
        if (!overviewFailedRef.current) showError(describeApiError(error, t('operations.page.overviewLoadFailed')).message)
        overviewFailedRef.current = true
      })
      .then(() => (refused ? null : listAlerts()))
      .then((data) => {
        if (cancelled || data === null) return
        setAlerts(data)
        setAlertsFailed(false)
      })
      .catch((error: unknown) => {
        if (cancelled) return
        setAlertsFailed(true)
        setAlertsRetryable(!handleRefusal(error))
      })
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
  const visibleTrips = useMemo(() => (overview?.trips ?? []).filter((trip) =>
    tripFilter === 'delayed' ? trip.etaStatus === 'Late' || (trip.delayMinutes ?? 0) > 0
      : tripFilter === 'missingPod' ? trip.missingPodCount > 0
        : true), [overview, tripFilter])
  const toggleTripFilter = (filter: 'delayed' | 'missingPod') => setTripFilter((current) => (current === filter ? null : filter))

  if (forbidden) return <AccessDeniedState />

  return (
    <div className="ops-page">
      <PageHeader
        title={t('operations.page.title')}
        subtitle={(
          <span className="ops-refresh">
            {overview
              ? t('operations.page.updatedAt', { time: formatTime(overview.generatedAt) })
              : overviewFailed ? t('operations.page.overviewLoadFailed') : t('operations.page.loading')}
            {' · '}{t('operations.page.refreshEvery')}
          </span>
        )}
      />

      {counters && (
        <div className="ops-kpis">
          <KpiTile label={t('operations.page.kpiFailedDeliveries')} value={counters.failedDeliveries ?? 0} alert={(counters.failedDeliveries ?? 0) > 0} onClick={(counters.failedDeliveries ?? 0) > 0 ? () => failedRef.current?.scrollIntoView({ block: 'start' }) : undefined} />
          <KpiTile label={t('operations.page.kpiActiveTrips')} value={counters.activeTrips} />
          <KpiTile label={t('operations.page.kpiDelayed')} value={counters.delayedTrips} alert={counters.delayedTrips > 0} pressed={tripFilter === 'delayed'} onClick={() => toggleTripFilter('delayed')} />
          <KpiTile label={t('operations.page.kpiOpenExceptions')} value={counters.openExceptions} alert={counters.openExceptions > 0} to={hasPermission('exceptions.view') ? '/exceptions' : undefined} />
          <KpiTile label={t('operations.page.kpiCriticalIncidents')} value={counters.openCriticalIncidents} alert={counters.openCriticalIncidents > 0} to={canOpenIncidents ? '/incidents' : undefined} />
          <KpiTile label={t('operations.page.kpiMissingPods')} value={counters.missingPods} alert={counters.missingPods > 0} pressed={tripFilter === 'missingPod'} onClick={() => toggleTripFilter('missingPod')} />
          <KpiTile label={t('operations.page.kpiOpenAlerts')} value={counters.activeAlerts} alert={counters.criticalAlerts > 0} onClick={() => alertsRef.current?.scrollIntoView({ block: 'start' })} />
        </div>
      )}

      {(overview?.failedDeliveries?.length ?? 0) > 0 && (
        <section className="ops-failed" aria-label={t('operations.page.failedTitle')} ref={failedRef}>
          <h2>{t('operations.page.failedTitle')}</h2>
          <p className="ops-sub">{t('operations.page.failedHint')}</p>
          <ul className="ops-failed-list">
            {(showAllFailed ? overview!.failedDeliveries! : overview!.failedDeliveries!.slice(0, FAILED_VISIBLE)).map((failed) => (
              <li key={failed.incidentId} className="ops-failed-item">
                <div className="ops-failed-what">
                  <strong>{[failed.orderNumber, failed.tripNumber].filter(Boolean).join(' · ')}</strong>
                  {failed.customerName && <span> · {failed.customerName}</span>}
                  <span className="ops-sub"> · {formatDateTime(failed.occurredAt)}</span>
                  <p>{failed.description}</p>
                </div>
                <div className="ops-failed-actions">
                  {canOpenIncidents && (
                    <Link className="ui-button ui-button-primary" to={`/incidents/${failed.incidentId}`}>
                      {failed.redeliveryOrderId ? t('operations.page.failedFollowUp') : t('operations.page.failedAct')}
                    </Link>
                  )}
                  {failed.redeliveryOrderId && failed.redeliveryOrderNumber && (
                    <Link to={`/transport-orders/${failed.redeliveryOrderId}`}>
                      {t('operations.page.failedRedelivery', { orderNumber: failed.redeliveryOrderNumber })}
                    </Link>
                  )}
                  {failed.dossierId && failed.dossierNumber && hasPermission('dossiers.view') && (
                    <Link to={`/dossiers/${failed.dossierId}`}>{failed.dossierNumber}</Link>
                  )}
                  {failed.tripId && failed.tripNumber && hasPermission('planning.view') && (
                    <Link to={`/planning/${failed.tripId}`}>{failed.tripNumber}</Link>
                  )}
                </div>
              </li>
            ))}
          </ul>
          {overview!.failedDeliveries!.length > FAILED_VISIBLE && (
            <button type="button" className="ops-link" aria-expanded={showAllFailed} onClick={() => setShowAllFailed((current) => !current)}>
              {showAllFailed
                ? t('operations.page.failedShowLess')
                : t('operations.page.failedShowAll', { count: overview!.failedDeliveries!.length })}
            </button>
          )}
          {showAllFailed && (counters?.failedDeliveries ?? 0) > overview!.failedDeliveries!.length && (
            <p className="ops-sub">
              {t('operations.page.failedMore', { shown: overview!.failedDeliveries!.length, total: counters!.failedDeliveries! })}
            </p>
          )}
        </section>
      )}

      <div className="ops-columns">
        <section className="ops-trips" aria-label={t('operations.page.tripsLabel')}>
          <h2>{t('operations.page.tripsTitle')}</h2>
          <div className="ops-table-wrap">
            <table className="ops-table">
              <thead>
                <tr>
                  <th>{t('operations.page.colTrip')}</th>
                  <th>{t('operations.page.colSignals')}</th>
                  <th>{t('operations.page.colEta')}</th>
                  <th>{t('operations.page.colProgress')}</th>
                  <th>{t('operations.page.colDriverEquipment')}</th>
                  <th>{t('operations.page.colLastScan')}</th>
                </tr>
              </thead>
              <tbody>
                {visibleTrips.map((trip) => {
                  const delay = formatDelay(trip.delayMinutes)
                  return (
                    <tr key={trip.id} onClick={() => navigate(`/planning/${trip.id}`)} className="ops-row" tabIndex={0}
                        onKeyDown={(event) => {
                          if (event.key === 'Enter') navigate(`/planning/${trip.id}`)
                        }}>
                      <td>
                        <strong>{trip.tripNumber}</strong>
                        {trip.orders && trip.orders.length > 0 && (
                          <div className="ops-sub ops-orders">
                            <Link
                              to={trip.orders[0].dossierId ? `/dossiers/${trip.orders[0].dossierId}` : `/transport-orders/${trip.orders[0].id}`}
                              onClick={(event) => event.stopPropagation()}
                              title={trip.orders[0].dossierNumber ? `${trip.orders[0].dossierNumber} · ${trip.orders[0].orderNumber}` : trip.orders[0].orderNumber}
                            >
                              {trip.orders[0].dossierNumber ?? trip.orders[0].orderNumber}
                            </Link>
                            {trip.orders[0].customerName && ` · ${trip.orders[0].customerName}`}
                            {trip.orders.length > 1 && ` +${trip.orders.length - 1}`}
                          </div>
                        )}
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
                      <td>
                        {trip.etaStatus ? (
                          <>
                            <Badge tone={ETA_STATUS_META[trip.etaStatus].tone}>
                              {delay ? t('operations.page.lateBy', { delay }) : t(ETA_STATUS_META[trip.etaStatus].label)}
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
                        <div>
                          <Badge tone={TRIP_STATUS_TONE[trip.status]}>{t(TRIP_STATUS_LABELS[trip.status])}</Badge>{' '}
                          {t('operations.page.stopsProgress', { completed: trip.completedStopCount, total: trip.stopCount })}
                        </div>
                        {trip.currentStop && (
                          <div className="ops-sub">
                            {t('operations.page.colCurrentStop')}: {trip.currentStop.city ?? trip.currentStop.locationName ?? '?'} ({labelFor(t, trip.currentStop.status, 'myTrips.stopStatus')})
                          </div>
                        )}
                        {trip.nextStop && (
                          <div className="ops-sub">
                            {t('operations.page.colNextStop')}: {trip.nextStop.city ?? trip.nextStop.locationName ?? '?'}
                            {trip.nextStop.currentEta && ` · ETA ${formatTime(trip.nextStop.currentEta)}`}
                          </div>
                        )}
                      </td>
                      <td>
                        <div>{trip.driverName ?? '—'}</div>
                        <div className="ops-sub">
                          {trip.vehicleNumber ?? '—'}{trip.trailerNumber ? ` + ${trip.trailerNumber}` : ''}
                        </div>
                      </td>
                      <td>
                        {trip.lastScanAt
                          ? `${formatTime(trip.lastScanAt)} (${trip.lastScanResult ? labelFor(t, trip.lastScanResult, 'scanning.scanResult') : '—'})`
                          : '—'}
                      </td>
                    </tr>
                  )
                })}
                {overview && visibleTrips.length === 0 && (
                  <tr><td colSpan={6} className="ops-sub">{tripFilter ? t('operations.page.noFilteredTrips') : t('operations.page.noActiveTrips')}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </section>

        <section className="ops-alerts" aria-label={t('operations.page.alertsLabel')} ref={alertsRef}>
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
            {alertsFailed && (
              <li className="ops-sub ops-alert-empty">
                {t('operations.page.alertsLoadFailed')}
                {alertsRetryable && (
                  <>
                    {' '}
                    <button type="button" className="ops-link-button" onClick={() => setTick((value) => value + 1)}>{t('operations.page.retry')}</button>
                  </>
                )}
              </li>
            )}
            {!alertsFailed && visibleAlerts.length === 0 && (
              <li className="ops-sub ops-alert-empty">{alertFilter === 'Open' ? t('operations.page.noOpenAlerts') : t('operations.page.noAlerts')}</li>
            )}
          </ul>
        </section>
      </div>
    </div>
  )
}

/** UX-009: a tile either goes where its number can be worked on, or filters the table; a bare count stays text. */
function KpiTile({ label, value, alert = false, to, onClick, pressed }: {
  label: string
  value: number
  alert?: boolean
  to?: string
  onClick?: () => void
  pressed?: boolean
}) {
  const className = `ops-kpi${alert ? ' ops-kpi-alert' : ''}${to || onClick ? ' ops-kpi-action' : ''}${pressed ? ' ops-kpi-pressed' : ''}`
  const content = (
    <>
      <span className="ops-kpi-value">{value}</span>
      <span className="ops-kpi-label">{label}</span>
    </>
  )
  if (to) return <Link className={className} to={to}>{content}</Link>
  if (onClick) return <button type="button" className={className} onClick={onClick} aria-pressed={pressed}>{content}</button>
  return <div className={className}>{content}</div>
}
