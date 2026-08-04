import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { addRecentItem } from '../../../hooks/recentItems'
import { PageHeader } from '../../../components/layout/PageHeader'
import { Breadcrumbs } from '../../../components/layout/Breadcrumbs'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { BackButton } from '../../../components/ui/BackButton'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import { ApiError } from '../../../api/apiClient'
import {
  cancelTransportOrder,
  changeTransportOrderStatus,
  confirmOrderPriceLine,
  correctTransportOrderStatus,
  deleteTransportOrder,
  getTransportOrder,
  recalculateOrderPricing,
  saveOrderPriceLines,
  setOrderPricingStatus,
  updateTransportOrder,
  type SaveOrderPriceLineInput,
} from '../api/transportOrdersApi'
import { TransportOrderForm } from '../components/TransportOrderForm'
import { UnitSelect } from '../components/UnitSelect'
import { OrderDocumentsPanel } from '../components/OrderDocumentsPanel'
import { OrderPortalReviewPanel } from '../components/OrderPortalReviewPanel'
import { OrderTimelinePanel } from '../components/OrderTimelinePanel'
import { CustomerMessagesPanel } from '../../customers/components/CustomerMessagesPanel'
import { StopExecutionPlanDialog } from '../components/StopExecutionPlanDialog'
import { OrderPackagesPanel } from '../../packages/components/OrderPackagesPanel'
import { UNIT_TYPE_LABELS } from '../../packages/types'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { CustomerPackagesSummary } from '../../packages/components/CustomerPackagesSummary'
import {
  ORDER_PRICE_LINE_KIND_TONE,
  ORDER_PRICING_STATUS_LABELS,
  ORDER_PRICING_STATUS_TONE,
  ORDER_STATUS_LABELS,
  ORDER_STATUS_TONE,
  ORDER_TRANSITION_LABELS,
  PRICING_COVERAGE_LABELS,
  PRICING_COVERAGE_TONE,
  STOP_TYPE_LABELS,
  lineBadge,
  type OrderPricingLine,
  type OrderPricingStatus,
  type TransportOrderDetail,
  type TransportOrderStatus,
  type TransportOrderStop,
} from '../types'
import './transport-orders.css'

function formatWindow(from: string | null, to: string | null): string {
  const fmt = (value: string) => value.slice(0, 16).replace('T', ' ')
  if (from && to) return `${fmt(from)} – ${fmt(to)}`
  if (from) return `vanaf ${fmt(from)}`
  if (to) return `tot ${fmt(to)}`
  return '—'
}

function money(amount: number): string {
  return amount.toLocaleString('nl-BE', { style: 'currency', currency: 'EUR' })
}

/**
 * Label for the Berekening column: quantity x unit x unit price when known AND consistent with
 * the shown Bedrag, else a flat-amount or unknown fallback. An amount-only adjustment (e.g.
 * Aantal cleared, Bedrag typed directly) legitimately leaves a stale quantity/unitPrice on the
 * stored line — showing the stale formula next to the real amount would contradict it, so the
 * formula is only rendered when it actually reproduces the amount (rounding-tolerant).
 */
function calculationLabel(line: OrderPricingLine): string {
  if (line.quantity != null && line.unitPrice != null) {
    const computed = Math.round(line.quantity * line.unitPrice * 100) / 100
    if (computed === line.amount) {
      const unit = line.unit ? ` ${line.unit}` : ''
      return `${line.quantity.toLocaleString('nl-BE')}${unit} × ${money(line.unitPrice)}`
    }
  }
  return line.kind === 'Manual' ? 'Vast bedrag' : '—'
}

export function TransportOrderDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showSuccess, showError } = useToast()
  const { hasPermission, hasAnyPermission } = useAuth()
  const { options: unitTypes } = useLookupOptions('/api/unit-types')

  // Managed unit-type name when a code is set, else the preserved legacy free-text value.
  const unitLabel = (code: string | null, legacy: string | null): string =>
    (code ? unitTypes.find((u) => u.code === code)?.name ?? code : null) ?? legacy ?? ''

  // Commercial "Lading" summary: cargo lines grouped by unit, quantities summed per group —
  // distinct from the scanable colli generated per line on confirmation.
  const aggregateCargo = (items: TransportOrderDetail['cargoItems']): { unit: string; total: number }[] => {
    const totals = new Map<string, number>()
    for (const item of items) {
      const unit = unitLabel(item.quantityUnitCode, item.quantityUnit) || 'stuks'
      totals.set(unit, (totals.get(unit) ?? 0) + item.expectedQuantity)
    }
    return Array.from(totals, ([unit, total]) => ({ unit, total }))
  }

  const [order, setOrder] = useState<TransportOrderDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  const [busy, setBusy] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [cancelDialogOpen, setCancelDialogOpen] = useState(false)
  const [cancelReason, setCancelReason] = useState('')
  const [correctDialogOpen, setCorrectDialogOpen] = useState(false)
  const [correctTarget, setCorrectTarget] = useState<TransportOrderStatus | ''>('')
  const [correctReason, setCorrectReason] = useState('')
  const [planStop, setPlanStop] = useState<TransportOrderStop | null>(null)

  // --- Pricing lines / status (spec ch. 24-26) --------------------------------------------
  const [editLine, setEditLine] = useState<OrderPricingLine | null>(null)
  const [editQuantity, setEditQuantity] = useState('')
  const [editUnitPrice, setEditUnitPrice] = useState('')
  const [editAmount, setEditAmount] = useState('')
  const [editReason, setEditReason] = useState('')
  const [removeLine, setRemoveLine] = useState<OrderPricingLine | null>(null)
  const [removeReason, setRemoveReason] = useState('')
  const [addLineOpen, setAddLineOpen] = useState(false)
  const [addMode, setAddMode] = useState<'perUnit' | 'fixed'>('perUnit')
  const [addLabel, setAddLabel] = useState('')
  const [addQuantity, setAddQuantity] = useState('')
  const [addUnit, setAddUnit] = useState<string | null>(null)
  const [addUnitPrice, setAddUnitPrice] = useState('')
  const [addAmount, setAddAmount] = useState('')
  const [addReason, setAddReason] = useState('')
  const [calcDetailsOpen, setCalcDetailsOpen] = useState(false)
  const [recalcConfirmOpen, setRecalcConfirmOpen] = useState(false)
  const [pricingBusy, setPricingBusy] = useState(false)

  useEffect(() => {
    let mounted = true
    getTransportOrder(id)
      .then((data) => {
        if (!mounted) return
        setOrder(data)
        setLoadError(null)
        addRecentItem({
          category: 'Transportopdrachten',
          title: `${data.orderNumber} — ${data.customerName}`,
          route: `/transport-orders/${data.id}`,
        })
      })
      .catch(() => {
        if (mounted) setLoadError('De transportopdracht kon niet worden geladen.')
      })
    return () => {
      mounted = false
    }
  }, [id])

  async function applyTransition(target: TransportOrderStatus) {
    setBusy(true)
    try {
      const updated = await changeTransportOrderStatus(id, target)
      setOrder(updated)
      showSuccess(`Status gewijzigd naar ${ORDER_STATUS_LABELS[target]}.`)
    } catch {
      showError('De status kon niet worden gewijzigd.')
    } finally {
      setBusy(false)
    }
  }

  async function handleCancel() {
    if (!cancelReason.trim()) {
      showError('Een reden is verplicht bij het annuleren.')
      return
    }
    setBusy(true)
    try {
      const updated = await cancelTransportOrder(id, cancelReason.trim())
      setOrder(updated)
      setCancelDialogOpen(false)
      setCancelReason('')
      showSuccess('Opdracht geannuleerd.')
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De opdracht kon niet worden geannuleerd.')
    } finally {
      setBusy(false)
    }
  }

  async function handleCorrectStatus() {
    if (!correctTarget) {
      showError('Kies de status waarnaar je wilt corrigeren.')
      return
    }
    if (!correctReason.trim()) {
      showError('Een reden is verplicht bij een statuscorrectie.')
      return
    }
    setBusy(true)
    try {
      const updated = await correctTransportOrderStatus(id, correctTarget, correctReason.trim())
      setOrder(updated)
      setCorrectDialogOpen(false)
      setCorrectTarget('')
      setCorrectReason('')
      showSuccess(`Status gecorrigeerd naar ${ORDER_STATUS_LABELS[updated.status]}.`)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De status kon niet worden gecorrigeerd.')
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete() {
    try {
      await deleteTransportOrder(id)
      showSuccess('Opdracht verwijderd.')
      navigate('/transport-orders')
    } catch {
      showError('De opdracht kon niet worden verwijderd.')
      setConfirmDelete(false)
    }
  }

  function parseNum(value: string): number | null {
    if (!value.trim()) return null
    const n = Number(value)
    return Number.isFinite(n) ? n : null
  }

  function round2(n: number): number {
    return Math.round(n * 100) / 100
  }

  function resetAddLine() {
    setAddMode('perUnit')
    setAddLabel('')
    setAddQuantity('')
    setAddUnit(null)
    setAddUnitPrice('')
    setAddAmount('')
    setAddReason('')
  }

  function openAddLine() {
    resetAddLine()
    setAddLineOpen(true)
  }

  function closeAddLine() {
    setAddLineOpen(false)
    resetAddLine()
  }

  async function submitPriceLine(payload: SaveOrderPriceLineInput) {
    if (!order) return
    setPricingBusy(true)
    try {
      const updated = await saveOrderPriceLines(order.id, [payload])
      setOrder(updated)
      showSuccess('Prijsregel bijgewerkt.')
      setEditLine(null)
      setRemoveLine(null)
      setRemoveReason('')
      closeAddLine()
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De prijsregel kon niet worden opgeslagen.')
    } finally {
      setPricingBusy(false)
    }
  }

  function openEditLine(line: OrderPricingLine) {
    setEditLine(line)
    setEditQuantity(line.quantity != null ? String(line.quantity) : '')
    setEditUnitPrice(line.unitPrice != null ? String(line.unitPrice) : '')
    setEditAmount('')
    setEditReason('')
  }

  async function handleSaveEditLine() {
    if (!editLine) return
    if (!editReason.trim()) {
      showError('Een reden is verplicht bij het aanpassen van een prijsregel.')
      return
    }
    const quantity = parseNum(editQuantity)
    const unitPrice = parseNum(editUnitPrice)
    const computedAmount = quantity !== null && unitPrice !== null ? round2(quantity * unitPrice) : null
    await submitPriceLine({
      lineKey: editLine.lineKey ?? null,
      label: editLine.label,
      quantity,
      unitPrice,
      amount: computedAmount !== null ? null : parseNum(editAmount),
      adjustReason: editReason.trim(),
    })
  }

  async function handleRemoveLine() {
    if (!removeLine) return
    if (removeLine.kind !== 'Manual' && !removeReason.trim()) {
      showError('Een reden is verplicht bij het verwijderen van een berekende prijsregel.')
      return
    }
    await submitPriceLine({
      lineKey: removeLine.lineKey ?? null,
      label: removeLine.label,
      quantity: null,
      unitPrice: null,
      amount: null,
      adjustReason: removeReason.trim() || null,
      remove: true,
    })
  }

  async function handleAddLine() {
    if (!addLabel.trim()) {
      showError('Een omschrijving is verplicht voor een vrije regel.')
      return
    }
    if (addMode === 'perUnit') {
      const quantity = parseNum(addQuantity)
      const unitPrice = parseNum(addUnitPrice)
      if (quantity === null || quantity <= 0 || unitPrice === null || unitPrice < 0) {
        showError('Geef een aantal en eenheidsprijs op.')
        return
      }
      await submitPriceLine({
        lineKey: null,
        label: addLabel.trim(),
        quantity,
        unitPrice,
        unit: addUnit,
        amount: null,
        adjustReason: addReason.trim() || null,
      })
    } else {
      const amount = parseNum(addAmount)
      if (amount === null) {
        showError('Geef een totaalbedrag op.')
        return
      }
      await submitPriceLine({
        lineKey: null,
        label: addLabel.trim(),
        quantity: null,
        unitPrice: null,
        unit: null,
        amount,
        adjustReason: addReason.trim() || null,
      })
    }
  }

  async function handleConfirmLine(line: OrderPricingLine) {
    if (!order || !line.id) return
    setPricingBusy(true)
    try {
      const updated = await confirmOrderPriceLine(order.id, line.id)
      setOrder(updated)
      showSuccess('Voorstel bevestigd.')
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'Het voorstel kon niet worden bevestigd.')
    } finally {
      setPricingBusy(false)
    }
  }

  async function handleRecalculate() {
    if (!order) return
    setPricingBusy(true)
    try {
      const updated = await recalculateOrderPricing(order.id)
      setOrder(updated)
      showSuccess('Prijs herberekend.')
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De prijs kon niet worden herberekend.')
    } finally {
      setPricingBusy(false)
      setRecalcConfirmOpen(false)
    }
  }

  function handleRecalculateClick() {
    if (order?.pricingSnapshot?.status === 'Reviewed') {
      setRecalcConfirmOpen(true)
    } else {
      void handleRecalculate()
    }
  }

  async function handleSetPricingStatus(status: OrderPricingStatus) {
    if (!order) return
    setPricingBusy(true)
    try {
      const updated = await setOrderPricingStatus(order.id, status)
      setOrder(updated)
      showSuccess(`Prijsstatus gewijzigd naar ${ORDER_PRICING_STATUS_LABELS[status]}.`)
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De prijsstatus kon niet worden gewijzigd.')
    } finally {
      setPricingBusy(false)
    }
  }

  if (loadError) return <ErrorState message={loadError} />
  if (!order) return <LoadingState message="Opdracht laden..." />

  const editable =
    (order.status === 'Draft' || order.status === 'Confirmed') && hasPermission('orders.edit')
  const deletable =
    (order.status === 'Draft' || order.status === 'Cancelled') && hasPermission('orders.delete')
  const planEditable =
    !['Completed', 'Invoiced', 'Cancelled'].includes(order.status) &&
    hasAnyPermission(['orders.edit', 'orders.manage'])
  const canEditPricingLines = hasAnyPermission(['orders.override_price', 'orders.manage'])
  const canEditPricingStatus = hasAnyPermission(['orders.edit', 'orders.manage'])
  const canLockPrice = hasAnyPermission(['orders.lock_price', 'orders.manage'])
  const pricingStatus = order.pricingSnapshot?.status ?? 'Draft'
  const pricingLocked = pricingStatus === 'Locked' || pricingStatus === 'Invoiced'
  // Informational lines with a non-zero amount (e.g. the diesel surcharge) still carry a real
  // amount that will be applied at invoicing — they stay as a (dimmed) table row. Only
  // zero-amount informational lines (e.g. "no matching rule") are pure notices under "Niet toegepast".
  const invoiceLines = (order.pricingLines ?? []).filter((l) => !l.informational || l.amount !== 0)
  const notAppliedLines = (order.pricingLines ?? []).filter((l) => l.informational && l.amount === 0)
  // Wave 2026-08-04 §7: per-goods-line pricing coverage frozen on the snapshot.
  const coverage = order.pricingSnapshot?.coverage ?? []
  const unpricedCoverage = coverage.filter((c) => c.status !== 'Full')

  const addQuantityNum = parseNum(addQuantity)
  const addUnitPriceNum = parseNum(addUnitPrice)
  const computedAddTotal =
    addMode === 'perUnit' && addQuantityNum !== null && addUnitPriceNum !== null
      ? round2(addQuantityNum * addUnitPriceNum)
      : null
  const computedAddTotalDisplay =
    computedAddTotal !== null ? computedAddTotal.toLocaleString('nl-BE', { style: 'currency', currency: 'EUR' }) : '—'

  const editQuantityNum = parseNum(editQuantity)
  const editUnitPriceNum = parseNum(editUnitPrice)
  const computedEditAmount =
    editQuantityNum !== null && editUnitPriceNum !== null ? round2(editQuantityNum * editUnitPriceNum) : null
  const editAmountIsComputed = computedEditAmount !== null
  const computedEditAmountDisplay =
    computedEditAmount !== null ? computedEditAmount.toLocaleString('nl-BE', { style: 'currency', currency: 'EUR' }) : '—'

  return (
    <div>
      <Breadcrumbs items={[{ label: 'Transportopdrachten', to: '/transport-orders' }, { label: order.orderNumber }]} />
      <BackButton to="/transport-orders" label="Terug naar opdrachten" />
      <PageHeader
        title={`${order.orderNumber} — ${order.customerName}`}
        subtitle={`Opdracht van ${order.orderDate}${order.customerReference ? ` · ref. ${order.customerReference}` : ''}`}
        action={
          <span className="to-header-actions">
            <Badge tone={ORDER_STATUS_TONE[order.status]}>{ORDER_STATUS_LABELS[order.status]}</Badge>
            {editable && !editing && (
              <Button variant="secondary" onClick={() => setEditing(true)} disabled={busy}>
                Bewerken
              </Button>
            )}
            {deletable && (
              <Button variant="danger" onClick={() => setConfirmDelete(true)} disabled={busy || editing}>
                Verwijderen
              </Button>
            )}
            {hasAnyPermission(['orders.change_status', 'orders.manage']) &&
              order.allowedTransitions.map((target) => (
                <Button key={target} onClick={() => void applyTransition(target)} disabled={busy || editing}>
                  {ORDER_TRANSITION_LABELS[target]}
                </Button>
              ))}
            {order.canCancel && hasAnyPermission(['orders.cancel', 'orders.manage']) && (
              <Button variant="secondary" onClick={() => setCancelDialogOpen(true)} disabled={busy || editing}>
                Annuleren
              </Button>
            )}
            {order.allowedCorrections.length > 0 && hasAnyPermission(['orders.correct_status', 'orders.manage']) && (
              <Button variant="secondary" onClick={() => setCorrectDialogOpen(true)} disabled={busy || editing}>
                Status corrigeren
              </Button>
            )}
            {hasAnyPermission(['orders.create', 'orders.manage']) && (
              <Button
                variant="secondary"
                onClick={() => navigate(`/transport-orders/new?template=${order.id}`)}
                disabled={busy || editing}
              >
                Gebruik als sjabloon
              </Button>
            )}
          </span>
        }
      />

      {order.status === 'Cancelled' && order.cancellationReason && (
        <p className="to-cancel-reason" role="note">
          Geannuleerd: {order.cancellationReason}
        </p>
      )}

      {!editing && hasAnyPermission(['orders.change_status', 'orders.manage']) && (
        <OrderPortalReviewPanel order={order} onReviewed={setOrder} />
      )}

      {editing ? (
        <TransportOrderForm
          order={order}
          submitLabel="Wijzigingen opslaan"
          onCancel={() => setEditing(false)}
          documentsSection={<OrderDocumentsPanel orderId={order.id} />}
          documentsSectionIsPanel
          onSubmit={async (input) => {
            const updated = await updateTransportOrder(id, input)
            setOrder(updated)
            setEditing(false)
            showSuccess('Opdracht bijgewerkt.')
          }}
        />
      ) : (
        <>
          <section className="to-section">
            <h2>Lading</h2>
            <dl className="to-facts">
              <div>
                <dt>Goederen</dt>
                <dd>{order.goodsDescription ?? '—'}</dd>
              </div>
              {order.cargoItems.length > 0 ? (
                <div>
                  <dt>Lading</dt>
                  <dd>
                    <ul className="to-lading-list">
                      {aggregateCargo(order.cargoItems).map(({ unit, total }) => (
                        <li key={unit}>{total.toLocaleString('nl-BE')} {unit}</li>
                      ))}
                    </ul>
                  </dd>
                </div>
              ) : (
                <div>
                  <dt>Aantal</dt>
                  <dd>{order.quantity !== null ? `${order.quantity} ${unitLabel(order.quantityUnitCode, order.quantityUnit)}`.trim() : '—'}</dd>
                </div>
              )}
              <div>
                <dt>Gewicht</dt>
                <dd>{order.weightKg !== null ? `${order.weightKg.toLocaleString('nl-BE')} kg` : '—'}</dd>
              </div>
              <div>
                <dt>Volume</dt>
                <dd>{order.volumeM3 !== null ? `${order.volumeM3.toLocaleString('nl-BE')} m³` : '—'}</dd>
              </div>
              <div>
                <dt>Paletten</dt>
                <dd>{order.palletCount ?? '—'}</dd>
              </div>
              <div>
                <dt>Prijs</dt>
                <dd>
                  {order.agreedPrice !== null
                    ? order.agreedPrice.toLocaleString('nl-BE', { style: 'currency', currency: 'EUR' })
                    : '—'}
                </dd>
              </div>
              <div>
                <dt>Kenmerken</dt>
                <dd>
                  {order.adrRequired && <Badge tone="danger">ADR</Badge>}
                  {order.craneRequired && <Badge tone="info">Kraan</Badge>}
                  {!order.adrRequired && !order.craneRequired && '—'}
                </dd>
              </div>
            </dl>
            {order.notes && <p className="to-notes">{order.notes}</p>}
          </section>

          {order.pricingLines && order.pricingLines.length > 0 && (
            <section className="to-section">
              <h2>
                Prijs{' '}
                <Badge tone={ORDER_PRICING_STATUS_TONE[pricingStatus]}>{ORDER_PRICING_STATUS_LABELS[pricingStatus]}</Badge>
              </h2>
              <div className="to-header-actions to-price-status-actions">
                {canEditPricingStatus && pricingStatus === 'Draft' && (
                  <Button variant="secondary" onClick={() => void handleSetPricingStatus('Reviewed')} disabled={pricingBusy}>
                    Markeer gecontroleerd
                  </Button>
                )}
                {canEditPricingStatus && pricingStatus === 'Reviewed' && (
                  <Button variant="secondary" onClick={() => void handleSetPricingStatus('Draft')} disabled={pricingBusy}>
                    Terug naar concept
                  </Button>
                )}
                {canLockPrice && (pricingStatus === 'Draft' || pricingStatus === 'Reviewed') && (
                  <Button variant="secondary" onClick={() => void handleSetPricingStatus('Locked')} disabled={pricingBusy}>
                    Vergrendel prijs
                  </Button>
                )}
                {canLockPrice && pricingStatus === 'Locked' && (
                  <Button variant="secondary" onClick={() => void handleSetPricingStatus('Reviewed')} disabled={pricingBusy}>
                    Ontgrendel
                  </Button>
                )}
                {canEditPricingStatus && !pricingLocked && (
                  <Button variant="secondary" onClick={handleRecalculateClick} disabled={pricingBusy}>
                    Herberekenen
                  </Button>
                )}
                {canEditPricingLines && !pricingLocked && (
                  <Button variant="secondary" onClick={openAddLine} disabled={pricingBusy}>
                    + Vrije regel
                  </Button>
                )}
                {order.pricingSnapshot && (
                  <Button variant="ghost" onClick={() => setCalcDetailsOpen(true)}>
                    Bekijk berekeningsdetails
                  </Button>
                )}
              </div>
              {unpricedCoverage.length > 0 && (
                <div className="to-coverage-warning" role="alert">
                  <strong>Niet alle goederen zijn geprijsd.</strong>
                  <ul>
                    {unpricedCoverage.map((c, index) => (
                      <li key={c.unitTypeId ?? `${c.unitLabel}-${index}`}>
                        {c.quantity.toLocaleString('nl-BE')} {c.unitLabel}:{' '}
                        {(c.reason ?? 'geen passend basistarief').toLowerCase()}
                        {c.servicesAmount > 0 && ` — alleen diensten (€ ${c.servicesAmount.toFixed(2)}), geen transportprijs`}
                      </li>
                    ))}
                  </ul>
                </div>
              )}
              {coverage.length > 0 && (
                <div className="to-coverage">
                  <h3>Prijsdekking per goederenlijn</h3>
                  <ul>
                    {coverage.map((c, index) => (
                      <li key={c.unitTypeId ?? `${c.unitLabel}-${index}`}>
                        <Badge tone={PRICING_COVERAGE_TONE[c.status]}>{PRICING_COVERAGE_LABELS[c.status]}</Badge>{' '}
                        {c.quantity.toLocaleString('nl-BE')} {c.unitLabel}
                        {c.status === 'Full' && ` — ${c.baseRuleName ?? 'basistarief'}: € ${c.baseAmount.toFixed(2)}`}
                        {c.status !== 'Full' && c.reason ? ` — ${c.reason}` : ''}
                        {c.servicesAmount > 0 && ` · diensten € ${c.servicesAmount.toFixed(2)}`}
                      </li>
                    ))}
                  </ul>
                </div>
              )}
              <table className="to-stops-table">
                <thead>
                  <tr>
                    <th>Omschrijving</th>
                    <th>Type</th>
                    <th>Berekening</th>
                    <th className="tof-price-amount">Bedrag</th>
                    {canEditPricingLines && !pricingLocked && <th>Acties</th>}
                  </tr>
                </thead>
                <tbody>
                  {invoiceLines.map((line, index) => (
                    <tr
                      key={line.id ?? line.lineKey ?? index}
                      className={line.informational ? 'tof-price-informational' : undefined}
                    >
                      <td>
                        {line.label}
                        {line.kind === 'AutoAdjusted' && (
                          <div className="to-price-original">
                            <s>
                              {line.originalQuantity != null && line.originalUnitPrice != null
                                ? `${line.originalQuantity} × € ${line.originalUnitPrice.toFixed(2)}`
                                : `€ ${(line.originalAmount ?? 0).toFixed(2)}`}
                            </s>
                            {' → '}
                            {line.quantity != null && line.unitPrice != null
                              ? `${line.quantity} × € ${line.unitPrice.toFixed(2)}`
                              : `€ ${line.amount.toFixed(2)}`}
                          </div>
                        )}
                      </td>
                      <td>
                        <Badge tone={ORDER_PRICE_LINE_KIND_TONE[line.kind]}>{lineBadge(line)}</Badge>
                      </td>
                      <td>{calculationLabel(line)}</td>
                      <td className="tof-price-amount">€ {line.amount.toFixed(2)}</td>
                      {canEditPricingLines && !pricingLocked && (
                        <td className="to-price-line-actions">
                          {line.kind === 'Proposed' ? (
                            <Button variant="ghost" onClick={() => void handleConfirmLine(line)} disabled={pricingBusy}>
                              Bevestigen
                            </Button>
                          ) : (
                            <>
                              <Button variant="ghost" onClick={() => openEditLine(line)} disabled={pricingBusy}>
                                Bewerken
                              </Button>
                              <Button variant="ghost" onClick={() => setRemoveLine(line)} disabled={pricingBusy}>
                                Verwijderen
                              </Button>
                            </>
                          )}
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr>
                    <th>Totaal</th>
                    <th />
                    <th />
                    <th className="tof-price-amount">
                      € {(order.pricingSnapshot?.linesTotal ?? order.agreedPrice ?? 0).toFixed(2)}
                    </th>
                    {canEditPricingLines && !pricingLocked && <th />}
                  </tr>
                </tfoot>
              </table>
              {notAppliedLines.length > 0 && (
                <div className="to-price-not-applied">
                  <h3>Niet toegepast</h3>
                  <ul>
                    {notAppliedLines.map((line, index) => (
                      <li key={line.id ?? line.lineKey ?? index}>{line.label}</li>
                    ))}
                  </ul>
                </div>
              )}
            </section>
          )}

          <section className="to-section">
            <h2>Stops</h2>
            <table className="to-stops-table">
              <thead>
                <tr>
                  <th>#</th>
                  <th>Type</th>
                  <th>Locatie</th>
                  <th>Adres</th>
                  <th>Gepland</th>
                  <th>Gevraagd</th>
                  <th>Bevestigd</th>
                  <th>Afspraak</th>
                  <th>Referentie</th>
                  {planEditable && <th aria-label="Acties" />}
                </tr>
              </thead>
              <tbody>
                {order.stops.map((stop) => (
                  <tr key={stop.id}>
                    <td>{stop.sequence}</td>
                    <td>
                      <Badge tone={stop.stopType === 'Loading' ? 'info' : 'success'}>{STOP_TYPE_LABELS[stop.stopType]}</Badge>
                    </td>
                    <td title={stop.instructions ?? undefined}>
                      {stop.locationName}
                      {stop.locationCode && <span className="to-loc-code"> ({stop.locationCode})</span>}
                    </td>
                    <td>{[stop.address, [stop.postalCode, stop.city].filter(Boolean).join(' ')].filter(Boolean).join(', ') || '—'}</td>
                    <td>{formatWindow(stop.plannedFrom, stop.plannedTo)}</td>
                    <td>{formatWindow(stop.requestedFrom, stop.requestedTo)}</td>
                    <td>{formatWindow(stop.confirmedFrom, stop.confirmedTo)}</td>
                    <td>
                      {stop.appointmentRequired ? (
                        <Badge tone="warning">Afspraak{stop.appointmentReference ? ` · ${stop.appointmentReference}` : ''}</Badge>
                      ) : (
                        '—'
                      )}
                    </td>
                    <td>{stop.reference ?? '—'}</td>
                    {planEditable && (
                      <td>
                        <Button variant="ghost" onClick={() => setPlanStop(stop)} disabled={busy || editing}>
                          Venster
                        </Button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </section>

          {hasPermission('packages.view') ? (
            <OrderPackagesPanel
              orderId={order.id}
              unloadingStops={order.stops
                .filter((stop) => stop.stopType === 'Unloading')
                .map((stop) => ({ id: stop.id, label: `${stop.sequence}. ${stop.city ?? stop.locationName ?? 'Losstop'}` }))}
            />
          ) : (
            <CustomerPackagesSummary orderId={order.id} />
          )}

          {order.cargoItems.length > 0 && (
            <section className="to-section">
              <h2>Goederenlijnen</h2>
              <table className="to-stops-table">
                <thead>
                  <tr>
                    <th>#</th>
                    <th>Omschrijving</th>
                    <th>Type</th>
                    <th>Barcode</th>
                    <th>Verwacht</th>
                    <th>Gewicht</th>
                    <th>Volume/stuk</th>
                    <th>Kenmerken</th>
                    <th>Notities</th>
                  </tr>
                </thead>
                <tbody>
                  {order.cargoItems.map((item) => (
                    <tr key={item.id}>
                      <td>{item.sequence}</td>
                      <td>{item.description ?? `${item.expectedQuantity} × ${unitLabel(item.quantityUnitCode, item.quantityUnit)}`}</td>
                      <td>{item.unitType ? (item.unitTypeLabel ?? UNIT_TYPE_LABELS[item.unitType]) : '—'}</td>
                      <td>{item.barcode ? <code>{item.barcode}</code> : '—'}</td>
                      <td>
                        {item.expectedQuantity} {unitLabel(item.quantityUnitCode, item.quantityUnit)}
                      </td>
                      <td>{item.totalWeightKg !== null ? `${item.totalWeightKg.toLocaleString('nl-BE')} kg` : '—'}</td>
                      <td>{item.volumeM3 !== null ? `${item.volumeM3.toLocaleString('nl-BE')} m³` : '—'}</td>
                      <td>
                        {item.adrRequired && <Badge tone="danger">ADR</Badge>}{' '}
                        {!item.stackable && <Badge tone="warning">Niet stapelbaar</Badge>}
                      </td>
                      <td>{item.notes ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </section>
          )}

          <section className="to-section">
            <h2>Historiek</h2>
            <OrderTimelinePanel orderId={order.id} />
          </section>

          {hasPermission('customer_messages.view') && (
            <section className="to-section">
              <h2>Berichten (klantportaal)</h2>
              <CustomerMessagesPanel customerId={order.customerId} orderId={order.id} />
            </section>
          )}

          <div className="to-detail-actions">
            {editable && (
              <Button variant="secondary" onClick={() => setEditing(true)} disabled={busy}>
                Bewerken
              </Button>
            )}
            {deletable && (
              <Button variant="secondary" onClick={() => setConfirmDelete(true)} disabled={busy}>
                Verwijderen
              </Button>
            )}
          </div>
        </>
      )}

      {cancelDialogOpen && (
        <Modal
          title={`Opdracht ${order.orderNumber} annuleren`}
          onClose={() => setCancelDialogOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCancelDialogOpen(false)} disabled={busy}>
                Terug
              </Button>
              <Button variant="danger" onClick={() => void handleCancel()} disabled={busy}>
                {busy ? 'Annuleren…' : 'Opdracht annuleren'}
              </Button>
            </>
          }
        >
          <FormField label="Reden" htmlFor="cancel-reason" required hint="De reden wordt vastgelegd in de historiek.">
            <textarea
              id="cancel-reason"
              rows={3}
              value={cancelReason}
              onChange={(e) => setCancelReason(e.target.value)}
              maxLength={500}
              autoFocus
            />
          </FormField>
        </Modal>
      )}

      {correctDialogOpen && (
        <Modal
          title={`Status van ${order.orderNumber} corrigeren`}
          onClose={() => setCorrectDialogOpen(false)}
          busy={busy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setCorrectDialogOpen(false)} disabled={busy}>
                Terug
              </Button>
              <Button onClick={() => void handleCorrectStatus()} disabled={busy}>
                {busy ? 'Corrigeren…' : 'Status corrigeren'}
              </Button>
            </>
          }
        >
          <p className="to-cancel-reason" role="note">
            Een correctie draait een per ongeluk gekozen status terug. De wijziging wordt met reden en gebruiker
            vastgelegd in de historiek; POD-, scan- en factuurgegevens blijven onaangetast.
          </p>
          <FormField label="Corrigeren naar" htmlFor="correct-target" required>
            <select
              id="correct-target"
              value={correctTarget}
              onChange={(e) => setCorrectTarget(e.target.value as TransportOrderStatus)}
              autoFocus
            >
              <option value="">— Kies status —</option>
              {order.allowedCorrections.map((target) => (
                <option key={target} value={target}>
                  {ORDER_STATUS_LABELS[target]}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Reden" htmlFor="correct-reason" required hint="Verplicht; wordt vastgelegd in de historiek.">
            <textarea
              id="correct-reason"
              rows={3}
              value={correctReason}
              onChange={(e) => setCorrectReason(e.target.value)}
              maxLength={1000}
            />
          </FormField>
        </Modal>
      )}

      {planStop && (
        <StopExecutionPlanDialog
          orderId={order.id}
          stop={planStop}
          onClose={() => setPlanStop(null)}
          onSaved={(updated) => {
            setOrder(updated)
            setPlanStop(null)
          }}
        />
      )}

      {confirmDelete && (
        <ConfirmDialog
          title={`Transportopdracht ${order.orderNumber} verwijderen?`}
          message={`Deze actie kan niet ongedaan worden gemaakt. De opdracht van ${order.customerName} wordt verwijderd; gekoppelde goederenlijnen, prijsregels en conceptplanning worden behandeld volgens de bestaande domeinregels.`}
          confirmLabel="Verwijderen"
          destructive
          onConfirm={handleDelete}
          onCancel={() => setConfirmDelete(false)}
        />
      )}

      {editLine && (
        <Modal
          title="Prijsregel aanpassen"
          onClose={() => setEditLine(null)}
          busy={pricingBusy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setEditLine(null)} disabled={pricingBusy}>
                Terug
              </Button>
              <Button onClick={() => void handleSaveEditLine()} disabled={pricingBusy}>
                {pricingBusy ? 'Opslaan…' : 'Opslaan'}
              </Button>
            </>
          }
        >
          <p className="customer-form-muted">{editLine.label}</p>
          <div className="tof-row">
            <FormField label="Aantal" htmlFor="price-line-qty">
              <input
                id="price-line-qty"
                type="number"
                step="0.01"
                value={editQuantity}
                onChange={(e) => setEditQuantity(e.target.value)}
                disabled={pricingBusy}
              />
            </FormField>
            <FormField label="Eenheidsprijs (€)" htmlFor="price-line-unit-price">
              <input
                id="price-line-unit-price"
                type="number"
                step="0.01"
                value={editUnitPrice}
                onChange={(e) => setEditUnitPrice(e.target.value)}
                disabled={pricingBusy}
              />
            </FormField>
          </div>
          <FormField
            label="Bedrag (€)"
            htmlFor="price-line-amount"
            hint={editAmountIsComputed ? 'Berekend als aantal × eenheidsprijs.' : 'Leeg = aantal × eenheidsprijs.'}
          >
            {editAmountIsComputed ? (
              <input id="price-line-amount" readOnly value={computedEditAmountDisplay} />
            ) : (
              <input
                id="price-line-amount"
                type="number"
                step="0.01"
                value={editAmount}
                onChange={(e) => setEditAmount(e.target.value)}
                disabled={pricingBusy}
              />
            )}
          </FormField>
          <FormField label="Reden" htmlFor="price-line-reason" required hint="Verplicht bij een aanpassing.">
            <input
              id="price-line-reason"
              value={editReason}
              onChange={(e) => setEditReason(e.target.value)}
              disabled={pricingBusy}
              maxLength={500}
              autoFocus
            />
          </FormField>
        </Modal>
      )}

      {removeLine && (
        <Modal
          title="Prijsregel verwijderen"
          onClose={() => setRemoveLine(null)}
          busy={pricingBusy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setRemoveLine(null)} disabled={pricingBusy}>
                Terug
              </Button>
              <Button variant="danger" onClick={() => void handleRemoveLine()} disabled={pricingBusy}>
                {pricingBusy ? 'Verwijderen…' : 'Verwijderen'}
              </Button>
            </>
          }
        >
          <p>
            Weet je zeker dat je de regel <strong>{removeLine.label}</strong> wilt verwijderen?
          </p>
          {removeLine.kind !== 'Manual' && (
            <FormField label="Reden" htmlFor="price-line-remove-reason" required hint="Verplicht bij het verwijderen van een berekende regel.">
              <input
                id="price-line-remove-reason"
                value={removeReason}
                onChange={(e) => setRemoveReason(e.target.value)}
                disabled={pricingBusy}
                maxLength={500}
                autoFocus
              />
            </FormField>
          )}
        </Modal>
      )}

      {addLineOpen && (
        <Modal
          title="Vrije prijsregel toevoegen"
          onClose={closeAddLine}
          busy={pricingBusy}
          footer={
            <>
              <Button variant="secondary" onClick={closeAddLine} disabled={pricingBusy}>
                Terug
              </Button>
              <Button onClick={() => void handleAddLine()} disabled={pricingBusy}>
                {pricingBusy ? 'Toevoegen…' : 'Toevoegen'}
              </Button>
            </>
          }
        >
          <FormField label="Omschrijving" htmlFor="add-line-label" required>
            <input id="add-line-label" value={addLabel} onChange={(e) => setAddLabel(e.target.value)} disabled={pricingBusy} maxLength={300} autoFocus />
          </FormField>
          <FormField label="Berekeningswijze" htmlFor="add-line-mode">
            <div role="radiogroup" className="tof-radio-row" id="add-line-mode">
              <label>
                <input
                  type="radio"
                  name="add-line-mode"
                  checked={addMode === 'perUnit'}
                  onChange={() => setAddMode('perUnit')}
                  disabled={pricingBusy}
                />{' '}
                Berekenen op basis van aantal en eenheidsprijs
              </label>
              <label>
                <input
                  type="radio"
                  name="add-line-mode"
                  checked={addMode === 'fixed'}
                  onChange={() => setAddMode('fixed')}
                  disabled={pricingBusy}
                />{' '}
                Vast bedrag
              </label>
            </div>
          </FormField>
          {addMode === 'perUnit' && (
            <>
              <div className="tof-row">
                <FormField label="Aantal" htmlFor="add-line-qty">
                  <input
                    id="add-line-qty"
                    type="number"
                    min="0.01"
                    step="any"
                    value={addQuantity}
                    onChange={(e) => setAddQuantity(e.target.value)}
                    disabled={pricingBusy}
                  />
                </FormField>
                <FormField label="Eenheid" htmlFor="add-line-unit" hint="Optioneel.">
                  <UnitSelect
                    id="add-line-unit"
                    value={addUnit}
                    onChange={setAddUnit}
                    units={unitTypes}
                    preferredUnits={[]}
                    disabled={pricingBusy}
                  />
                </FormField>
              </div>
              <div className="tof-row">
                <FormField label="Eenheidsprijs (€)" htmlFor="add-line-price">
                  <input
                    id="add-line-price"
                    type="number"
                    step="any"
                    value={addUnitPrice}
                    onChange={(e) => setAddUnitPrice(e.target.value)}
                    disabled={pricingBusy}
                  />
                </FormField>
                <FormField label="Totaalbedrag" htmlFor="add-line-total">
                  <input id="add-line-total" readOnly value={computedAddTotalDisplay} />
                </FormField>
              </div>
            </>
          )}
          {addMode === 'fixed' && (
            <FormField label="Totaalbedrag (€)" htmlFor="add-line-amount">
              <input
                id="add-line-amount"
                type="number"
                step="any"
                value={addAmount}
                onChange={(e) => setAddAmount(e.target.value)}
                disabled={pricingBusy}
              />
            </FormField>
          )}
          <FormField label="Reden" htmlFor="add-line-reason" hint="Optioneel.">
            <input id="add-line-reason" value={addReason} onChange={(e) => setAddReason(e.target.value)} disabled={pricingBusy} maxLength={500} />
          </FormField>
        </Modal>
      )}

      {calcDetailsOpen && order.pricingSnapshot && (
        <Modal title="Berekeningsdetails" onClose={() => setCalcDetailsOpen(false)}>
          <p className="customer-form-muted">
            Tariefdatum: {order.pricingSnapshot.tariffDate}
            {order.pricingSnapshot.zoneName ? ` · Zone: ${order.pricingSnapshot.zoneName} (${order.pricingSnapshot.zoneCode})` : ''}
            {order.pricingSnapshot.agreementNames ? ` · Tarief: ${order.pricingSnapshot.agreementNames}` : ''}
          </p>
          <pre className="to-calc-explanation">{order.pricingSnapshot.explanation}</pre>
        </Modal>
      )}

      {recalcConfirmOpen && (
        <ConfirmDialog
          title="Prijs herberekenen"
          message="De prijs is al gecontroleerd. Toch herberekenen?"
          confirmLabel="Herberekenen"
          busy={pricingBusy}
          onConfirm={() => void handleRecalculate()}
          onCancel={() => setRecalcConfirmOpen(false)}
        />
      )}
    </div>
  )
}
