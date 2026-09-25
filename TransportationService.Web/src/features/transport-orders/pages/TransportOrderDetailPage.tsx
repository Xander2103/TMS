import { useEffect, useState } from 'react'
import { Navigate, useNavigate, useParams } from 'react-router-dom'
import { addRecentItem } from '../../../hooks/recentItems'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
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
  correctTransportOrderStatus,
  deleteTransportOrder,
  getTransportOrder,
  updateTransportOrder,
} from '../api/transportOrdersApi'
import { TransportOrderForm } from '../components/TransportOrderForm'
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
  type TransportOrderDetail,
  type TransportOrderStatus,
  type TransportOrderStop,
} from '../types'
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
import { OrderPricingDialogs } from '../pricing/OrderPricingDialogs'
import { useOrderPricingEditor } from '../pricing/useOrderPricingEditor'






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
  // Master sprint 2026-09-21 (D5): state, rules and dialogs live in the shared editor so the
  // dossier price tab edits an order's lines through the very same code.
  const pricing = useOrderPricingEditor(order, { onOrderChanged: setOrder })

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
  const workspace: OrderDetailWorkspace = {
    order,
    busy,
    editing,
    entities,
    editable,
    deletable,
    planEditable,
    canEditPricingLines: pricing.canEditPricingLines,
    canEditPricingStatus: pricing.canEditPricingStatus,
    canLockPrice: pricing.canLockPrice,
    canViewPackages: hasPermission('packages.view'),
    canViewMessages: hasPermission('customer_messages.view'),
    canChangeStatus: hasAnyPermission(['orders.change_status', 'orders.manage']),
    canEditOrder: hasPermission('orders.edit'),
    pricingStatus: pricing.pricingStatus,
    pricingLocked: pricing.pricingLocked,
    pricingBusy: pricing.pricingBusy,
    invoiceLines: pricing.invoiceLines,
    notAppliedLines: pricing.notAppliedLines,
    coverage: pricing.coverage,
    unpricedCoverage: pricing.unpricedCoverage,
    priceDisplay: pricing.priceDisplay,
    totalPrice: pricing.totalPrice,
    unitLabel,
    aggregateCargo,
    setOrder,
    openTab: (tab) => navigate(orderTabPath(id, tab)),
    setPlanStop,
    openCustomerChange: () => setCustomerChangeOpen(true),
    openEntityChange: () => setEntityChangeOpen(true),
    handleConfirmPriceClick: pricing.handleConfirmPriceClick,
    openReopenPrice: pricing.openReopenPrice,
    handleRecalculateClick: pricing.handleRecalculateClick,
    openAddLine: pricing.openAddLine,
    openCalcDetails: pricing.openCalcDetails,
    handleConfirmLine: pricing.handleConfirmLine,
    openEditLine: pricing.openEditLine,
    openRemoveLine: pricing.openRemoveLine,
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

      <OrderPricingDialogs editor={pricing} unitTypes={unitTypes} />
    </div>
    </OrderDetailContext.Provider>
  )
}
