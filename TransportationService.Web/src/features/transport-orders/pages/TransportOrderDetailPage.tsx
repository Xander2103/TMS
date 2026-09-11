import { useEffect, useState } from 'react'
import { Navigate, useNavigate, useParams } from 'react-router-dom'
import { addRecentItem } from '../../../hooks/recentItems'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
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
  confirmOrderPricing,
  correctTransportOrderStatus,
  deleteTransportOrder,
  getTransportOrder,
  recalculateOrderPricing,
  reopenOrderPricing,
  saveOrderPriceLines,
  updateTransportOrder,
  type SaveOrderPriceLineInput,
} from '../api/transportOrdersApi'
import { TransportOrderForm } from '../components/TransportOrderForm'
import { UnitSelect } from '../components/UnitSelect'
import { OrderDocumentsPanel } from '../components/OrderDocumentsPanel'
import { StopExecutionPlanDialog } from '../components/StopExecutionPlanDialog'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { OrderCustomerChangeDialog } from '../components/OrderCustomerChangeDialog'
import { OrderLegalEntityDialog } from '../components/OrderLegalEntityDialog'
import { getLegalEntityOptions } from '../../legal-entities/api/legalEntitiesApi'
import type { LegalEntityOption } from '../../legal-entities/types'
import '../components/commercialChange.css'
import {
  ORDER_STATUS_LABELS,
  PRICING_COVERAGE_LABELS,
  PRICING_COVERAGE_TONE,
  priceStatusDisplay,
  type OrderPricingLine,
  type TransportOrderDetail,
  type TransportOrderStatus,
  type TransportOrderStop,
} from '../types'
import { formatCurrency, formatQuantity } from '../../../utils/numbers'
import { useLocale } from '../../../i18n/localeContext'
import './transport-orders.css'
import '../detail/order-detail.css'
import { OrderDetailContext, type OrderDetailWorkspace } from '../detail/orderDetailContext'
import { ORDER_TABS, isOrderTab, orderTabPath, type OrderTab } from '../detail/orderSections'
import { OrderDetailHeader } from '../detail/OrderDetailHeader'
import { OrderAttention } from '../detail/OrderAttention'
import { OrderSubnav } from '../detail/OrderSubnav'
import { OrderOverview } from '../detail/OrderOverview'
import { OrderLadingSection } from '../detail/OrderLadingSection'
import { OrderPriceSection } from '../detail/OrderPriceSection'
import { OrderStopsSection } from '../detail/OrderStopsSection'
import { OrderColliSection } from '../detail/OrderColliSection'
import { OrderHistorySection } from '../detail/OrderHistorySection'
import { OrderMessagesSection } from '../detail/OrderMessagesSection'






export function TransportOrderDetailPage() {
  const { t } = useLocale()
  const { id = '', section } = useParams<{ id: string; section?: string }>()
  const activeTab: OrderTab = isOrderTab(section) ? section : 'overzicht'
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
  // Sprint 6: commercial identity of the order (customer / invoicing entity) changes through
  // explicit, previewed flows — never through the edit form.
  const [customerChangeOpen, setCustomerChangeOpen] = useState(false)
  const [entityChangeOpen, setEntityChangeOpen] = useState(false)
  const [entities, setEntities] = useState<LegalEntityOption[]>([])

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
  // Wave 2026-08-04 §8: confirmation workflow dialogs.
  const [confirmPriceOpen, setConfirmPriceOpen] = useState(false)
  const [confirmPriceReason, setConfirmPriceReason] = useState('')
  const [reopenPriceOpen, setReopenPriceOpen] = useState(false)
  const [reopenPriceReason, setReopenPriceReason] = useState('')

  useEffect(() => {
    let mounted = true
    getLegalEntityOptions()
      .then((options) => {
        if (mounted) setEntities(options)
      })
      .catch(() => undefined)
    return () => {
      mounted = false
    }
  }, [])

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
      showSuccess(`Status gewijzigd naar ${t(ORDER_STATUS_LABELS[target])}.`)
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
      showSuccess(`Status gecorrigeerd naar ${t(ORDER_STATUS_LABELS[updated.status])}.`)
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

  // --- Wave 2026-08-04 §8: Prijs bevestigen / Prijs aanpassen -----------------------------
  async function handleConfirmPrice(unpricedGoodsReason: string | null) {
    if (!order) return
    setPricingBusy(true)
    try {
      const updated = await confirmOrderPricing(order.id, unpricedGoodsReason)
      setOrder(updated)
      showSuccess('Prijs bevestigd.')
      setConfirmPriceOpen(false)
      setConfirmPriceReason('')
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De prijs kon niet worden bevestigd.')
    } finally {
      setPricingBusy(false)
    }
  }

  function handleConfirmPriceClick() {
    if (unpricedCoverage.length > 0) {
      // Blocked or override-with-reason — both explained in the dialog.
      setConfirmPriceReason('')
      setConfirmPriceOpen(true)
    } else {
      void handleConfirmPrice(null)
    }
  }

  async function handleReopenPrice() {
    if (!order) return
    if (!reopenPriceReason.trim()) {
      showError('Geef een reden op om de bevestigde prijs aan te passen.')
      return
    }
    setPricingBusy(true)
    try {
      const updated = await reopenOrderPricing(order.id, reopenPriceReason.trim())
      setOrder(updated)
      showSuccess('De prijs staat terug op "Nog te bevestigen".')
      setReopenPriceOpen(false)
      setReopenPriceReason('')
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De prijs kon niet worden aangepast.')
    } finally {
      setPricingBusy(false)
    }
  }

  if (loadError) return <ErrorState message={loadError} />
  if (!order) return <LoadingState message="Opdracht laden..." />
  // Redesign 2026-09-12: an unknown segment lands on the overview with a clean URL.
  if (section !== undefined && !isOrderTab(section)) return <Navigate to={orderTabPath(id, 'overzicht')} replace />

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
  const canOverrideIncomplete = hasAnyPermission(['orders.confirm_incomplete_price', 'orders.manage'])
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
  // §9: one visible price status — Bevestigd / Onvolledig / Nog te bevestigen / Gefactureerd.
  const priceDisplay = priceStatusDisplay(order.pricingSnapshot?.status, unpricedCoverage.length > 0)
  const totalPrice = order.pricingSnapshot?.linesTotal ?? order.agreedPrice

  const addQuantityNum = parseNum(addQuantity)
  const addUnitPriceNum = parseNum(addUnitPrice)
  const computedAddTotal =
    addMode === 'perUnit' && addQuantityNum !== null && addUnitPriceNum !== null
      ? round2(addQuantityNum * addUnitPriceNum)
      : null
  const computedAddTotalDisplay =
    computedAddTotal !== null ? formatCurrency(computedAddTotal) : '—'

  const editQuantityNum = parseNum(editQuantity)
  const editUnitPriceNum = parseNum(editUnitPrice)
  const computedEditAmount =
    editQuantityNum !== null && editUnitPriceNum !== null ? round2(editQuantityNum * editUnitPriceNum) : null
  const editAmountIsComputed = computedEditAmount !== null
  const computedEditAmountDisplay =
    computedEditAmount !== null ? formatCurrency(computedEditAmount) : '—'

  const workspace: OrderDetailWorkspace = {
    order,
    busy,
    editing,
    entities,
    editable,
    deletable,
    planEditable,
    canEditPricingLines,
    canEditPricingStatus,
    canLockPrice,
    canViewPackages: hasPermission('packages.view'),
    canViewMessages: hasPermission('customer_messages.view'),
    canChangeStatus: hasAnyPermission(['orders.change_status', 'orders.manage']),
    canEditOrder: hasPermission('orders.edit'),
    pricingStatus,
    pricingLocked,
    pricingBusy,
    invoiceLines,
    notAppliedLines,
    coverage,
    unpricedCoverage,
    priceDisplay,
    totalPrice,
    unitLabel,
    aggregateCargo,
    setOrder,
    openTab: (tab) => navigate(orderTabPath(id, tab)),
    setPlanStop,
    openCustomerChange: () => setCustomerChangeOpen(true),
    openEntityChange: () => setEntityChangeOpen(true),
    handleConfirmPriceClick,
    openReopenPrice: () => {
      setReopenPriceReason('')
      setReopenPriceOpen(true)
    },
    handleRecalculateClick,
    openAddLine,
    openCalcDetails: () => setCalcDetailsOpen(true),
    handleConfirmLine: (line) => void handleConfirmLine(line),
    openEditLine,
    openRemoveLine: (line) => setRemoveLine(line),
  }

  return (
    <OrderDetailContext.Provider value={workspace}>
    <div className="tod-detail">
      <OrderDetailHeader
        onEdit={() => setEditing(true)}
        onDelete={() => setConfirmDelete(true)}
        onCancel={() => setCancelDialogOpen(true)}
        onCorrectStatus={() => setCorrectDialogOpen(true)}
        onTransition={(target) => void applyTransition(target)}
      />

      {customerChangeOpen && (
        <OrderCustomerChangeDialog
          orderId={order.id}
          orderNumber={order.orderNumber}
          currentCustomerId={order.customerId}
          currentCustomerName={order.customerName}
          onClose={() => setCustomerChangeOpen(false)}
          onChanged={(impact) => {
            setCustomerChangeOpen(false)
            showSuccess(t('transportOrders.commercial.changedCustomer', { name: impact.newCustomerName }))
            void getTransportOrder(order.id).then(setOrder).catch(() => undefined)
          }}
        />
      )}
      {entityChangeOpen && (
        <OrderLegalEntityDialog
          order={order}
          onClose={() => setEntityChangeOpen(false)}
          onChanged={(updated) => {
            setEntityChangeOpen(false)
            setOrder(updated)
            showSuccess(t('transportOrders.commercial.changedEntity'))
          }}
        />
      )}


      {editing ? (
        <TransportOrderForm
          mode="edit"
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
          <OrderAttention />
          <OrderSubnav orderId={order.id} tabs={[...ORDER_TABS]} />
          <div className="tod-subsection" data-tab={activeTab}>
            {activeTab === 'overzicht' && <OrderOverview />}
            {activeTab === 'lading' && <OrderLadingSection />}
            {activeTab === 'prijs' && <OrderPriceSection />}
            {activeTab === 'stops' && <OrderStopsSection />}
            {activeTab === 'colli' && <OrderColliSection />}
            {activeTab === 'historiek' && <OrderHistorySection />}
            {activeTab === 'berichten' && <OrderMessagesSection />}
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
                  {t(ORDER_STATUS_LABELS[target])}
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
          {coverage.length > 0 && (
            <div className="to-coverage">
              <h3>Prijsdekking per goederenlijn</h3>
              <ul>
                {coverage.map((c, index) => (
                  <li key={c.unitTypeId ?? `${c.unitLabel}-${index}`}>
                    <Badge tone={PRICING_COVERAGE_TONE[c.status]}>{t(PRICING_COVERAGE_LABELS[c.status])}</Badge>{' '}
                    {formatQuantity(c.quantity)} {c.unitLabel}
                    {c.status === 'Full' && ` — ${c.baseRuleName ?? 'basistarief'}: ${formatCurrency(c.baseAmount)}`}
                    {c.status !== 'Full' && c.reason ? ` — ${c.reason}` : ''}
                    {c.servicesAmount > 0 && ` · diensten ${formatCurrency(c.servicesAmount)}`}
                  </li>
                ))}
              </ul>
            </div>
          )}
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

      {confirmPriceOpen && (
        <Modal
          title="Prijs bevestigen"
          onClose={() => setConfirmPriceOpen(false)}
          busy={pricingBusy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setConfirmPriceOpen(false)} disabled={pricingBusy}>
                {canOverrideIncomplete ? 'Annuleren' : 'Sluiten'}
              </Button>
              {canOverrideIncomplete && (
                <Button
                  onClick={() => {
                    if (!confirmPriceReason.trim()) {
                      showError('Geef een reden op om te bevestigen terwijl niet alle goederen geprijsd zijn.')
                      return
                    }
                    void handleConfirmPrice(confirmPriceReason.trim())
                  }}
                  disabled={pricingBusy}
                >
                  Toch bevestigen
                </Button>
              )}
            </>
          }
        >
          <p>
            <strong>De prijs kan niet worden bevestigd.</strong>
          </p>
          <ul>
            {unpricedCoverage.map((c, index) => (
              <li key={c.unitTypeId ?? `${c.unitLabel}-${index}`}>
                {formatQuantity(c.quantity)} {c.unitLabel}:{' '}
                {(c.reason ?? 'geen passend basistarief').toLowerCase()}
              </li>
            ))}
          </ul>
          {canOverrideIncomplete ? (
            <FormField
              label="Reden"
              htmlFor="confirm-price-reason"
              hint="Verplicht — de waarschuwing blijft zichtbaar bij de bevestigde prijs."
              required
            >
              <input
                id="confirm-price-reason"
                value={confirmPriceReason}
                onChange={(e) => setConfirmPriceReason(e.target.value)}
                disabled={pricingBusy}
                maxLength={500}
                autoFocus
              />
            </FormField>
          ) : (
            <p className="customer-form-muted">
              Prijs de goederen (basistarief of goederenlijn corrigeren) of vraag iemand met de juiste rechten
              om toch te bevestigen.
            </p>
          )}
        </Modal>
      )}

      {reopenPriceOpen && (
        <Modal
          title="Prijs aanpassen"
          onClose={() => setReopenPriceOpen(false)}
          busy={pricingBusy}
          footer={
            <>
              <Button variant="secondary" onClick={() => setReopenPriceOpen(false)} disabled={pricingBusy}>
                Annuleren
              </Button>
              <Button onClick={() => void handleReopenPrice()} disabled={pricingBusy}>
                Prijs aanpassen
              </Button>
            </>
          }
        >
          <p>
            De prijs gaat terug naar <strong>Nog te bevestigen</strong>; de huidige totaalprijs en bevestiging
            blijven bewaard in de historiek. Bevestig de prijs opnieuw na het aanpassen.
          </p>
          <FormField label="Reden" htmlFor="reopen-price-reason" required>
            <input
              id="reopen-price-reason"
              value={reopenPriceReason}
              onChange={(e) => setReopenPriceReason(e.target.value)}
              disabled={pricingBusy}
              maxLength={500}
              autoFocus
            />
          </FormField>
        </Modal>
      )}
    </div>
    </OrderDetailContext.Provider>
  )
}
