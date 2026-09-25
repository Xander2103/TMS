import { useState } from 'react'
import { ApiError } from '../../../api/apiClient'
import { useToast } from '../../../components/ui/toastContext'
import { useAuth } from '../../auth/authContextValue'
import {
  confirmOrderPriceLine,
  confirmOrderPricing,
  recalculateOrderPricing,
  reopenOrderPricing,
  saveOrderPriceLines,
  type SaveOrderPriceLineInput,
} from '../api/transportOrdersApi'
import type { OrderDetailWorkspace } from '../detail/orderDetailContext'
import { priceStatusDisplay, type OrderPricingLine, type TransportOrderDetail } from '../types'

/**
 * The pricing slice of the order workspace: everything `OrderPricingPanel` renders against. The
 * order detail shell hands its whole workspace (a structural superset); the dossier price tab
 * hands exactly this.
 */
export type OrderPricingWorkspace = Pick<
  OrderDetailWorkspace,
  | 'order'
  | 'pricingStatus'
  | 'pricingLocked'
  | 'pricingBusy'
  | 'canEditPricingLines'
  | 'canEditPricingStatus'
  | 'canLockPrice'
  | 'invoiceLines'
  | 'notAppliedLines'
  | 'coverage'
  | 'unpricedCoverage'
  | 'priceDisplay'
  | 'totalPrice'
  | 'handleConfirmPriceClick'
  | 'openReopenPrice'
  | 'handleRecalculateClick'
  | 'openAddLine'
  | 'openCalcDetails'
  | 'handleConfirmLine'
  | 'openEditLine'
  | 'openRemoveLine'
>

interface UseOrderPricingEditorOptions {
  /** Every successful pricing mutation returns the fresh order; the host adopts it. */
  onOrderChanged: (order: TransportOrderDetail) => void
  /** Host-level lock (e.g. a closed dossier): every pricing action is hidden. */
  readOnly?: boolean
}

function parseNum(value: string): number | null {
  if (!value.trim()) return null
  const n = Number(value)
  return Number.isFinite(n) ? n : null
}

function round2(n: number): number {
  return Math.round(n * 100) / 100
}

/** Same members, any order — the goods link is a set. */
function sameIds(a: string[], b: string[]): boolean {
  return a.length === b.length && a.every((id) => b.includes(id))
}

/**
 * Master sprint 2026-09-21 (D5): THE order price line editor — state, rules and handlers of the
 * pricing lines / status workflow (spec ch. 24-26, wave 2026-08-04 §8), lifted unchanged out of
 * `TransportOrderDetailPage` so the dossier price tab edits an order's lines through the very
 * same code. `OrderPricingDialogs` renders the dialogs of the returned editor,
 * `OrderPricingPanel` the read model.
 *
 * D4: a sales line may be linked to goods lines (`cargoItemIds`); none = the whole trip/activity.
 */
export function useOrderPricingEditor(order: TransportOrderDetail | null, { onOrderChanged, readOnly = false }: UseOrderPricingEditorOptions) {
  const { showSuccess, showError } = useToast()
  const { hasAnyPermission } = useAuth()

  const [editLine, setEditLine] = useState<OrderPricingLine | null>(null)
  const [editQuantity, setEditQuantity] = useState('')
  const [editUnitPrice, setEditUnitPrice] = useState('')
  const [editAmount, setEditAmount] = useState('')
  const [editReason, setEditReason] = useState('')
  const [editCargoItemIds, setEditCargoItemIds] = useState<string[]>([])
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
  const [addCargoItemIds, setAddCargoItemIds] = useState<string[]>([])
  const [calcDetailsOpen, setCalcDetailsOpen] = useState(false)
  const [recalcConfirmOpen, setRecalcConfirmOpen] = useState(false)
  const [pricingBusy, setPricingBusy] = useState(false)
  // Wave 2026-08-04 §8: confirmation workflow dialogs.
  const [confirmPriceOpen, setConfirmPriceOpen] = useState(false)
  const [confirmPriceReason, setConfirmPriceReason] = useState('')
  const [reopenPriceOpen, setReopenPriceOpen] = useState(false)
  const [reopenPriceReason, setReopenPriceReason] = useState('')

  const canEditPricingLines = !readOnly && hasAnyPermission(['orders.override_price', 'orders.manage'])
  const canEditPricingStatus = !readOnly && hasAnyPermission(['orders.edit', 'orders.manage'])
  const canLockPrice = !readOnly && hasAnyPermission(['orders.lock_price', 'orders.manage'])
  const canOverrideIncomplete = !readOnly && hasAnyPermission(['orders.confirm_incomplete_price', 'orders.manage'])
  const pricingStatus = order?.pricingSnapshot?.status ?? 'Draft'
  const pricingLocked = pricingStatus === 'Locked' || pricingStatus === 'Invoiced'
  // Informational lines with a non-zero amount (e.g. the diesel surcharge) still carry a real
  // amount that will be applied at invoicing — they stay as a (dimmed) table row. Only
  // zero-amount informational lines (e.g. "no matching rule") are pure notices under "Niet toegepast".
  const invoiceLines = (order?.pricingLines ?? []).filter((l) => !l.informational || l.amount !== 0)
  const notAppliedLines = (order?.pricingLines ?? []).filter((l) => l.informational && l.amount === 0)
  // Wave 2026-08-04 §7: per-goods-line pricing coverage frozen on the snapshot.
  const coverage = order?.pricingSnapshot?.coverage ?? []
  const unpricedCoverage = coverage.filter((c) => c.status !== 'Full')
  // §9: one visible price status — Bevestigd / Onvolledig / Nog te bevestigen / Gefactureerd.
  const priceDisplay = priceStatusDisplay(order?.pricingSnapshot?.status, unpricedCoverage.length > 0)
  const totalPrice = order ? (order.pricingSnapshot?.linesTotal ?? order.agreedPrice) : null

  function resetAddLine() {
    setAddMode('perUnit')
    setAddLabel('')
    setAddQuantity('')
    setAddUnit(null)
    setAddUnitPrice('')
    setAddAmount('')
    setAddReason('')
    setAddCargoItemIds([])
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
      onOrderChanged(updated)
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
    setEditCargoItemIds(line.cargoItemIds ?? [])
  }

  /**
   * An edit that ONLY changes the goods link is no price correction (D4): the server keeps the
   * line's amount, kind and reason, so quantity/price stay off the wire and no reason is asked.
   */
  function isLinkOnlyEdit(line: OrderPricingLine): boolean {
    const priceUntouched =
      parseNum(editQuantity) === (line.quantity ?? null) &&
      parseNum(editUnitPrice) === (line.unitPrice ?? null) &&
      editAmount.trim() === '' &&
      editReason.trim() === ''
    return priceUntouched && !sameIds(editCargoItemIds, line.cargoItemIds ?? [])
  }

  async function handleSaveEditLine() {
    if (!editLine) return
    if (isLinkOnlyEdit(editLine)) {
      await submitPriceLine({
        lineKey: editLine.lineKey ?? null,
        label: editLine.label,
        quantity: null,
        unitPrice: null,
        amount: null,
        adjustReason: null,
        cargoItemIds: editCargoItemIds,
      })
      return
    }
    if (!editReason.trim()) {
      showError('Een reden is verplicht bij het aanpassen van een prijsregel.')
      return
    }
    const quantity = parseNum(editQuantity)
    const unitPrice = parseNum(editUnitPrice)
    const computedAmount = quantity !== null && unitPrice !== null ? round2(quantity * unitPrice) : null
    // The link only travels along when the planner changed it: absent = "leave the links alone".
    const linkChanged = !sameIds(editCargoItemIds, editLine.cargoItemIds ?? [])
    await submitPriceLine({
      lineKey: editLine.lineKey ?? null,
      label: editLine.label,
      quantity,
      unitPrice,
      amount: computedAmount !== null ? null : parseNum(editAmount),
      adjustReason: editReason.trim(),
      ...(linkChanged ? { cargoItemIds: editCargoItemIds } : {}),
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
    const link = addCargoItemIds.length > 0 ? { cargoItemIds: addCargoItemIds } : {}
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
        ...link,
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
        ...link,
      })
    }
  }

  async function handleConfirmLine(line: OrderPricingLine) {
    if (!order || !line.id) return
    setPricingBusy(true)
    try {
      const updated = await confirmOrderPriceLine(order.id, line.id)
      onOrderChanged(updated)
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
      onOrderChanged(updated)
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
      onOrderChanged(updated)
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
      onOrderChanged(updated)
      showSuccess('De prijs staat terug op "Nog te bevestigen".')
      setReopenPriceOpen(false)
      setReopenPriceReason('')
    } catch (err) {
      showError(err instanceof ApiError ? err.message : 'De prijs kon niet worden aangepast.')
    } finally {
      setPricingBusy(false)
    }
  }

  const addQuantityNum = parseNum(addQuantity)
  const addUnitPriceNum = parseNum(addUnitPrice)
  const computedAddTotal =
    addMode === 'perUnit' && addQuantityNum !== null && addUnitPriceNum !== null ? round2(addQuantityNum * addUnitPriceNum) : null

  const editQuantityNum = parseNum(editQuantity)
  const editUnitPriceNum = parseNum(editUnitPrice)
  const computedEditAmount =
    editQuantityNum !== null && editUnitPriceNum !== null ? round2(editQuantityNum * editUnitPriceNum) : null

  return {
    order,
    // permissions / read model (the `OrderPricingWorkspace` slice)
    canEditPricingLines,
    canEditPricingStatus,
    canLockPrice,
    canOverrideIncomplete,
    pricingStatus,
    pricingLocked,
    pricingBusy,
    invoiceLines,
    notAppliedLines,
    coverage,
    unpricedCoverage,
    priceDisplay,
    totalPrice,
    handleConfirmPriceClick,
    openReopenPrice: () => {
      setReopenPriceReason('')
      setReopenPriceOpen(true)
    },
    handleRecalculateClick,
    openAddLine,
    openCalcDetails: () => setCalcDetailsOpen(true),
    handleConfirmLine: (line: OrderPricingLine) => void handleConfirmLine(line),
    openEditLine,
    openRemoveLine: (line: OrderPricingLine) => setRemoveLine(line),
    // dialog state (rendered by OrderPricingDialogs)
    editLine,
    closeEditLine: () => setEditLine(null),
    editQuantity,
    setEditQuantity,
    editUnitPrice,
    setEditUnitPrice,
    editAmount,
    setEditAmount,
    editReason,
    setEditReason,
    editCargoItemIds,
    setEditCargoItemIds,
    editReasonRequired: editLine ? !isLinkOnlyEdit(editLine) : true,
    computedEditAmount,
    handleSaveEditLine,
    removeLine,
    closeRemoveLine: () => setRemoveLine(null),
    removeReason,
    setRemoveReason,
    handleRemoveLine,
    addLineOpen,
    closeAddLine,
    addMode,
    setAddMode,
    addLabel,
    setAddLabel,
    addQuantity,
    setAddQuantity,
    addUnit,
    setAddUnit,
    addUnitPrice,
    setAddUnitPrice,
    addAmount,
    setAddAmount,
    addReason,
    setAddReason,
    addCargoItemIds,
    setAddCargoItemIds,
    computedAddTotal,
    handleAddLine,
    calcDetailsOpen,
    closeCalcDetails: () => setCalcDetailsOpen(false),
    recalcConfirmOpen,
    closeRecalcConfirm: () => setRecalcConfirmOpen(false),
    handleRecalculate,
    confirmPriceOpen,
    closeConfirmPrice: () => setConfirmPriceOpen(false),
    confirmPriceReason,
    setConfirmPriceReason,
    handleConfirmPrice,
    reopenPriceOpen,
    closeReopenPrice: () => setReopenPriceOpen(false),
    reopenPriceReason,
    setReopenPriceReason,
    handleReopenPrice,
  }
}

export type OrderPricingEditor = ReturnType<typeof useOrderPricingEditor>
