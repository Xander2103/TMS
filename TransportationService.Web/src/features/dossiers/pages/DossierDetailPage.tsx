import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
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
import { euro } from '../../invoices/types'
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
import { type DossierActivity, type DossierDetail, type ReadinessSection } from '../types'
import { ActivityDrawer } from '../components/ActivityDrawer'
import { ActivityList } from '../components/ActivityList'
import { AddActivityDialog } from '../components/AddActivityDialog'
import { AttentionPanel } from '../components/AttentionPanel'
import { DossierHeader, type DossierMenuAction } from '../components/DossierHeader'
import { DossierGoodsSummary } from '../components/DossierGoodsSummary'
import { AddRelationDialog, LinkOrderDialog } from '../components/DossierLinkDialogs'
import { DossierMoreSection } from '../components/DossierMoreSection'
import { DossierPriceSummary } from '../components/DossierPriceSummary'
import { DossierRouteSummary } from '../components/DossierRouteSummary'
import { GoodsDrawer } from '../components/GoodsDrawer'
import { RouteDrawer } from '../components/RouteDrawer'
import { formatDate as formatIsoDate } from '../../../utils/dates'
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

/** §11 dossierpagina: kop, aandacht, activiteiten, contextuele secties en drawers. */
export function DossierDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { t } = useLocale()
  const toast = useToast()
  const { hasPermission } = useAuth()
  const canManage = hasPermission('dossiers.manage')

  const [dossier, setDossier] = useState<DossierDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  /** 409-payload van een collega-wijziging; [Herladen] neemt deze staat over. */
  const [conflict, setConflict] = useState<DossierDetail | null>(null)

  // First linked order (drives Route/Goederen/Prijs summaries + drawers), keyed by order id
  // so switching orders never shows stale data and the effect stays callback-only.
  const [loadedOrder, setLoadedOrder] = useState<{ id: string; order: TransportOrderDetail | null } | null>(null)

  // Dialogs & drawers
  const [showAddActivity, setShowAddActivity] = useState(false)
  const [drawerActivity, setDrawerActivity] = useState<DossierActivity | null>(null)
  const [routeDrawerOpen, setRouteDrawerOpen] = useState(false)
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

  const activities = dossier?.activities ?? []
  const firstLinkedOrderId =
    [...activities].sort((a, b) => a.sequence - b.sequence).find((a) => a.hasStops && a.linkedTransportOrderId)
      ?.linkedTransportOrderId ?? null

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
  }, [firstLinkedOrderId, dossier?.version])

  const firstOrder = firstLinkedOrderId && loadedOrder?.id === firstLinkedOrderId ? loadedOrder.order : null
  const firstOrderLoading = Boolean(firstLinkedOrderId) && loadedOrder?.id !== firstLinkedOrderId

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

  const isOpen = dossier.status === 'Open'
  const hasRoute = activities.some((a) => a.hasStops)
  const hasGoods = activities.some((a) => a.supportsGoods)
  // Compat: opdrachten die (nog) niet door een activiteit vertegenwoordigd worden.
  const activityOrderIds = new Set(activities.map((a) => a.linkedTransportOrderId).filter(Boolean))
  const legacyOrders = dossier.orders.filter((o) => !activityOrderIds.has(o.orderId))

  function goToSection(section: ReadinessSection) {
    document.getElementById(`sectie-${section}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
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
    onSelect: () => document.getElementById('sectie-notities')?.scrollIntoView({ behavior: 'smooth', block: 'start' }),
  })

  return (
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

      <section id="sectie-activiteiten" className="dossier-section" aria-label={t('dossiers.detail.activitiesTitle')}>
        <h2>{t('dossiers.detail.activitiesTitle')}</h2>
        <ActivityList
          activities={activities}
          canManage={canManage && isOpen}
          onOpen={openActivity}
          onAdd={() => setShowAddActivity(true)}
        />
        {legacyOrders.length > 0 && (
          <div className="dossier-legacy-orders">
            <h3>{t('dossiers.detail.linkedOrders')}</h3>
            <ul className="db-list">
              {legacyOrders.map((order) => (
                <li key={order.linkId}>
                  <span className="db-row">
                    <button type="button" className="link-button" onClick={() => navigate(`/transport-orders/${order.orderId}`)}>
                      <code>{order.orderNumber}</code>
                    </button>
                    <span className="db-row-main" title={order.goodsDescription ?? undefined}>
                      {order.orderDate} · {order.status}
                      {order.agreedPrice !== null && <> · {euro(order.agreedPrice)}</>}
                    </span>
                    {canManage && isOpen && (
                      <Button
                        variant="secondary"
                        onClick={() => void run(() => unlinkDossierOrder(id, order.orderId), t('dossiers.detail.unlinked'))}
                        disabled={busy}
                      >
                        {t('dossiers.detail.unlink')}
                      </Button>
                    )}
                  </span>
                </li>
              ))}
            </ul>
          </div>
        )}
      </section>

      {hasRoute && (
        <section id="sectie-route" className="dossier-section" aria-label={t('dossiers.detail.routeTitle')}>
          <h2>{t('dossiers.detail.routeTitle')}</h2>
          {firstLinkedOrderId ? (
            <DossierRouteSummary
              order={firstOrder}
              loading={firstOrderLoading}
              canEdit={canManage && isOpen}
              onEdit={() => setRouteDrawerOpen(true)}
            />
          ) : (
            <p className="placeholder-text">{t('dossiers.detail.routeNoOrder')}</p>
          )}
        </section>
      )}

      {hasGoods && (
        <section id="sectie-goederen" className="dossier-section" aria-label={t('dossiers.detail.goodsTitle')}>
          <h2>{t('dossiers.detail.goodsTitle')}</h2>
          {firstLinkedOrderId ? (
            <DossierGoodsSummary
              order={firstOrder}
              loading={firstOrderLoading}
              canEdit={canManage && isOpen}
              onEdit={() => setGoodsDrawerOpen(true)}
            />
          ) : (
            <p className="placeholder-text">{t('dossiers.detail.goodsOnOrder')}</p>
          )}
        </section>
      )}

      <section id="sectie-prijs" className="dossier-section" aria-label={t('dossiers.detail.priceAria')}>
        <h2>{t('dossiers.detail.priceTitle')}</h2>
        <DossierPriceSummary dossier={dossier} />
      </section>

      <details className="dossier-collapsed" id="sectie-documenten">
        <summary>{t('dossiers.detail.documentsTitle')}</summary>
        {firstLinkedOrderId ? (
          <p>
            {t('dossiers.detail.documentsOnOrder')}{' '}
            <button type="button" className="link-button" onClick={() => navigate(`/transport-orders/${firstLinkedOrderId}`)}>
              {t('dossiers.detail.toOrder')}
            </button>
          </p>
        ) : (
          <p className="placeholder-text">{t('dossiers.detail.documentsNone')}</p>
        )}
      </details>

      <details className="dossier-collapsed" id="sectie-notities">
        <summary>{t('dossiers.detail.notesTitle')}</summary>
        {dossier.description && <p>{dossier.description}</p>}
        {dossier.notes ? <p>{dossier.notes}</p> : <p className="placeholder-text">{t('dossiers.detail.noNotes')}</p>}
        <p className="placeholder-text">
          {t('dossiers.detail.createdAt', { date: formatIsoDate(dossier.createdAt) })}
          {dossier.responsibleName && <> · {t('dossiers.detail.responsible', { name: dossier.responsibleName })}</>}
          {dossier.closedAt && <> · {t('dossiers.detail.closedAt', { date: formatIsoDate(dossier.closedAt) })}</>}
        </p>
      </details>

      <DossierMoreSection
        dossier={dossier}
        canManage={canManage}
        busy={busy}
        onRemoveRelation={(relationId) => void run(() => removeDossierRelation(id, relationId), t('dossiers.detail.relationRemoved'))}
      />

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

      {routeDrawerOpen && firstOrder && (
        <RouteDrawer
          order={firstOrder}
          onClose={() => setRouteDrawerOpen(false)}
          onSaved={(updated) => {
            setLoadedOrder({ id: updated.id, order: updated })
            toast.showSuccess(t('dossiers.detail.routeSaved'))
            load()
          }}
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
            <FormField label={t('dossiers.detail.customerField')} htmlFor="edit-customer" error={getFieldError(fieldErrors, 'customerId')}>
              <SearchableSelect id="edit-customer" value={customerId} onChange={setCustomerId} options={customerOptions} />
            </FormField>
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
  )
}
