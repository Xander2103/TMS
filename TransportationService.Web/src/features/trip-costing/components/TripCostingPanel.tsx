import { useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../../../api/apiClient'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { euro } from '../../invoices/types'
import {
  addCostLine,
  deleteCostLine,
  finalizeTripCosting,
  getTripCosting,
  overrideCostLine,
  recalculateActual,
  recalculateEstimate,
  reopenTripCosting,
  updateTripActuals,
} from '../api/tripCostingApi'
import {
  COST_PHASE_LABELS,
  COST_TYPE_LABELS,
  MANUAL_COST_TYPES,
  formatMarginPct,
  type TripCostLine,
  type TripCostPhase,
  type TripCostType,
  type TripCosting,
} from '../types'
import './trip-costing.css'

interface TripCostingPanelProps {
  tripId: string
  tripStatus: string
}

/**
 * Kosten & rendement on the trip detail. Visible with trip_costs.view; mutations need
 * trip_costs.manage; overrides/reopen trip_costs.override; the profitability block only
 * arrives from the server when the caller holds profitability.view.
 */
export function TripCostingPanel({ tripId, tripStatus }: TripCostingPanelProps) {
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('trip_costs.manage')
  const canOverride = hasPermission('trip_costs.override')

  const [costing, setCosting] = useState<TripCosting | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [phaseTab, setPhaseTab] = useState<TripCostPhase>('Estimated')

  const [lineDialogOpen, setLineDialogOpen] = useState(false)
  const [lineType, setLineType] = useState<TripCostType>('Toll')
  const [linePhase, setLinePhase] = useState<TripCostPhase>('Actual')
  const [lineDescription, setLineDescription] = useState('')
  const [lineQuantity, setLineQuantity] = useState('1')
  const [lineUnit, setLineUnit] = useState('stuk')
  const [lineRate, setLineRate] = useState('')

  const [overrideTarget, setOverrideTarget] = useState<TripCostLine | null>(null)
  const [overrideAmount, setOverrideAmount] = useState('')
  const [overrideReason, setOverrideReason] = useState('')

  const [actualsOpen, setActualsOpen] = useState(false)
  const [actualDistance, setActualDistance] = useState('')
  const [actualEmpty, setActualEmpty] = useState('')

  useEffect(() => {
    let mounted = true
    getTripCosting(tripId)
      .then((data) => {
        if (!mounted) return
        setCosting(data)
        setLoadError(null)
      })
      .catch((err) => {
        if (!mounted) return
        setLoadError(err instanceof ApiError && err.status === 403
          ? 'Je hebt geen toegang tot kostengegevens.'
          : 'De kosten konden niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [tripId])

  async function run(action: () => Promise<TripCosting>, successMessage: string) {
    setBusy(true)
    try {
      setCosting(await action())
      showSuccess(successMessage)
      return true
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De bewerking is mislukt.')
      return false
    } finally {
      setBusy(false)
    }
  }

  if (loadError) return <p className="placeholder-text">{loadError}</p>
  if (!costing) return <p className="placeholder-text">Kosten laden…</p>

  const lines = costing.lines.filter((line) => line.phase === phaseTab)
  const isDraftOrPlanned = tripStatus === 'Draft' || tripStatus === 'Planned'
  const isRunningOrDone = tripStatus === 'InProgress' || tripStatus === 'Completed'
  const profitability = costing.profitability

  function openLineDialog() {
    setLinePhase(isRunningOrDone ? 'Actual' : 'Estimated')
    setLineType('Toll')
    setLineDescription('')
    setLineQuantity('1')
    setLineUnit('stuk')
    setLineRate('')
    setLineDialogOpen(true)
  }

  async function submitLine(event: FormEvent) {
    event.preventDefault()
    const quantity = Number(lineQuantity.replace(',', '.'))
    const rate = Number(lineRate.replace(',', '.'))
    if (!lineDescription.trim() || Number.isNaN(quantity) || Number.isNaN(rate)) {
      showError('Omschrijving, aantal en tarief zijn verplicht.')
      return
    }
    const ok = await run(
      () => addCostLine(costing!.tripId, {
        phase: linePhase,
        costType: lineType,
        description: lineDescription.trim(),
        quantity,
        unit: lineUnit.trim() || 'stuk',
        unitRate: rate,
      }),
      'Kostenregel toegevoegd.',
    )
    if (ok) setLineDialogOpen(false)
  }

  async function submitOverride(event: FormEvent) {
    event.preventDefault()
    if (!overrideTarget) return
    const amount = Number(overrideAmount.replace(',', '.'))
    if (Number.isNaN(amount) || !overrideReason.trim()) {
      showError('Bedrag en reden zijn verplicht.')
      return
    }
    const ok = await run(
      () => overrideCostLine(costing!.tripId, overrideTarget.id, amount, overrideReason.trim()),
      'Kostenregel overschreven.',
    )
    if (ok) setOverrideTarget(null)
  }

  async function submitActuals(event: FormEvent) {
    event.preventDefault()
    const distance = actualDistance.trim() === '' ? null : Number(actualDistance.replace(',', '.'))
    const empty = actualEmpty.trim() === '' ? null : Number(actualEmpty.replace(',', '.'))
    if ((distance !== null && Number.isNaN(distance)) || (empty !== null && Number.isNaN(empty))) {
      showError('Geef geldige kilometers op.')
      return
    }
    const ok = await run(
      () => updateTripActuals(costing!.tripId, distance, empty),
      'Werkelijke afstanden bijgewerkt.',
    )
    if (ok) setActualsOpen(false)
  }

  return (
    <section className="pl-section tc-panel">
      <h2>Kosten &amp; rendement</h2>

      <div className="tc-totals">
        <div className="tc-total">
          <span className="tc-total-label">Geschat</span>
          <span className="tc-total-value">{euro(costing.estimatedTotal)}</span>
        </div>
        <div className="tc-total">
          <span className="tc-total-label">Werkelijk</span>
          <span className="tc-total-value">{euro(costing.actualTotal)}</span>
        </div>
        <div className="tc-total">
          <span className="tc-total-label">Geprojecteerd</span>
          <span className="tc-total-value">{euro(costing.projectedTotal)}</span>
        </div>
        <div className="tc-total">
          <span className="tc-total-label">Definitief</span>
          <span className="tc-total-value">
            {costing.finalCost !== null ? euro(costing.finalCost) : '—'}
          </span>
          {costing.isFinalized && <Badge tone="success">Afgerond</Badge>}
        </div>
      </div>

      {profitability && (
        <div className="tc-profitability">
          <h3>Rendement</h3>
          <div className="tc-profit-grid">
            <div><span>Omzet</span><strong>{euro(profitability.revenue)}</strong></div>
            <div><span>Kosten</span><strong>{euro(profitability.cost)}</strong></div>
            <div>
              <span>Winst</span>
              <strong className={profitability.grossProfit < 0 ? 'tc-negative' : 'tc-positive'}>
                {euro(profitability.grossProfit)}
              </strong>
            </div>
            <div><span>Marge</span><strong>{formatMarginPct(profitability.marginPct)}</strong></div>
            <div><span>Omzet/km</span><strong>{profitability.revenuePerKm !== null ? euro(profitability.revenuePerKm) : '—'}</strong></div>
            <div><span>Kosten/km</span><strong>{profitability.costPerKm !== null ? euro(profitability.costPerKm) : '—'}</strong></div>
            <div><span>Omzet/uur</span><strong>{profitability.revenuePerHour !== null ? euro(profitability.revenuePerHour) : '—'}</strong></div>
            <div><span>Kosten/uur</span><strong>{profitability.costPerHour !== null ? euro(profitability.costPerHour) : '—'}</strong></div>
          </div>
          {profitability.perOrder.length > 1 && (
            <table className="tc-table">
              <thead>
                <tr>
                  <th>Opdracht</th>
                  <th>Klant</th>
                  <th className="tc-num">Omzet</th>
                  <th className="tc-num">Toegerekende kost</th>
                  <th className="tc-num">Winst</th>
                  <th className="tc-num">Marge</th>
                </tr>
              </thead>
              <tbody>
                {profitability.perOrder.map((order) => (
                  <tr key={order.transportOrderId}>
                    <td>{order.orderNumber}</td>
                    <td>{order.customerName}</td>
                    <td className="tc-num">{euro(order.revenue)}</td>
                    <td className="tc-num">{euro(order.allocatedCost)}</td>
                    <td className="tc-num">{euro(order.profit)}</td>
                    <td className="tc-num">{formatMarginPct(order.marginPct)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      <div className="tc-toolbar">
        <div className="tc-phase-tabs" role="tablist" aria-label="Kostenfase">
          {(['Estimated', 'Actual'] as TripCostPhase[]).map((phase) => (
            <button
              key={phase}
              type="button"
              role="tab"
              aria-selected={phaseTab === phase}
              className={`tc-phase-tab ${phaseTab === phase ? 'tc-phase-tab-active' : ''}`}
              onClick={() => setPhaseTab(phase)}
            >
              {COST_PHASE_LABELS[phase]}
            </button>
          ))}
        </div>
        <span className="tc-distances">
          Gepland: {costing.plannedDistanceKm ?? '—'} km ({costing.plannedEmptyKm ?? 0} leeg) ·
          Werkelijk: {costing.actualDistanceKm ?? '—'} km ({costing.actualEmptyKm ?? '—'} leeg)
        </span>
        {canManage && !costing.isFinalized && (
          <div className="tc-actions">
            {isDraftOrPlanned && (
              <Button variant="secondary" onClick={() => void run(() => recalculateEstimate(tripId), 'Schatting herberekend.')} disabled={busy}>
                Herbereken schatting
              </Button>
            )}
            {isRunningOrDone && (
              <>
                <Button variant="secondary" onClick={() => void run(() => recalculateActual(tripId), 'Werkelijke kosten herberekend.')} disabled={busy}>
                  Herbereken werkelijk
                </Button>
                <Button
                  variant="secondary"
                  onClick={() => {
                    setActualDistance(costing.actualDistanceKm?.toString() ?? '')
                    setActualEmpty(costing.actualEmptyKm?.toString() ?? '')
                    setActualsOpen(true)
                  }}
                  disabled={busy}
                >
                  Werkelijke km
                </Button>
              </>
            )}
            <Button variant="secondary" onClick={openLineDialog} disabled={busy}>
              + Kostenregel
            </Button>
            {(tripStatus === 'Completed' || tripStatus === 'Cancelled') && (
              <Button onClick={() => void run(() => finalizeTripCosting(tripId), 'Kosten definitief afgerond.')} disabled={busy}>
                Afronden
              </Button>
            )}
          </div>
        )}
        {canOverride && costing.isFinalized && (
          <Button variant="danger" onClick={() => void run(() => reopenTripCosting(tripId), 'Kosten heropend.')} disabled={busy}>
            Heropenen
          </Button>
        )}
      </div>

      {lines.length === 0 && <p className="placeholder-text">Geen {COST_PHASE_LABELS[phaseTab].toLowerCase()}e kostenregels.</p>}
      {lines.length > 0 && (
        <table className="tc-table">
          <thead>
            <tr>
              <th>Type</th>
              <th>Omschrijving</th>
              <th className="tc-num">Aantal</th>
              <th>Eenheid</th>
              <th className="tc-num">Tarief</th>
              <th className="tc-num">Bedrag</th>
              <th>Bron</th>
              {canManage && !costing.isFinalized && <th aria-label="Acties" />}
            </tr>
          </thead>
          <tbody>
            {lines.map((line) => (
              <tr key={line.id} className={line.isManualOverride ? 'tc-overridden' : undefined}>
                <td>{COST_TYPE_LABELS[line.costType]}</td>
                <td>
                  {line.description}
                  {line.isManualOverride && (
                    <span className="tc-override-note" title={line.overrideReason ?? undefined}>
                      {' '}
                      · overschreven{line.overrideReason ? `: ${line.overrideReason}` : ''}
                    </span>
                  )}
                </td>
                <td className="tc-num">{line.quantity.toLocaleString('nl-BE')}</td>
                <td>{line.unit}</td>
                <td className="tc-num">{euro(line.unitRate)}</td>
                <td className="tc-num">{euro(line.amount)}</td>
                <td>{line.source}</td>
                {canManage && !costing.isFinalized && (
                  <td className="tc-row-actions">
                    {canOverride && (
                      <button
                        type="button"
                        className="tc-link-button"
                        onClick={() => {
                          setOverrideTarget(line)
                          setOverrideAmount(String(line.amount))
                          setOverrideReason('')
                        }}
                      >
                        Overschrijven
                      </button>
                    )}
                    {line.source === 'Handmatig' && (
                      <button
                        type="button"
                        className="tc-link-button tc-danger"
                        onClick={() => void run(() => deleteCostLine(tripId, line.id), 'Kostenregel verwijderd.')}
                      >
                        Verwijderen
                      </button>
                    )}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {lineDialogOpen && (
        <Modal
          title="Kostenregel toevoegen"
          onClose={() => setLineDialogOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setLineDialogOpen(false)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="tc-line-form" disabled={busy}>
                Toevoegen
              </Button>
            </>
          }
        >
          <form id="tc-line-form" className="tc-form" onSubmit={submitLine} noValidate>
            <div className="tc-form-row">
              <FormField label="Fase" htmlFor="tc-line-phase" required>
                <select id="tc-line-phase" value={linePhase} onChange={(e) => setLinePhase(e.target.value as TripCostPhase)} disabled={busy}>
                  <option value="Estimated">Geschat</option>
                  <option value="Actual">Werkelijk</option>
                </select>
              </FormField>
              <FormField label="Type" htmlFor="tc-line-type" required>
                <select id="tc-line-type" value={lineType} onChange={(e) => setLineType(e.target.value as TripCostType)} disabled={busy}>
                  {MANUAL_COST_TYPES.map((type) => (
                    <option key={type} value={type}>
                      {COST_TYPE_LABELS[type]}
                    </option>
                  ))}
                </select>
              </FormField>
            </div>
            <FormField label="Omschrijving" htmlFor="tc-line-description" required>
              <input id="tc-line-description" value={lineDescription} onChange={(e) => setLineDescription(e.target.value)} maxLength={300} disabled={busy} />
            </FormField>
            <div className="tc-form-row">
              <FormField label="Aantal" htmlFor="tc-line-quantity" required>
                <input id="tc-line-quantity" inputMode="decimal" value={lineQuantity} onChange={(e) => setLineQuantity(e.target.value)} disabled={busy} />
              </FormField>
              <FormField label="Eenheid" htmlFor="tc-line-unit">
                <input id="tc-line-unit" value={lineUnit} onChange={(e) => setLineUnit(e.target.value)} maxLength={10} disabled={busy} />
              </FormField>
              <FormField label="Tarief (€)" htmlFor="tc-line-rate" required hint="Negatief alleen bij correctie/credit.">
                <input id="tc-line-rate" inputMode="decimal" value={lineRate} onChange={(e) => setLineRate(e.target.value)} disabled={busy} />
              </FormField>
            </div>
          </form>
        </Modal>
      )}

      {overrideTarget && (
        <Modal
          title={`Regel overschrijven — ${COST_TYPE_LABELS[overrideTarget.costType]}`}
          onClose={() => setOverrideTarget(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setOverrideTarget(null)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="tc-override-form" disabled={busy}>
                Overschrijven
              </Button>
            </>
          }
        >
          <form id="tc-override-form" className="tc-form" onSubmit={submitOverride} noValidate>
            <p className="tc-override-current">
              Huidig bedrag: <strong>{euro(overrideTarget.amount)}</strong> ({overrideTarget.description})
            </p>
            <FormField label="Nieuw bedrag (€)" htmlFor="tc-override-amount" required>
              <input id="tc-override-amount" inputMode="decimal" value={overrideAmount} onChange={(e) => setOverrideAmount(e.target.value)} disabled={busy} />
            </FormField>
            <FormField label="Reden" htmlFor="tc-override-reason" required hint="Verplicht; wordt gelogd in de historiek.">
              <textarea id="tc-override-reason" rows={2} value={overrideReason} onChange={(e) => setOverrideReason(e.target.value)} maxLength={500} disabled={busy} />
            </FormField>
          </form>
        </Modal>
      )}

      {actualsOpen && (
        <Modal
          title="Werkelijke afstanden"
          onClose={() => setActualsOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setActualsOpen(false)} disabled={busy}>
                Annuleren
              </Button>
              <Button type="submit" form="tc-actuals-form" disabled={busy}>
                Opslaan
              </Button>
            </>
          }
        >
          <form id="tc-actuals-form" className="tc-form" onSubmit={submitActuals} noValidate>
            <div className="tc-form-row">
              <FormField label="Werkelijke afstand (km)" htmlFor="tc-actual-distance">
                <input id="tc-actual-distance" inputMode="decimal" value={actualDistance} onChange={(e) => setActualDistance(e.target.value)} disabled={busy} />
              </FormField>
              <FormField label="Waarvan leeg (km)" htmlFor="tc-actual-empty">
                <input id="tc-actual-empty" inputMode="decimal" value={actualEmpty} onChange={(e) => setActualEmpty(e.target.value)} disabled={busy} />
              </FormField>
            </div>
          </form>
        </Modal>
      )}
    </section>
  )
}
