import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { Navigate, useNavigate, useParams } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { ErrorState } from '../../../components/feedback/ErrorState'
import { BackButton } from '../../../components/ui/BackButton'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { Modal } from '../../../components/ui/Modal'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { addRecentItem } from '../../../hooks/recentItems'
import { useAuth } from '../../auth/authContextValue'
import { searchCustomers } from '../../customers/api/customersApi'
import { getTransportOrder } from '../../transport-orders/api/transportOrdersApi'
import type { TransportOrderDetail } from '../../transport-orders/types'
import { getUsers } from '../../users/api/usersApi'
import {
  closeDossier,
  getDossier,
  removeDossierRelation,
  reopenDossier,
  unlinkDossierOrder,
  updateDossier,
} from '../api/dossiersApi'
import { type DossierActivity, type DossierDetail, type ReadinessIssue, type ReadinessSection } from '../types'
import { ActivityDrawer } from '../components/ActivityDrawer'
import { AddActivityDialog } from '../components/AddActivityDialog'
import { AttentionPanel } from '../components/AttentionPanel'
import { DossierHeader, type DossierMenuAction } from '../components/DossierHeader'
import { DossierCustomerChangeDialog } from '../components/DossierCustomerChangeDialog'
import { AddRelationDialog, LinkOrderDialog } from '../components/DossierLinkDialogs'
import { DossierOverview } from '../components/DossierOverview'
import type { DossierPricePanelHandle } from '../components/DossierPricePanel'
import type { DossierRouteEditorHandle } from '../components/DossierRouteEditor'
import { DossierSubnav } from '../components/DossierSubnav'
import { GoodsDrawer } from '../components/GoodsDrawer'
import { DossierSectionsProvider } from '../components/DossierSectionsProvider'
import { DossierActivitiesSection } from '../components/sections/DossierActivitiesSection'
import { DossierDocumentsSection } from '../components/sections/DossierDocumentsSection'
import { DossierGoodsSection } from '../components/sections/DossierGoodsSection'
import { DossierHistorySection } from '../components/sections/DossierHistorySection'
import { DossierPriceSection } from '../components/sections/DossierPriceSection'
import { DossierRouteSection } from '../components/sections/DossierRouteSection'
import { DOSSIER_TABS, TAB_FOR_SECTION, dossierTabPath, isDossierTab, type DossierTab } from '../dossierSections'
import { DossierWorkspaceContext, type DossierWorkspace } from '../dossierWorkspace'
import { useDossierNavigator, type DossierSectionId } from '../sectionRegistry'
import '../../dashboard/pages/dashboard.css'
import './dossiers.css'
import './dossier-detail.css'

/** True when a thrown error is a dossier 409 whose body carries the current state. */
function conflictBody(err: unknown): DossierDetail | null {
  if (err instanceof ApiError && err.status === 409 && err.body && typeof err.body === 'object' && 'dossierNumber' in err.body) {
    return err.body as DossierDetail
  }
  return null
}

/**
 * Redesign 2026-09-11 — the dossier as a navigation-based workspace: a stable shell (header,
 * attention strip, subnav, dossier-level state and dialogs) and ONE subsection at a time,
 * selected by the URL segment. Overzicht is read-only; editing lives in the subsections.
 */
export function DossierDetailPage() {
  return (
    <DossierSectionsProvider>
      <DossierDetailContent />
    </DossierSectionsProvider>
  )
}

function DossierDetailContent() {
  const { id, section } = useParams<{ id: string; section?: string }>()
  const navigate = useNavigate()
  const { t } = useLocale()
  const toast = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('dossiers.manage')
  // The inline route/price editors write to the linked ORDER, so they follow the order's
  // permission model — not only the dossier's (a dossiers.manage user without orders.edit got a
  // 403 at save time from the old drawer).
  const canEditOrder = hasPermission('orders.edit') || hasPermission('orders.manage')
  const canEditPriceLines = hasPermission('orders.override_price') || hasPermission('orders.manage')
  // Stap 13: the price of a standalone billable activity is a commercial act on a non-order and
  // has its own right — neither orders.edit nor dossiers.manage implies it.
  const canEditActivityPrice = hasPermission('dossiers.price')
  const canCreateLocations = hasPermission('locations.create')
  const goTo = useDossierNavigator()
  const routeEditorRef = useRef<DossierRouteEditorHandle>(null)
  const pricePanelRef = useRef<DossierPricePanelHandle>(null)
  const addActivityRef = useRef<HTMLButtonElement>(null)
  const subnavRef = useRef<HTMLElement>(null)

  const [dossier, setDossier] = useState<DossierDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  /** 409-payload van een collega-wijziging; [Herladen] neemt deze staat over. */
  const [conflict, setConflict] = useState<DossierDetail | null>(null)

  // The TARGET order (drives Route/Goederen/Prijs summaries + drawers), keyed by order id so
  // switching orders never shows stale data and the effect stays callback-only. Lives in the
  // shell, so a subsection switch never refetches it.
  const [loadedOrder, setLoadedOrder] = useState<{ id: string; order: TransportOrderDetail | null } | null>(null)
  // Bumped by "Opnieuw laden" after a failed order load (the editors keep their entered state).
  const [orderLoadToken, setOrderLoadToken] = useState(0)
  // Hardening 2026-09-10: a dossier can hold several transport orders. The planner picks the
  // target explicitly (DossierOrderSwitcher); null = the default (first activity with an order,
  // else the first transport activity). Attention actions select the order they are about.
  const [selectedActivityId, setSelectedActivityId] = useState<string | null>(null)
  // Stap 13: the price section also targets standalone billable activities (Opslag, Kraan). The
  // route keeps its own transport target so that selecting a standalone unit changes ONLY the
  // price target (no order reload, nothing collapses); selecting a transport unit moves both.
  const [routeSelectionId, setRouteSelectionId] = useState<string | null>(null)
  const [routeDirty, setRouteDirty] = useState(false)
  // An attention jump: performed once its subsection is mounted, its target unit selected and
  // (for order-bound sections) the order on screen. `token` re-arms the effect for a jump that
  // lands on the subsection already open.
  const pendingJump = useRef<{ section: DossierSectionId; field: string | null; activityId: string | null } | null>(null)
  const [jumpToken, setJumpToken] = useState(0)

  // Dialogs & drawers
  const [showAddActivity, setShowAddActivity] = useState(false)
  const [drawerActivity, setDrawerActivity] = useState<DossierActivity | null>(null)
  const [goodsDrawerOpen, setGoodsDrawerOpen] = useState(false)
  const [confirmClose, setConfirmClose] = useState(false)
  const [showLinkOrder, setShowLinkOrder] = useState(false)
  const [showAddRelation, setShowAddRelation] = useState(false)

  // Kop bewerken
  const [editing, setEditing] = useState(false)
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [notes, setNotes] = useState('')
  const [customerReference, setCustomerReference] = useState('')
  const [dossierDate, setDossierDate] = useState('')
  const [customerId, setCustomerId] = useState<string | null>(null)
  const [responsibleUserId, setResponsibleUserId] = useState<string | null>(null)
  const [customerOptions, setCustomerOptions] = useState<SearchableSelectOption[]>([])
  // Sprint 6: once a dossier has a customer or orders, the customer only changes via the
  // previewed flow (pricing, entity and linked orders are re-evaluated together).
  const [customerChangeOpen, setCustomerChangeOpen] = useState(false)
  const [userOptions, setUserOptions] = useState<SearchableSelectOption[]>([])
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const applyDossier = useCallback((updated: DossierDetail) => {
    setDossier(updated)
    setConflict(null)
  }, [])

  /** Centrale 409-afhandeling: toont de banner en meldt de aanroeper dat het afgehandeld is. */
  const handleConflict = useCallback((err: unknown): boolean => {
    const current = conflictBody(err)
    if (current) {
      setConflict(current)
      return true
    }
    return false
  }, [])

  const load = useCallback(() => {
    if (!id) return
    getDossier(id)
      .then((data) => {
        applyDossier(data)
        setLoadError(null)
        addRecentItem({
          category: t('navigation.menu.dossiers'),
          title: `${data.dossierNumber} · ${data.title}`,
          route: `/dossiers/${data.id}`,
        })
      })
      .catch(() => setLoadError(t('dossiers.detail.loadFailed')))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, applyDossier])

  useEffect(() => {
    load()
  }, [load])

  const rawActivities = dossier?.activities
  const orderedActivities = useMemo(() => [...(rawActivities ?? [])].sort((a, b) => a.sequence - b.sequence), [rawActivities])
  const activities = orderedActivities
  const transportActivities = orderedActivities.filter((a) => a.hasStops)
  // The route/goods surfaces work on ONE transport activity — the explicitly selected one, else
  // the first that already has an order, else the first transport activity (whose order the
  // route editor creates on save). With several transport activities the switcher is shown.
  const routeActivity =
    (routeSelectionId ? transportActivities.find((a) => a.id === routeSelectionId) : undefined) ??
    transportActivities.find((a) => a.linkedTransportOrderId) ??
    transportActivities[0] ??
    null
  // Billable units (stap 13): every activity of a billable type, transport or standalone. The
  // price target is the selected unit, else the route target when billable, else the first
  // STANDALONE billable unit — never a different transport activity than the route works on,
  // because the loaded order belongs to the route target.
  const billableActivities = orderedActivities.filter((a) => a.isBillable !== false)
  const priceActivity =
    (selectedActivityId ? billableActivities.find((a) => a.id === selectedActivityId) : undefined) ??
    (routeActivity && routeActivity.isBillable !== false ? routeActivity : undefined) ??
    billableActivities.find((a) => !a.hasStops) ??
    null
  const priceTargetIsStandalone = priceActivity !== null && !priceActivity.hasStops
  const firstLinkedOrderId = routeActivity?.linkedTransportOrderId ?? null
  // The create-order offer in the price panel concerns the TARGET activity when it has no order
  // (never a different one than the route editor works on), else any transport activity without one.
  const activityWithoutOrder =
    routeActivity && !routeActivity.linkedTransportOrderId
      ? routeActivity
      : (orderedActivities.find((a) => a.hasStops && !a.linkedTransportOrderId) ?? null)

  useEffect(() => {
    if (!firstLinkedOrderId) return
    let mounted = true
    getTransportOrder(firstLinkedOrderId)
      .then((order) => {
        if (mounted) setLoadedOrder({ id: firstLinkedOrderId, order })
      })
      .catch(() => {
        if (mounted) setLoadedOrder({ id: firstLinkedOrderId, order: null })
      })
    return () => {
      mounted = false
    }
  }, [firstLinkedOrderId, dossier?.version, orderLoadToken])

  const firstOrder = firstLinkedOrderId && loadedOrder?.id === firstLinkedOrderId ? loadedOrder.order : null
  // True only while a DIFFERENT order than the one on screen is being fetched (an order switch or
  // the first load). A refetch of the same order after a save keeps the current content mounted.
  // While true, the route/goods/price bodies show a placeholder inside a RetainedHeight, so the
  // page never shrinks under the planner's scroll position.
  const firstOrderLoading = Boolean(firstLinkedOrderId) && loadedOrder?.id !== firstLinkedOrderId

  // --- Subsection from the URL ---------------------------------------------------------------
  const hasRoute = activities.some((a) => a.hasStops)
  const hasGoods = activities.some((a) => a.supportsGoods)
  const availableTabs = useMemo(
    () => DOSSIER_TABS.filter((tab) => (tab === 'route' ? hasRoute : tab === 'goederen' ? hasGoods : true)),
    [hasRoute, hasGoods],
  )
  const requestedTab: DossierTab | null = section === undefined ? 'overzicht' : isDossierTab(section) ? section : null
  const activeTab: DossierTab = requestedTab && availableTabs.includes(requestedTab) ? requestedTab : 'overzicht'

  // Finish a deferred attention jump once its subsection is mounted (the register callback ran
  // in the same commit as the route change), its target unit is selected and its order is on
  // screen (a standalone price target and the activity list load nothing).
  const targetReady = routeActivity !== null && (!firstLinkedOrderId || Boolean(firstOrder))
  const priceActivityId = priceActivity?.id ?? null
  const routeActivityId = routeActivity?.id ?? null
  useEffect(() => {
    const jump = pendingJump.current
    if (!jump) return
    if (TAB_FOR_SECTION[jump.section] !== activeTab) return
    const onPrice = jump.section === 'prijs'
    const orderBound = jump.section === 'route' || jump.section === 'goederen' || (onPrice && !priceTargetIsStandalone)
    if (jump.activityId) {
      const currentId = onPrice ? priceActivityId : routeActivityId
      if (currentId !== jump.activityId) return
    }
    if (orderBound && !targetReady) return
    if (!goTo(jump.section, jump.field)) return
    pendingJump.current = null
  }, [activeTab, jumpToken, targetReady, routeActivityId, priceActivityId, priceTargetIsStandalone, goTo])

  // A subsection switch keeps the scroll offset (the shell stays mounted); only when the subnav
  // itself was scrolled out of view is it brought back — never the global top, and never while
  // an attention jump is about to scroll to its own destination.
  useEffect(() => {
    if (pendingJump.current) return
    const nav = subnavRef.current
    if (nav && nav.getBoundingClientRect().top < 0) nav.scrollIntoView?.({ block: 'start' })
  }, [activeTab])

  useEffect(() => {
    if (!editing) return
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((result) => setCustomerOptions(result.items.map((c) => ({ value: c.id, label: c.name }))))
      .catch(() => setCustomerOptions([]))
    getUsers()
      .then((users) =>
        setUserOptions(
          users.filter((u) => u.isActive).map((u) => ({ value: u.id, label: `${u.firstName} ${u.lastName}` })),
        ),
      )
      .catch(() => setUserOptions([]))
  }, [editing])

  if (loadError) return <ErrorState message={loadError} />
  if (!dossier || !id) return <LoadingState message={t('dossiers.detail.loading')} />
  // An unknown or unavailable segment lands on the overview with a clean URL.
  if (section !== undefined && activeTab !== section) return <Navigate to={dossierTabPath(id, 'overzicht')} replace />

  const isOpen = dossier.status === 'Open'
  // Compat: opdrachten die (nog) niet door een activiteit vertegenwoordigd worden.
  const activityOrderIds = new Set(activities.map((a) => a.linkedTransportOrderId).filter(Boolean))
  const legacyOrders = dossier.orders.filter((o) => !activityOrderIds.has(o.orderId))

  function selectActivity(activityId: string) {
    if (routeDirty) return
    setSelectedActivityId(activityId)
    // A transport unit is the target of BOTH sections; a standalone one only of the price section.
    if (activities.find((a) => a.id === activityId)?.hasStops) setRouteSelectionId(activityId)
  }

  /**
   * Attention jump. Opens the subsection that hosts the section, first makes the activity/order
   * the issue is about the target of the editors, and finishes with the existing
   * `goTo(section, field)` (scroll + field focus + highlight) once everything is on screen.
   * Switching the target is refused while the route editor holds unsaved edits — the jump then
   * lands on the current target; leaving the route tab itself is guarded by the unsaved-changes
   * dialog of the editor.
   */
  function goToSection(section: ReadinessSection, field: string | null, issue?: ReadinessIssue) {
    const candidates = section === 'prijs' ? billableActivities : transportActivities
    const current = section === 'prijs' ? priceActivity : routeActivity
    const target =
      (issue?.activityId ? candidates.find((a) => a.id === issue.activityId) : undefined) ??
      (issue?.transportOrderId ? candidates.find((a) => a.linkedTransportOrderId === issue.transportOrderId) : undefined)
    const switching = Boolean(target) && target!.id !== current?.id && !routeDirty
    if (switching) selectActivity(target!.id)
    pendingJump.current = { section, field, activityId: switching ? target!.id : (current?.id ?? null) }
    openTab(TAB_FOR_SECTION[section])
    setJumpToken((token) => token + 1)
  }

  function openTab(tab: DossierTab) {
    if (tab !== activeTab) navigate(dossierTabPath(id!, tab))
  }

  /** Inline route/price saves return the fresh order; the dossier is re-read for readiness + totals. */
  function handleOrderSaved(updated: TransportOrderDetail) {
    setLoadedOrder({ id: updated.id, order: updated })
    load()
  }

  function openActivity(activity: DossierActivity) {
    if (activity.hasStops && activity.linkedTransportOrderId) {
      navigate(`/transport-orders/${activity.linkedTransportOrderId}`)
      return
    }
    setDrawerActivity(activity)
  }

  function startEdit() {
    if (!dossier) return
    setTitle(dossier.title)
    setDescription(dossier.description ?? '')
    setNotes(dossier.notes ?? '')
    setCustomerReference(dossier.customerReference ?? '')
    setDossierDate(dossier.dossierDate ?? '')
    setCustomerId(dossier.customerId)
    setResponsibleUserId(dossier.responsibleUserId)
    setFieldErrors({})
    setFormError(null)
    setEditing(true)
  }

  async function run(action: () => Promise<DossierDetail>, successMessage: string) {
    setBusy(true)
    try {
      const updated = await action()
      applyDossier(updated)
      toast.showSuccess(successMessage)
      return true
    } catch (err) {
      if (handleConflict(err)) return false
      toast.showError(describeApiError(err, t('dossiers.detail.actionFailed')).message)
      return false
    } finally {
      setBusy(false)
    }
  }

  async function submitEdit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setFormError(null)
    setFieldErrors({})
    try {
      const updated = await updateDossier(id!, {
        title,
        description: description || null,
        customerId,
        responsibleUserId,
        notes: notes || null,
        customerReference: customerReference.trim() || null,
        dossierDate: dossierDate || null,
        version: dossier!.version,
      })
      applyDossier(updated)
      setEditing(false)
      toast.showSuccess(t('dossiers.detail.updated'))
    } catch (err) {
      if (handleConflict(err)) {
        setEditing(false)
        return
      }
      const described = describeApiError(err, t('dossiers.detail.updateFailed'))
      setFormError(described.message)
      setFieldErrors(described.fieldErrors)
    } finally {
      setBusy(false)
    }
  }

  const menuActions: DossierMenuAction[] = []
  if (canManage && isOpen) menuActions.push({ key: 'edit', label: t('dossiers.detail.menuEdit'), onSelect: startEdit })
  if (canManage && isOpen && dossier.customerId) {
    menuActions.push({ key: 'change-customer', label: t('dossiers.customerChange.action'), onSelect: () => setCustomerChangeOpen(true) })
  }
  if (canManage) {
    menuActions.push(
      isOpen
        ? { key: 'close', label: t('dossiers.detail.menuClose'), onSelect: () => setConfirmClose(true) }
        : {
            key: 'reopen',
            label: t('dossiers.detail.menuReopen'),
            onSelect: () => void run(() => reopenDossier(id), t('dossiers.detail.reopened')),
          },
    )
    menuActions.push({ key: 'relation', label: t('dossiers.detail.menuRelation'), onSelect: () => setShowAddRelation(true) })
    if (isOpen) {
      menuActions.push({ key: 'link-order', label: t('dossiers.detail.menuLinkOrder'), onSelect: () => setShowLinkOrder(true) })
    }
  }
  if (hasPermission('incidents.manage')) {
    menuActions.push({
      key: 'incident',
      label: t('dossiers.detail.menuIncident'),
      onSelect: () => navigate(`/incidents/new?dossierId=${dossier.id}`),
    })
  }
  menuActions.push({
    key: 'history',
    label: t('dossiers.detail.menuHistory'),
    onSelect: () => openTab('historiek'),
  })

  const workspace: DossierWorkspace = {
    dossier,
    isOpen,
    canManage,
    canEditRoute: canManage && canEditOrder && isOpen,
    canEditOrder,
    canEditPriceLines,
    canEditActivityPrice,
    canCreateLocations,
    busy,
    activities: orderedActivities,
    transportActivities,
    billableActivities,
    legacyOrders,
    routeActivity,
    priceActivity,
    priceTargetIsStandalone,
    activityWithoutOrder,
    firstLinkedOrderId,
    firstOrder,
    firstOrderLoading,
    routeDirty,
    setRouteDirty,
    selectActivity,
    applyDossier,
    handleConflict,
    handleOrderSaved,
    retryOrderLoad: () => setOrderLoadToken((token) => token + 1),
    openActivity,
    openAddActivity: () => setShowAddActivity(true),
    openGoodsDrawer: () => setGoodsDrawerOpen(true),
    unlinkOrder: (orderId) => void run(() => unlinkDossierOrder(id, orderId), t('dossiers.detail.unlinked')),
    removeRelation: (relationId) => void run(() => removeDossierRelation(id, relationId), t('dossiers.detail.relationRemoved')),
    focusRouteField: (field) => routeEditorRef.current?.focusField(field) ?? false,
    focusPriceField: (field) => pricePanelRef.current?.focusField(field) ?? false,
    focusAddActivity: () => {
      addActivityRef.current?.focus()
      return Boolean(addActivityRef.current)
    },
  }

  return (
    <DossierWorkspaceContext.Provider value={workspace}>
      <div className="dossier-detail">
        <BackButton to="/dossiers" label={t('dossiers.detail.back')} />

        <DossierHeader
          dossier={dossier}
          canManage={canManage}
          onAddActivity={() => setShowAddActivity(true)}
          menuActions={menuActions}
          onUpdated={applyDossier}
          onConflict={handleConflict}
        />

        {conflict && (
          <div className="dossier-conflict-banner" role="alert">
            <span>{t('dossiers.detail.conflict')}</span>
            <Button variant="secondary" onClick={() => applyDossier(conflict)}>
              {t('dossiers.detail.reload')}
            </Button>
          </div>
        )}

        <AttentionPanel issues={dossier.readiness} onNavigate={goToSection} />

        <DossierSubnav ref={subnavRef} dossierId={id} tabs={availableTabs} />

        <div className="dossier-subsection" data-tab={activeTab}>
          {activeTab === 'overzicht' && <DossierOverview />}
          {activeTab === 'activiteiten' && <DossierActivitiesSection addButtonRef={addActivityRef} />}
          {activeTab === 'route' && <DossierRouteSection editorRef={routeEditorRef} />}
          {activeTab === 'goederen' && <DossierGoodsSection />}
          {activeTab === 'prijs' && <DossierPriceSection panelRef={pricePanelRef} />}
          {activeTab === 'documenten' && <DossierDocumentsSection />}
          {activeTab === 'historiek' && <DossierHistorySection />}
        </div>

        {showAddActivity && (
          <AddActivityDialog
            dossier={dossier}
            onClose={() => setShowAddActivity(false)}
            onAdded={(updated) => {
              applyDossier(updated)
              toast.showSuccess(t('dossiers.detail.activityAdded'))
            }}
            onConflict={handleConflict}
          />
        )}

        {drawerActivity && (
          <ActivityDrawer
            dossier={dossier}
            activity={drawerActivity}
            canManage={canManage && isOpen}
            onClose={() => setDrawerActivity(null)}
            onUpdated={(updated) => {
              applyDossier(updated)
              toast.showSuccess(t('dossiers.detail.activityUpdated'))
            }}
            onConflict={handleConflict}
          />
        )}

        {goodsDrawerOpen && firstOrder && (
          <GoodsDrawer
            order={firstOrder}
            onClose={() => setGoodsDrawerOpen(false)}
            onSaved={(updated) => {
              setLoadedOrder({ id: updated.id, order: updated })
              toast.showSuccess(t('dossiers.detail.goodsSaved'))
              load()
            }}
          />
        )}

        {editing && (
          <Modal
            title={t('dossiers.detail.editTitle')}
            onClose={() => setEditing(false)}
            busy={busy}
            footer={
              <>
                <Button variant="secondary" onClick={() => setEditing(false)} disabled={busy}>
                  {t('ui.actions.cancel')}
                </Button>
                <Button type="submit" form="edit-dossier-form" disabled={busy}>
                  {t('ui.actions.save')}
                </Button>
              </>
            }
          >
            <form id="edit-dossier-form" onSubmit={(event) => void submitEdit(event)}>
              <ValidationSummary message={formError} fieldErrors={fieldErrors} />
              <FormField label={t('dossiers.detail.titleField')} htmlFor="edit-title" required error={getFieldError(fieldErrors, 'title')}>
                <input id="edit-title" value={title} onChange={(event) => setTitle(event.target.value)} maxLength={200} />
              </FormField>
              <FormField label={t('dossiers.detail.customerReferenceField')} htmlFor="edit-ref" error={getFieldError(fieldErrors, 'customerReference')}>
                <input
                  id="edit-ref"
                  value={customerReference}
                  onChange={(event) => setCustomerReference(event.target.value)}
                  maxLength={100}
                />
              </FormField>
              <FormField label={t('dossiers.detail.dateField')} htmlFor="edit-date" error={getFieldError(fieldErrors, 'dossierDate')}>
                <input id="edit-date" type="date" value={dossierDate} onChange={(event) => setDossierDate(event.target.value)} />
              </FormField>
              <FormField label={t('dossiers.detail.descriptionField')} htmlFor="edit-description" error={getFieldError(fieldErrors, 'description')}>
                <textarea
                  id="edit-description"
                  value={description}
                  onChange={(event) => setDescription(event.target.value)}
                  rows={3}
                  maxLength={2000}
                />
              </FormField>
              {dossier.customerId || dossier.orders.length > 0 ? (
                <FormField label={t('dossiers.detail.customerField')} htmlFor="edit-customer-locked" hint={t('dossiers.customerChange.lockedHint')}>
                  <div className="dossier-customer-locked">
                    <span id="edit-customer-locked">{dossier.customerName ?? '—'}</span>
                    <Button
                      variant="secondary"
                      onClick={() => {
                        setEditing(false)
                        setCustomerChangeOpen(true)
                      }}
                      disabled={busy}
                    >
                      {t('dossiers.customerChange.action')}
                    </Button>
                  </div>
                </FormField>
              ) : (
                <FormField label={t('dossiers.detail.customerField')} htmlFor="edit-customer" error={getFieldError(fieldErrors, 'customerId')}>
                  <SearchableSelect id="edit-customer" value={customerId} onChange={setCustomerId} options={customerOptions} />
                </FormField>
              )}
              <FormField label={t('dossiers.detail.responsibleField')} htmlFor="edit-responsible" error={getFieldError(fieldErrors, 'responsibleUserId')}>
                <SearchableSelect
                  id="edit-responsible"
                  value={responsibleUserId}
                  onChange={setResponsibleUserId}
                  options={userOptions}
                />
              </FormField>
              <FormField label={t('dossiers.detail.notesField')} htmlFor="edit-notes" error={getFieldError(fieldErrors, 'notes')}>
                <textarea id="edit-notes" value={notes} onChange={(event) => setNotes(event.target.value)} rows={3} maxLength={4000} />
              </FormField>
            </form>
          </Modal>
        )}

        {customerChangeOpen && (
          <DossierCustomerChangeDialog
            dossier={dossier}
            onClose={() => setCustomerChangeOpen(false)}
            onChanged={(updated) => {
              applyDossier(updated)
              setCustomerChangeOpen(false)
              toast.showSuccess(t('dossiers.customerChange.changed'))
            }}
          />
        )}

        {showLinkOrder && (
          <LinkOrderDialog
            dossier={dossier}
            onClose={() => setShowLinkOrder(false)}
            onUpdated={(updated) => {
              applyDossier(updated)
              toast.showSuccess(t('dossiers.detail.orderLinked'))
            }}
          />
        )}

        {showAddRelation && (
          <AddRelationDialog
            dossier={dossier}
            onClose={() => setShowAddRelation(false)}
            onUpdated={(updated) => {
              applyDossier(updated)
              toast.showSuccess(t('dossiers.detail.relationAdded'))
            }}
          />
        )}

        {confirmClose && (
          <ConfirmDialog
            title={t('dossiers.detail.closeTitle')}
            message={t('dossiers.detail.closeMessage')}
            confirmLabel={t('dossiers.detail.closeConfirm')}
            busy={busy}
            onCancel={() => setConfirmClose(false)}
            onConfirm={() =>
              void run(() => closeDossier(id), t('dossiers.detail.closed')).then((ok) => {
                if (ok) setConfirmClose(false)
              })
            }
          />
        )}
      </div>
    </DossierWorkspaceContext.Provider>
  )
}
