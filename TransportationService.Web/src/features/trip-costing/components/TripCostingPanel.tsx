import { useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../../../api/apiClient'
import { localizeApiError } from '../../../api/problemDetails'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
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
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const { showSuccess, showError } = useToast()
  const canManage = hasPermission('trip_costs.manage')
  const canOverride = hasPermission('trip_costs.override')

  const [costing, setCosting] = useState<TripCosting | null>(null)
  // Vertaalsleutel in state; vertaling gebeurt pas bij render.
  const [loadErrorKey, setLoadErrorKey] = useState<string | null>(null)
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
        setLoadErrorKey(null)
      })
      .catch((err) => {
        if (!mounted) return
        setLoadErrorKey(err instanceof ApiError && err.status === 403
          ? 'tripCosting.panel.loadForbidden'
          : 'tripCosting.panel.loadFailed')
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
      showError(localizeApiError(t, err, t('tripCosting.panel.actionFailed')))
      return false
    } finally {
      setBusy(false)
    }
  }

  if (loadErrorKey) return <p className="placeholder-text">{t(loadErrorKey)}</p>
  if (!costing) return <p className="placeholder-text">{t('tripCosting.panel.loading')}</p>

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
      showError(t('tripCosting.panel.lineDialog.validation'))
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
      t('tripCosting.panel.toasts.lineAdded'),
    )
    if (ok) setLineDialogOpen(false)
  }

  async function submitOverride(event: FormEvent) {
    event.preventDefault()
    if (!overrideTarget) return
    const amount = Number(overrideAmount.replace(',', '.'))
    if (Number.isNaN(amount) || !overrideReason.trim()) {
      showError(t('tripCosting.panel.overrideDialog.validation'))
      return
    }
    const ok = await run(
      () => overrideCostLine(costing!.tripId, overrideTarget.id, amount, overrideReason.trim()),
      t('tripCosting.panel.toasts.lineOverridden'),
    )
    if (ok) setOverrideTarget(null)
  }

  async function submitActuals(event: FormEvent) {
    event.preventDefault()
    const distance = actualDistance.trim() === '' ? null : Number(actualDistance.replace(',', '.'))
    const empty = actualEmpty.trim() === '' ? null : Number(actualEmpty.replace(',', '.'))
    if ((distance !== null && Number.isNaN(distance)) || (empty !== null && Number.isNaN(empty))) {
      showError(t('tripCosting.panel.actualsDialog.validation'))
      return
    }
    const ok = await run(
      () => updateTripActuals(costing!.tripId, distance, empty),
      t('tripCosting.panel.toasts.actualsUpdated'),
    )
    if (ok) setActualsOpen(false)
  }

  return (
    <section className="pl-section tc-panel">
      <h2>{t('tripCosting.panel.title')}</h2>

      <div className="tc-totals">
        <div className="tc-total">
          <span className="tc-total-label">{t('tripCosting.phase.Estimated')}</span>
          <span className="tc-total-value">{euro(costing.estimatedTotal)}</span>
        </div>
        <div className="tc-total">
          <span className="tc-total-label">{t('tripCosting.phase.Actual')}</span>
          <span className="tc-total-value">{euro(costing.actualTotal)}</span>
        </div>
        <div className="tc-total">
          <span className="tc-total-label">{t('tripCosting.panel.totals.projected')}</span>
          <span className="tc-total-value">{euro(costing.projectedTotal)}</span>
        </div>
        <div className="tc-total">
          <span className="tc-total-label">{t('tripCosting.panel.totals.final')}</span>
          <span className="tc-total-value">
            {costing.finalCost !== null ? euro(costing.finalCost) : '—'}
          </span>
          {costing.isFinalized && <Badge tone="success">{t('tripCosting.panel.totals.finalized')}</Badge>}
        </div>
      </div>

      {profitability && (
        <div className="tc-profitability">
          <h3>{t('tripCosting.panel.profitability.title')}</h3>
          <div className="tc-profit-grid">
            <div><span>{t('tripCosting.panel.profitability.revenue')}</span><strong>{euro(profitability.revenue)}</strong></div>
            <div><span>{t('tripCosting.panel.profitability.costs')}</span><strong>{euro(profitability.cost)}</strong></div>
            <div>
              <span>{t('tripCosting.panel.profitability.profit')}</span>
              <strong className={profitability.grossProfit < 0 ? 'tc-negative' : 'tc-positive'}>
                {euro(profitability.grossProfit)}
              </strong>
            </div>
            <div><span>{t('tripCosting.panel.profitability.margin')}</span><strong>{formatMarginPct(profitability.marginPct)}</strong></div>
            <div><span>{t('tripCosting.panel.profitability.revenuePerKm')}</span><strong>{profitability.revenuePerKm !== null ? euro(profitability.revenuePerKm) : '—'}</strong></div>
            <div><span>{t('tripCosting.panel.profitability.costPerKm')}</span><strong>{profitability.costPerKm !== null ? euro(profitability.costPerKm) : '—'}</strong></div>
            <div><span>{t('tripCosting.panel.profitability.revenuePerHour')}</span><strong>{profitability.revenuePerHour !== null ? euro(profitability.revenuePerHour) : '—'}</strong></div>
            <div><span>{t('tripCosting.panel.profitability.costPerHour')}</span><strong>{profitability.costPerHour !== null ? euro(profitability.costPerHour) : '—'}</strong></div>
          </div>
          {profitability.perOrder.length > 1 && (
            <table className="tc-table">
              <thead>
                <tr>
                  <th>{t('tripCosting.panel.profitability.colOrder')}</th>
                  <th>{t('tripCosting.panel.profitability.colCustomer')}</th>
                  <th className="tc-num">{t('tripCosting.panel.profitability.colRevenue')}</th>
                  <th className="tc-num">{t('tripCosting.panel.profitability.colAllocatedCost')}</th>
                  <th className="tc-num">{t('tripCosting.panel.profitability.colProfit')}</th>
                  <th className="tc-num">{t('tripCosting.panel.profitability.colMargin')}</th>
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
        <div className="tc-phase-tabs" role="tablist" aria-label={t('tripCosting.panel.phaseTablist')}>
          {(['Estimated', 'Actual'] as TripCostPhase[]).map((phase) => (
            <button
              key={phase}
              type="button"
              role="tab"
              aria-selected={phaseTab === phase}
              className={`tc-phase-tab ${phaseTab === phase ? 'tc-phase-tab-active' : ''}`}
              onClick={() => setPhaseTab(phase)}
            >
              {t(COST_PHASE_LABELS[phase])}
            </button>
          ))}
        </div>
        <span className="tc-distances">
          {t('tripCosting.panel.distances', {
            planned: costing.plannedDistanceKm ?? '—',
            plannedEmpty: costing.plannedEmptyKm ?? 0,
            actual: costing.actualDistanceKm ?? '—',
            actualEmpty: costing.actualEmptyKm ?? '—',
          })}
        </span>
        {canManage && !costing.isFinalized && (
          <div className="tc-actions">
            {isDraftOrPlanned && (
              <Button variant="secondary" onClick={() => void run(() => recalculateEstimate(tripId), t('tripCosting.panel.toasts.estimateRecalculated'))} disabled={busy}>
                {t('tripCosting.panel.actions.recalcEstimate')}
              </Button>
            )}
            {isRunningOrDone && (
              <>
                <Button variant="secondary" onClick={() => void run(() => recalculateActual(tripId), t('tripCosting.panel.toasts.actualRecalculated'))} disabled={busy}>
                  {t('tripCosting.panel.actions.recalcActual')}
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
                  {t('tripCosting.panel.actions.actualKm')}
                </Button>
              </>
            )}
            <Button variant="secondary" onClick={openLineDialog} disabled={busy}>
              {t('tripCosting.panel.actions.addLine')}
            </Button>
            {(tripStatus === 'Completed' || tripStatus === 'Cancelled') && (
              <Button onClick={() => void run(() => finalizeTripCosting(tripId), t('tripCosting.panel.toasts.finalized'))} disabled={busy}>
                {t('tripCosting.panel.actions.finalize')}
              </Button>
            )}
          </div>
        )}
        {canOverride && costing.isFinalized && (
          <Button variant="danger" onClick={() => void run(() => reopenTripCosting(tripId), t('tripCosting.panel.toasts.reopened'))} disabled={busy}>
            {t('tripCosting.panel.actions.reopen')}
          </Button>
        )}
      </div>

      {lines.length === 0 && <p className="placeholder-text">{t(`tripCosting.panel.emptyLines.${phaseTab}`)}</p>}
      {lines.length > 0 && (
        <table className="tc-table">
          <thead>
            <tr>
              <th>{t('tripCosting.panel.table.colType')}</th>
              <th>{t('tripCosting.panel.table.colDescription')}</th>
              <th className="tc-num">{t('tripCosting.panel.table.colQuantity')}</th>
              <th>{t('tripCosting.panel.table.colUnit')}</th>
              <th className="tc-num">{t('tripCosting.panel.table.colRate')}</th>
              <th className="tc-num">{t('tripCosting.panel.table.colAmount')}</th>
              <th>{t('tripCosting.panel.table.colSource')}</th>
              {canManage && !costing.isFinalized && <th aria-label={t('tripCosting.panel.table.colActions')} />}
            </tr>
          </thead>
          <tbody>
            {lines.map((line) => (
              <tr key={line.id} className={line.isManualOverride ? 'tc-overridden' : undefined}>
                <td>{t(COST_TYPE_LABELS[line.costType])}</td>
                <td>
                  {line.description}
                  {line.isManualOverride && (
                    <span className="tc-override-note" title={line.overrideReason ?? undefined}>
                      {' '}
                      {line.overrideReason
                        ? t('tripCosting.panel.overriddenNoteReason', { reason: line.overrideReason })
                        : t('tripCosting.panel.overriddenNote')}
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
                        {t('tripCosting.panel.actions.override')}
                      </button>
                    )}
                    {line.source === 'Handmatig' && (
                      <button
                        type="button"
                        className="tc-link-button tc-danger"
                        onClick={() => void run(() => deleteCostLine(tripId, line.id), t('tripCosting.panel.toasts.lineDeleted'))}
                      >
                        {t('ui.actions.delete')}
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
          title={t('tripCosting.panel.lineDialog.title')}
          onClose={() => setLineDialogOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setLineDialogOpen(false)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="tc-line-form" disabled={busy}>
                {t('ui.actions.add')}
              </Button>
            </>
          }
        >
          <form id="tc-line-form" className="tc-form" onSubmit={submitLine} noValidate>
            <div className="tc-form-row">
              <FormField label={t('tripCosting.panel.lineDialog.phase')} htmlFor="tc-line-phase" required>
                <select id="tc-line-phase" value={linePhase} onChange={(e) => setLinePhase(e.target.value as TripCostPhase)} disabled={busy}>
                  <option value="Estimated">{t('tripCosting.phase.Estimated')}</option>
                  <option value="Actual">{t('tripCosting.phase.Actual')}</option>
                </select>
              </FormField>
              <FormField label={t('tripCosting.panel.lineDialog.type')} htmlFor="tc-line-type" required>
                <select id="tc-line-type" value={lineType} onChange={(e) => setLineType(e.target.value as TripCostType)} disabled={busy}>
                  {MANUAL_COST_TYPES.map((type) => (
                    <option key={type} value={type}>
                      {t(COST_TYPE_LABELS[type])}
                    </option>
                  ))}
                </select>
              </FormField>
            </div>
            <FormField label={t('tripCosting.panel.lineDialog.description')} htmlFor="tc-line-description" required>
              <input id="tc-line-description" value={lineDescription} onChange={(e) => setLineDescription(e.target.value)} maxLength={300} disabled={busy} />
            </FormField>
            <div className="tc-form-row">
              <FormField label={t('tripCosting.panel.lineDialog.quantity')} htmlFor="tc-line-quantity" required>
                <input id="tc-line-quantity" inputMode="decimal" value={lineQuantity} onChange={(e) => setLineQuantity(e.target.value)} disabled={busy} />
              </FormField>
              <FormField label={t('tripCosting.panel.lineDialog.unit')} htmlFor="tc-line-unit">
                <input id="tc-line-unit" value={lineUnit} onChange={(e) => setLineUnit(e.target.value)} maxLength={10} disabled={busy} />
              </FormField>
              <FormField label={t('tripCosting.panel.lineDialog.rate')} htmlFor="tc-line-rate" required hint={t('tripCosting.panel.lineDialog.rateHint')}>
                <input id="tc-line-rate" inputMode="decimal" value={lineRate} onChange={(e) => setLineRate(e.target.value)} disabled={busy} />
              </FormField>
            </div>
          </form>
        </Modal>
      )}

      {overrideTarget && (
        <Modal
          title={t('tripCosting.panel.overrideDialog.title', { type: t(COST_TYPE_LABELS[overrideTarget.costType]) })}
          onClose={() => setOverrideTarget(null)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setOverrideTarget(null)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="tc-override-form" disabled={busy}>
                {t('tripCosting.panel.actions.override')}
              </Button>
            </>
          }
        >
          <form id="tc-override-form" className="tc-form" onSubmit={submitOverride} noValidate>
            <p className="tc-override-current">
              {t('tripCosting.panel.overrideDialog.current')} <strong>{euro(overrideTarget.amount)}</strong> ({overrideTarget.description})
            </p>
            <FormField label={t('tripCosting.panel.overrideDialog.newAmount')} htmlFor="tc-override-amount" required>
              <input id="tc-override-amount" inputMode="decimal" value={overrideAmount} onChange={(e) => setOverrideAmount(e.target.value)} disabled={busy} />
            </FormField>
            <FormField label={t('tripCosting.panel.overrideDialog.reason')} htmlFor="tc-override-reason" required hint={t('tripCosting.panel.overrideDialog.reasonHint')}>
              <textarea id="tc-override-reason" rows={2} value={overrideReason} onChange={(e) => setOverrideReason(e.target.value)} maxLength={500} disabled={busy} />
            </FormField>
          </form>
        </Modal>
      )}

      {actualsOpen && (
        <Modal
          title={t('tripCosting.panel.actualsDialog.title')}
          onClose={() => setActualsOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setActualsOpen(false)} disabled={busy}>
                {t('ui.actions.cancel')}
              </Button>
              <Button type="submit" form="tc-actuals-form" disabled={busy}>
                {t('ui.actions.save')}
              </Button>
            </>
          }
        >
          <form id="tc-actuals-form" className="tc-form" onSubmit={submitActuals} noValidate>
            <div className="tc-form-row">
              <FormField label={t('tripCosting.panel.actualsDialog.distance')} htmlFor="tc-actual-distance">
                <input id="tc-actual-distance" inputMode="decimal" value={actualDistance} onChange={(e) => setActualDistance(e.target.value)} disabled={busy} />
              </FormField>
              <FormField label={t('tripCosting.panel.actualsDialog.empty')} htmlFor="tc-actual-empty">
                <input id="tc-actual-empty" inputMode="decimal" value={actualEmpty} onChange={(e) => setActualEmpty(e.target.value)} disabled={busy} />
              </FormField>
            </div>
          </form>
        </Modal>
      )}
    </section>
  )
}
