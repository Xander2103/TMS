import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { describeApiError } from '../../../api/problemDetails'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import {
  downloadProfitabilityExport, getProfitabilityGrouped, getProfitabilityOverview, getTripExplanation,
} from '../api/profitabilityApi'
import {
  COST_TYPE_LABELS, DIMENSION_LABELS, REVENUE_SOURCE_LABELS, formatEuro, marginTone,
  type ProfitabilityDimension, type ProfitabilityGroup, type ProfitabilityOverview, type TripExplanation,
} from '../types'
import { formatDate } from '../../../utils/dates'
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
      .catch((error: unknown) => showError(describeApiError(error, 'Rendement laden mislukt.').message))
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
        <h1>Rendement</h1>
        <div className="pf-filters">
          <input type="date" value={from} onChange={(event) => setFrom(event.target.value || from)} aria-label="Vanaf" />
          <input type="date" value={to} onChange={(event) => setTo(event.target.value || to)} aria-label="Tot" />
          {canExport && (
            <Button variant="secondary" disabled={busyExport} onClick={() => {
              setBusyExport(true)
              void downloadProfitabilityExport(from, to)
                .catch(() => showError('Export mislukt.'))
                .finally(() => setBusyExport(false))
            }}>
              {busyExport ? 'Exporteren…' : 'Exporteer XLSX'}
            </Button>
          )}
        </div>
      </header>

      {summary && (
        <div className="pf-kpis">
          <div className="pf-kpi"><span className="pf-kpi-value">{summary.tripCount}</span><span>Ritten</span></div>
          <div className="pf-kpi"><span className="pf-kpi-value">{formatEuro(summary.revenueUsed)}</span><span>Omzet</span></div>
          <div className="pf-kpi">
            <span className="pf-kpi-value">{formatEuro(summary.projectedCost)}</span>
            <span>Kosten (waarvan {formatEuro(summary.estimatedCost)} raming)</span>
          </div>
          <div className={`pf-kpi pf-kpi-${marginTone(summary.marginPct)}`}>
            <span className="pf-kpi-value">{formatEuro(summary.margin)}</span>
            <span>Marge{summary.marginPct !== null ? ` (${summary.marginPct}%)` : ''}</span>
          </div>
          <div className={`pf-kpi${summary.unprofitableTrips > 0 ? ' pf-kpi-danger' : ''}`}>
            <span className="pf-kpi-value">{summary.unprofitableTrips}</span><span>Verlieslatend</span>
          </div>
          <div className={`pf-kpi${summary.tripsWithMissingData > 0 ? ' pf-kpi-warning' : ''}`}>
            <span className="pf-kpi-value">{summary.tripsWithMissingData}</span><span>Onvolledige kostdata</span>
          </div>
        </div>
      )}

      <section className="pf-panel">
        <div className="pf-panel-head">
          <h2>Ranking</h2>
          <div className="pf-tabs" role="tablist">
            {(Object.keys(DIMENSION_LABELS) as ProfitabilityDimension[]).map((option) => (
              <button key={option} role="tab" aria-selected={dimension === option}
                      className={dimension === option ? 'pf-tab-active' : ''}
                      onClick={() => setDimension(option)}>
                {DIMENSION_LABELS[option]}
              </button>
            ))}
          </div>
        </div>
        <div className="pf-table-wrap">
          <table className="pf-table">
            <thead>
              <tr><th>{DIMENSION_LABELS[dimension]}</th><th>Ritten</th><th>Omzet</th><th>Kosten</th><th>Marge</th><th>%</th></tr>
            </thead>
            <tbody>
              {groups.map((group) => (
                <tr key={group.key}>
                  <td>
                    {group.label}
                    {group.containsAllocatedCosts && (
                      <span className="pf-note" title="Kosten van gedeelde ritten zijn verdeeld over klanten (allocatie)."> ⚖</span>
                    )}
                  </td>
                  <td>{group.tripCount}</td>
                  <td>{formatEuro(group.revenue)}</td>
                  <td>{formatEuro(group.projectedCost)}</td>
                  <td>{formatEuro(group.margin)}</td>
                  <td><Badge tone={marginTone(group.marginPct)}>{group.marginPct ?? '—'}%</Badge></td>
                </tr>
              ))}
              {groups.length === 0 && <tr><td colSpan={6} className="pf-note">Geen gegevens in deze periode.</td></tr>}
            </tbody>
          </table>
        </div>
      </section>

      <section className="pf-panel">
        <div className="pf-panel-head"><h2>Ritten</h2></div>
        <div className="pf-table-wrap">
          <table className="pf-table">
            <thead>
              <tr>
                <th>Rit</th><th>Datum</th><th>Klant</th><th>Chauffeur</th>
                <th>Omzet</th><th>Kosten</th><th>Marge</th><th>€/km</th><th>Datakwaliteit</th><th></th>
              </tr>
            </thead>
            <tbody>
              {(overview?.trips ?? []).map((trip) => (
                <tr key={trip.tripId}>
                  <td><strong>{trip.tripNumber}</strong>{trip.isFinalized && <span className="pf-note"> ✓ afgerond</span>}</td>
                  <td>{formatDate(trip.tripDate)}</td>
                  <td>{trip.customerSummary ?? '—'}</td>
                  <td>{trip.driverName ?? '—'}</td>
                  <td>
                    {formatEuro(trip.revenueUsed)}
                    <div className="pf-note">{REVENUE_SOURCE_LABELS[trip.revenueSourceUsed]}</div>
                  </td>
                  <td>
                    {formatEuro(trip.projectedCost)}
                    {trip.estimatedCost > 0 && trip.actualCost === 0 && <div className="pf-note">volledig raming</div>}
                    {trip.estimatedCost > 0 && trip.actualCost > 0 && <div className="pf-note">deels raming</div>}
                  </td>
                  <td><Badge tone={marginTone(trip.marginPct)}>{formatEuro(trip.margin)}{trip.marginPct !== null ? ` (${trip.marginPct}%)` : ''}</Badge></td>
                  <td>{trip.costPerKm !== null ? `${trip.costPerKm.toLocaleString('nl-BE')}${trip.distanceIsActual ? '' : '*'}` : '—'}</td>
                  <td>
                    {trip.missingCostTypes.length > 0
                      ? <Badge tone="warning">Ontbreekt: {trip.missingCostTypes.map((t) => COST_TYPE_LABELS[t] ?? t).join(', ')}</Badge>
                      : <Badge tone="success">Volledig</Badge>}
                  </td>
                  <td className="pf-actions">
                    <button type="button" className="pf-link" onClick={() => {
                      void getTripExplanation(trip.tripId)
                        .then(setExplanation)
                        .catch(() => showError('Uitleg laden mislukt.'))
                    }}>
                      Uitleg
                    </button>
                    {canSeeCostDetail && <Link className="pf-link" to={`/planning/${trip.tripId}`}>Kosten</Link>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p className="pf-note pf-legend">* geraamde afstand (geen werkelijke kilometers geregistreerd)</p>
      </section>

      {explanation && (
        <Modal title={`Berekening ${explanation.tripNumber}`} onClose={() => setExplanation(null)}>
          <h3 className="pf-exp-title">Omzet</h3>
          <ul className="pf-exp-list">
            {explanation.revenueLines.map((line, index) => (
              <li key={index}>
                <span>{line.description} <span className="pf-note">({line.source})</span></span>
                <strong>{formatEuro(line.amount)}</strong>
              </li>
            ))}
            {explanation.revenueLines.length === 0 && <li className="pf-note">Geen omzetlijnen.</li>}
          </ul>
          <h3 className="pf-exp-title">Kosten</h3>
          <ul className="pf-exp-list">
            {explanation.costLines.map((line, index) => (
              <li key={index}>
                <span>
                  {line.description}
                  <span className="pf-note">
                    {' '}({line.phase === 'Actual' ? 'werkelijk' : 'raming'} · {line.source}
                    {line.isManualOverride ? ' · overschreven' : ''})
                  </span>
                </span>
                <strong>{formatEuro(line.amount)}</strong>
              </li>
            ))}
            {explanation.costLines.length === 0 && <li className="pf-note">Nog geen kostenlijnen.</li>}
          </ul>
          {explanation.missingCostTypes.length > 0 && (
            <p className="pf-note">
              Ontbrekend: {explanation.missingCostTypes.map((t) => COST_TYPE_LABELS[t] ?? t).join(', ')}
            </p>
          )}
          <p className="pf-note">{explanation.calculationNote}</p>
        </Modal>
      )}
    </div>
  )
}
