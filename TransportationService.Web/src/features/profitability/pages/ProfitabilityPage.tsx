import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { localizeApiError } from '../../../api/problemDetails'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import {
  downloadProfitabilityExport, getProfitabilityGrouped, getProfitabilityOverview, getTripExplanation,
} from '../api/profitabilityApi'
import {
  COST_TYPE_LABELS, DIMENSION_LABELS, REVENUE_SOURCE_LABELS, formatEuro, marginTone,
  type ProfitabilityDimension, type ProfitabilityGroup, type ProfitabilityOverview, type TripExplanation,
} from '../types'
import { formatDate } from '../../../utils/dates'
import { formatDecimal } from '../../../utils/numbers'
import './profitability.css'

function isoDaysAgo(days: number): string {
  const date = new Date()
  date.setDate(date.getDate() - days)
  return date.toISOString().slice(0, 10)
}

/**
 * Operational/commercial margin analysis. Estimates and actuals stay visibly separate,
 * missing cost data is flagged instead of hidden, and every trip explains its calculation.
 * Corrections deliberately run through the existing trip-costing page (audited there).
 */
export function ProfitabilityPage() {
  const { t } = useLocale()
  const { showError } = useToast()
  const { hasPermission } = useAuth()
  const canExport = hasPermission('profitability.export')
  const canSeeCostDetail = hasPermission('trip_costs.view')

  const [from, setFrom] = useState(() => isoDaysAgo(30))
  const [to, setTo] = useState(() => new Date().toISOString().slice(0, 10))
  const [dimension, setDimension] = useState<ProfitabilityDimension>('Customer')
  const [overview, setOverview] = useState<ProfitabilityOverview | null>(null)
  const [groups, setGroups] = useState<ProfitabilityGroup[]>([])
  const [explanation, setExplanation] = useState<TripExplanation | null>(null)
  const [busyExport, setBusyExport] = useState(false)
  const requestKey = useMemo(() => JSON.stringify({ from, to, dimension }), [from, to, dimension])

  useEffect(() => {
    let cancelled = false
    getProfitabilityOverview(from, to)
      .then((data) => {
        if (!cancelled) setOverview(data)
      })
      .catch((error: unknown) => showError(localizeApiError(t, error, t('profitability.page.loadFailed'))))
    getProfitabilityGrouped(dimension, from, to)
      .then((data) => {
        if (!cancelled) setGroups(data)
      })
      .catch(() => undefined)
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey])

  const summary = overview?.summary

  return (
    <div className="pf-page">
      <header className="pf-header">
        <h1>{t('profitability.page.title')}</h1>
        <div className="pf-filters">
          <input type="date" value={from} onChange={(event) => setFrom(event.target.value || from)} aria-label={t('profitability.page.fromAria')} />
          <input type="date" value={to} onChange={(event) => setTo(event.target.value || to)} aria-label={t('profitability.page.toAria')} />
          {canExport && (
            <Button variant="secondary" disabled={busyExport} onClick={() => {
              setBusyExport(true)
              void downloadProfitabilityExport(from, to)
                .catch(() => showError(t('profitability.page.exportFailed')))
                .finally(() => setBusyExport(false))
            }}>
              {busyExport ? t('profitability.page.exporting') : t('profitability.page.export')}
            </Button>
          )}
        </div>
      </header>

      {summary && (
        <div className="pf-kpis">
          <div className="pf-kpi"><span className="pf-kpi-value">{summary.tripCount}</span><span>{t('profitability.page.kpiTrips')}</span></div>
          <div className="pf-kpi"><span className="pf-kpi-value">{formatEuro(summary.revenueUsed)}</span><span>{t('profitability.page.kpiRevenue')}</span></div>
          <div className="pf-kpi">
            <span className="pf-kpi-value">{formatEuro(summary.projectedCost)}</span>
            <span>{t('profitability.page.kpiCosts', { amount: formatEuro(summary.estimatedCost) })}</span>
          </div>
          <div className={`pf-kpi pf-kpi-${marginTone(summary.marginPct)}`}>
            <span className="pf-kpi-value">{formatEuro(summary.margin)}</span>
            <span>{t('profitability.page.kpiMargin')}{summary.marginPct !== null ? ` (${summary.marginPct}%)` : ''}</span>
          </div>
          <div className={`pf-kpi${summary.unprofitableTrips > 0 ? ' pf-kpi-danger' : ''}`}>
            <span className="pf-kpi-value">{summary.unprofitableTrips}</span><span>{t('profitability.page.kpiUnprofitable')}</span>
          </div>
          <div className={`pf-kpi${summary.tripsWithMissingData > 0 ? ' pf-kpi-warning' : ''}`}>
            <span className="pf-kpi-value">{summary.tripsWithMissingData}</span><span>{t('profitability.page.kpiIncomplete')}</span>
          </div>
        </div>
      )}

      <section className="pf-panel">
        <div className="pf-panel-head">
          <h2>{t('profitability.page.ranking')}</h2>
          <div className="pf-tabs" role="tablist">
            {(Object.keys(DIMENSION_LABELS) as ProfitabilityDimension[]).map((option) => (
              <button key={option} role="tab" aria-selected={dimension === option}
                      className={dimension === option ? 'pf-tab-active' : ''}
                      onClick={() => setDimension(option)}>
                {t(DIMENSION_LABELS[option])}
              </button>
            ))}
          </div>
        </div>
        <div className="pf-table-wrap">
          <table className="pf-table">
            <thead>
              <tr>
                <th>{t(DIMENSION_LABELS[dimension])}</th>
                <th>{t('profitability.page.tripsHeader')}</th>
                <th>{t('profitability.page.revenueHeader')}</th>
                <th>{t('profitability.page.costsHeader')}</th>
                <th>{t('profitability.page.marginHeader')}</th>
                <th>{t('profitability.page.marginPctHeader')}</th>
              </tr>
            </thead>
            <tbody>
              {groups.map((group) => (
                <tr key={group.key}>
                  <td>
                    {group.label}
                    {group.containsAllocatedCosts && (
                      <span className="pf-note" title={t('profitability.page.allocatedNote')}> ⚖</span>
                    )}
                  </td>
                  <td>{group.tripCount}</td>
                  <td>{formatEuro(group.revenue)}</td>
                  <td>{formatEuro(group.projectedCost)}</td>
                  <td>{formatEuro(group.margin)}</td>
                  <td><Badge tone={marginTone(group.marginPct)}>{group.marginPct ?? '—'}%</Badge></td>
                </tr>
              ))}
              {groups.length === 0 && <tr><td colSpan={6} className="pf-note">{t('profitability.page.emptyPeriod')}</td></tr>}
            </tbody>
          </table>
        </div>
      </section>

      <section className="pf-panel">
        <div className="pf-panel-head"><h2>{t('profitability.page.tripsTitle')}</h2></div>
        <div className="pf-table-wrap">
          <table className="pf-table">
            <thead>
              <tr>
                <th>{t('profitability.page.tripHeader')}</th>
                <th>{t('profitability.page.dateHeader')}</th>
                <th>{t('profitability.page.customerHeader')}</th>
                <th>{t('profitability.page.driverHeader')}</th>
                <th>{t('profitability.page.revenueHeader')}</th>
                <th>{t('profitability.page.costsHeader')}</th>
                <th>{t('profitability.page.marginHeader')}</th>
                <th>{t('profitability.page.perKmHeader')}</th>
                <th>{t('profitability.page.dataQualityHeader')}</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {(overview?.trips ?? []).map((trip) => (
                <tr key={trip.tripId}>
                  <td><strong>{trip.tripNumber}</strong>{trip.isFinalized && <span className="pf-note"> {t('profitability.page.finalized')}</span>}</td>
                  <td>{formatDate(trip.tripDate)}</td>
                  <td>{trip.customerSummary ?? '—'}</td>
                  <td>{trip.driverName ?? '—'}</td>
                  <td>
                    {formatEuro(trip.revenueUsed)}
                    <div className="pf-note">{t(REVENUE_SOURCE_LABELS[trip.revenueSourceUsed])}</div>
                  </td>
                  <td>
                    {formatEuro(trip.projectedCost)}
                    {trip.estimatedCost > 0 && trip.actualCost === 0 && <div className="pf-note">{t('profitability.page.fullyEstimated')}</div>}
                    {trip.estimatedCost > 0 && trip.actualCost > 0 && <div className="pf-note">{t('profitability.page.partlyEstimated')}</div>}
                  </td>
                  <td><Badge tone={marginTone(trip.marginPct)}>{formatEuro(trip.margin)}{trip.marginPct !== null ? ` (${trip.marginPct}%)` : ''}</Badge></td>
                  <td>{trip.costPerKm !== null ? `${formatDecimal(trip.costPerKm, 2)}${trip.distanceIsActual ? '' : '*'}` : '—'}</td>
                  <td>
                    {trip.missingCostTypes.length > 0
                      ? (
                        <Badge tone="warning">
                          {t('profitability.page.missingBadge', {
                            types: trip.missingCostTypes.map((type) => (COST_TYPE_LABELS[type] ? t(COST_TYPE_LABELS[type]) : type)).join(', '),
                          })}
                        </Badge>
                      )
                      : <Badge tone="success">{t('profitability.page.complete')}</Badge>}
                  </td>
                  <td className="pf-actions">
                    <button type="button" className="pf-link" onClick={() => {
                      void getTripExplanation(trip.tripId)
                        .then(setExplanation)
                        .catch(() => showError(t('profitability.page.explainFailed')))
                    }}>
                      {t('profitability.page.explain')}
                    </button>
                    {canSeeCostDetail && <Link className="pf-link" to={`/planning/${trip.tripId}`}>{t('profitability.page.costsLink')}</Link>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p className="pf-note pf-legend">{t('profitability.page.legend')}</p>
      </section>

      {explanation && (
        <Modal title={t('profitability.page.calcTitle', { trip: explanation.tripNumber })} onClose={() => setExplanation(null)}>
          <h3 className="pf-exp-title">{t('profitability.page.revenueSection')}</h3>
          <ul className="pf-exp-list">
            {explanation.revenueLines.map((line, index) => (
              <li key={index}>
                <span>{line.description} <span className="pf-note">({line.source})</span></span>
                <strong>{formatEuro(line.amount)}</strong>
              </li>
            ))}
            {explanation.revenueLines.length === 0 && <li className="pf-note">{t('profitability.page.noRevenueLines')}</li>}
          </ul>
          <h3 className="pf-exp-title">{t('profitability.page.costsSection')}</h3>
          <ul className="pf-exp-list">
            {explanation.costLines.map((line, index) => (
              <li key={index}>
                <span>
                  {line.description}
                  <span className="pf-note">
                    {' '}({line.phase === 'Actual' ? t('profitability.page.phaseActual') : t('profitability.page.phaseEstimate')} · {line.source}
                    {line.isManualOverride ? ` · ${t('profitability.page.overridden')}` : ''})
                  </span>
                </span>
                <strong>{formatEuro(line.amount)}</strong>
              </li>
            ))}
            {explanation.costLines.length === 0 && <li className="pf-note">{t('profitability.page.noCostLines')}</li>}
          </ul>
          {explanation.missingCostTypes.length > 0 && (
            <p className="pf-note">
              {t('profitability.page.missingTypes', {
                types: explanation.missingCostTypes.map((type) => (COST_TYPE_LABELS[type] ? t(COST_TYPE_LABELS[type]) : type)).join(', '),
              })}
            </p>
          )}
          <p className="pf-note">{explanation.calculationNote}</p>
        </Modal>
      )}
    </div>
  )
}
