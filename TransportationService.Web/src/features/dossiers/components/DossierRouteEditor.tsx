import { useEffect, useImperativeHandle, useRef, useState, type Ref } from 'react'
import { ApiError } from '../../../api/apiClient'
import { describeApiError } from '../../../api/problemDetails'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useToast } from '../../../components/ui/toastContext'
import { useLocale } from '../../../i18n/localeContext'
import { LocationQuickCreateDialog } from '../../locations/components/LocationQuickCreateDialog'
import type { LocationOption } from '../../locations/types'
import { getTransportOrder, updateTransportOrder } from '../../transport-orders/api/transportOrdersApi'
import type { CraneJobKind, TransportOrderDetail } from '../../transport-orders/types'
import { RouteSection } from '../../transport-orders/components/sections/RouteSection'
import { CraneJobKindField, OnSiteWorkFields } from '../../transport-orders/components/sections/CraneJobFields'
import { applyStopPatch } from '../../transport-orders/utils/plannedEnd'
import { ensureStopsForKind, kindSwitchDropsData, switchStopsToKind } from '../../transport-orders/components/sections/craneJobStops'
import { useLocationHours } from '../../transport-orders/components/sections/useOrderFormData'
import { listServiceOptions, type ServiceOption } from '../../tarification/api/pricingApi'
import { buildSubmitPayload } from '../../transport-orders/components/sections/orderFormPayload'
import {
  cargoFromOrder,
  craneJobFromOrder,
  emptyStop,
  fieldErrorMap,
  isEmptyStopRow,
  remapCargoStopIndices,
  stopsFromOrder,
  validateOrderForm,
  type CargoFormRow,
  type CraneJobFormValues,
  type StopFormRow,
} from '../../transport-orders/components/sections/orderFormState'
import { useStopMutation } from '../../transport-orders/components/sections/useStopMutation'
import { createOrderForActivity } from '../api/dossiersApi'
import type { DossierActivity, DossierDetail } from '../types'
import { orderValuesFromDetail } from './orderDrawerState'
import { DossierRouteSummary } from './DossierRouteSummary'
import '../../transport-orders/components/transport-order-form.css'

/**
 * Stop rows for the editor: the order's stops in sequence, plus a seeded loading row in front /
 * unloading row at the end when that stop type is still missing, so an incomplete route looks
 * ready to be completed. A seeded row left untouched is dropped again on save and re-seeded
 * from the saved order, so it never disappears from the planner's view; a row the planner ADDED
 * is never dropped without confirmation (see `requestSave`).
 */
function seedStops(order: TransportOrderDetail | null): StopFormRow[] {
  return seedRows(order && order.stops.length > 0 ? stopsFromOrder(order) : [], order?.craneJobKind === 'OnSiteLifting')
}

/**
 * D2: an on-site lifting job is ONE work-site stop — it gets a seeded "Werfadres" row when it has
 * none yet, and never a fictitious loading/unloading placeholder. Every other order keeps the
 * classic laad + los seeding.
 */
function seedRows(rows: StopFormRow[], onSite: boolean): StopFormRow[] {
  return ensureStopsForKind(rows, onSite, seededStop)
}

const seededStop = (stopType: StopFormRow['stopType']): StopFormRow => ({ ...emptyStop(stopType), seeded: true })

/** A seeded placeholder the planner never touched: not data, silently re-seeded after the save. */
function isUntouchedSeed(row: StopFormRow): boolean {
  return Boolean(row.seeded) && isEmptyStopRow(row)
}

/** A row the planner added and never touched: only dropped after explicit confirmation. */
function isUntouchedAdded(row: StopFormRow): boolean {
  return !row.seeded && isEmptyStopRow(row)
}

/**
 * Validation errors are computed on the SUBMITTED list (seeds dropped) but shown on the DISPLAYED
 * list, so `stops[i]` is re-indexed to the row's on-screen position — a dropped seed in front
 * would otherwise mark the wrong stop.
 */
function toDisplayedField(field: string, submitted: StopFormRow[], displayed: StopFormRow[]): string {
  return field.replace(/^stops\[(\d+)\]/, (match, index: string) => {
    const row = submitted[Number(index)]
    const position = row ? displayed.indexOf(row) : -1
    return position >= 0 ? `stops[${position}]` : match
  })
}

/** Statuses in which the backend still accepts a full-order update (stops included). */
const EDITABLE_ORDER_STATUSES = new Set(['Draft', 'Submitted', 'Confirmed'])

export interface DossierRouteEditorHandle {
  /**
   * Focuses the control that resolves a readiness field: "stops.loading" / "stops.unloading"
   * (first stop of that type without a location), "stops.plannedFrom" (first stop without a
   * date), anything else = first location field. Returns false when nothing could be focused.
   */
  focusField: (field: string | null) => boolean
}

interface DossierRouteEditorProps {
  dossier: DossierDetail
  /** First transport activity (hasStops) of the dossier; owns the linked order (or none yet). */
  activity: DossierActivity | null
  /** Linked order detail; null while loading or when the activity has no order yet. */
  order: TransportOrderDetail | null
  loading: boolean
  /** dossiers.manage AND orders.edit|orders.manage AND dossier open. */
  canEdit: boolean
  canCreateLocations: boolean
  onOrderSaved: (order: TransportOrderDetail) => void
  onDossierUpdated: (dossier: DossierDetail) => void
  /** Dossier-level 409 handling (create-order path); returns true when handled. */
  onConflict: (err: unknown) => boolean
  /** Reports the unsaved state so the host can lock order switching while edits are pending. */
  onDirtyChange?: (dirty: boolean) => void
  /** Re-fetches the linked order after a failed load (the editor keeps its entered state meanwhile). */
  onRetryLoad?: () => void
  ref?: Ref<DossierRouteEditorHandle>
}

/**
 * UX-sprint 2026-09-09: the route is fundamental transport data, so the loading and unloading
 * stops are editable directly on the dossier — no "Route bewerken" detour. Stops stay an
 * ORDERED collection on the linked order: the editor keeps every existing stop id and submits
 * the whole list through the same payload builder and version gate as the order form, so a
 * save can never drop a colleague's stop silently (409 → rebase banner).
 *
 * Save is explicit (collection-replacement semantics make autosave unsafe); the section shows
 * its own unsaved state and arms the navigation guard only while a change is pending.
 */
export function DossierRouteEditor({
  dossier,
  activity,
  order,
  loading,
  canEdit,
  canCreateLocations,
  onOrderSaved,
  onDossierUpdated,
  onConflict,
  onDirtyChange,
  onRetryLoad,
  ref,
}: DossierRouteEditorProps) {
  const { t } = useLocale()
  const toast = useToast()
  const rootRef = useRef<HTMLDivElement>(null)
  const [baseOrder, setBaseOrder] = useState<TransportOrderDetail | null>(order)
  const [stops, setStops] = useState<StopFormRow[]>(() => seedStops(order))
  const [cargoItems, setCargoItems] = useState<CargoFormRow[]>(() => cargoFromOrder(order ?? undefined))
  // D2: kind of crane job + the on-site work; edited here because it decides what the route IS.
  const [craneJob, setCraneJob] = useState<CraneJobFormValues>(() => craneJobFromOrder(order ?? undefined))
  // Kind switch that would drop stops holding data, awaiting confirmation.
  const [pendingKind, setPendingKind] = useState<CraneJobKind | null>(null)
  const [dirty, setDirty] = useState(false)
  const [saving, setSaving] = useState(false)
  const [creatingOrder, setCreatingOrder] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [conflict, setConflict] = useState<TransportOrderDetail | null>(null)
  const [refreshTarget, setRefreshTarget] = useState<string | null>(null)
  // Keys of added-but-untouched rows awaiting the "drop and save?" confirmation.
  const [emptyStopPrompt, setEmptyStopPrompt] = useState<string[] | null>(null)
  // Row with data (or persisted) whose Verwijderen awaits confirmation.
  const [removeTarget, setRemoveTarget] = useState<string | null>(null)
  const [quickCreate, setQuickCreate] = useState<{ name: string; resolve: (created: LocationOption | null) => void } | null>(null)
  const pendingFocus = useRef<string | null>(null)

  const customerId = order?.customerId ?? dossier.customerId ?? ''
  const locationHours = useLocationHours(stops)
  // Service options are needed to round-trip the order's service selections in the full PUT.
  const [serviceOptions, setServiceOptions] = useState<ServiceOption[]>([])
  useEffect(() => {
    let mounted = true
    listServiceOptions(false, true)
      .then((data) => {
        if (mounted) setServiceOptions(data)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])
  const mutateStops = useStopMutation(stops, setStops, setCargoItems, () => setDirty(true))

  const onDirtyChangeRef = useRef(onDirtyChange)
  useEffect(() => {
    onDirtyChangeRef.current = onDirtyChange
  })
  useEffect(() => {
    onDirtyChangeRef.current?.(dirty)
  }, [dirty])
  // Unmounting (section hidden, order switched away) means nothing is pending anymore.
  useEffect(() => () => onDirtyChangeRef.current?.(false), [])

  // A fresh order from the page (after a goods save, a reload, a create) replaces the baseline
  // — but never while the planner has unsaved edits here. Adjusted during render (React's
  // "state from previous render" pattern) so no stale frame is committed.
  const orderKey = order ? `${order.id}:${order.version}` : 'none'
  const baseKey = baseOrder ? `${baseOrder.id}:${baseOrder.version}` : 'none'
  if (orderKey !== baseKey && !dirty) {
    setBaseOrder(order)
    setStops(seedStops(order))
    setCargoItems(cargoFromOrder(order ?? undefined))
    setCraneJob(craneJobFromOrder(order ?? undefined))
    setErrors({})
  }

  // Capability FLAG of the activity's type (never a type code); an order that already is an
  // on-site job keeps its fields editable even when the flag is missing on an older payload.
  const onSite = craneJob.kind === 'OnSiteLifting'
  const supportsOnSiteWork = Boolean(baseOrder?.activitySupportsOnSiteWork ?? activity?.supportsOnSiteWork) || onSite
  // Planned duration lives on the ACTIVITY (edited there); here it only drives the automatic end.
  const durationHours = baseOrder?.activityDurationHours ?? activity?.durationHours ?? null

  function focusControl(id: string): boolean {
    const element = rootRef.current?.querySelector<HTMLElement>(`[id="${id}"]`)
    if (!element) return false
    element.focus()
    return true
  }

  function focusField(field: string | null): boolean {
    const firstUnresolved = (type: StopFormRow['stopType']) =>
      stops.find((s) => s.stopType === type && !s.locationId && !s.city.trim()) ?? stops.find((s) => s.stopType === type)
    // An on-site job has no loading/unloading stop to resolve: every route issue points at the work site.
    if (!onSite && (field === 'stops.loading' || field === 'stops.unloading')) {
      const type = field === 'stops.loading' ? 'Loading' : 'Unloading'
      const target = firstUnresolved(type)
      if (target) return focusControl(`st-loc-${target.key}`)
      pendingFocus.current = field
      mutateStops((rows) => [...rows, emptyStop(type)])
      return true
    }
    if (field === 'stops.plannedFrom') {
      const target = stops.find((s) => !s.date) ?? stops[0]
      return target ? focusControl(`st-date-${target.key}`) : false
    }
    const first = stops[0]
    return first ? focusControl(`st-loc-${first.key}`) : false
  }

  useImperativeHandle(ref, () => ({ focusField }))

  // A stop added by focusField renders one tick later; finish the focus once it exists.
  useEffect(() => {
    if (!pendingFocus.current) return
    const field = pendingFocus.current
    pendingFocus.current = null
    const type = field === 'stops.loading' ? 'Loading' : 'Unloading'
    const target = [...stops].reverse().find((s) => s.stopType === type)
    if (target) focusControl(`st-loc-${target.key}`)
  }, [stops])

  function setStop(key: string, patch: Partial<StopFormRow>) {
    // Site stops keep a predictable end (start + duration until the planner types their own).
    mutateStops((rows) => rows.map((row) => (row.key === key ? applyStopPatch(row, patch, durationHours) : row)))
  }

  /**
   * "Soort kraanopdracht". The two kinds have disjoint stop sets (work site vs. laden/lossen), so
   * a switch replaces the rows of the other kind — after confirmation when any of them holds
   * data. Nothing is sent until "Route opslaan"; "Verwerpen" restores the saved route.
   */
  function applyCraneJobKind(kind: CraneJobKind) {
    const toSite = kind === 'OnSiteLifting'
    mutateStops((rows) => switchStopsToKind(rows, toSite, seededStop))
    setCraneJob((job) => ({ ...job, kind }))
    setErrors({})
    setDirty(true)
  }

  function requestCraneJobKind(kind: CraneJobKind) {
    const toSite = kind === 'OnSiteLifting'
    if (toSite === onSite) return
    if (kindSwitchDropsData(stops, toSite)) setPendingKind(kind)
    else applyCraneJobKind(kind)
  }

  function moveStop(index: number, delta: number) {
    mutateStops((rows) => {
      const target = index + delta
      if (target < 0 || target >= rows.length) return rows
      const next = [...rows]
      ;[next[index], next[target]] = [next[target], next[index]]
      return next
    })
  }

  function removeStop(key: string) {
    mutateStops((rows) => rows.filter((row) => row.key !== key))
  }

  /**
   * Verwijderen: an untouched row (seeded or just added) goes immediately; a row that holds
   * data — every persisted stop included — is only removed after confirmation, so a mis-click
   * can never take real route data along.
   */
  function requestRemoveStop(key: string) {
    const row = stops.find((candidate) => candidate.key === key)
    if (!row) return
    if (isEmptyStopRow(row)) {
      removeStop(key)
      return
    }
    setRemoveTarget(key)
  }

  function discard() {
    setEmptyStopPrompt(null)
    setRemoveTarget(null)
    setStops(seedStops(baseOrder))
    setCargoItems(cargoFromOrder(baseOrder ?? undefined))
    setCraneJob(craneJobFromOrder(baseOrder ?? undefined))
    setPendingKind(null)
    setErrors({})
    setError(null)
    setDirty(false)
  }

  /**
   * Resolves the order the stops are saved to. Two-step flow for an activity without order:
   * create, then PUT the stops. Both halves are retry-safe: once created, the order id lives in
   * `baseOrder` AND in the refreshed activity (`linkedTransportOrderId`), so a retry after a
   * failed stop save — or after a failed follow-up load — re-uses that order and never creates
   * a second transport order for the same activity (the backend refuses that too).
   */
  async function ensureOrder(): Promise<TransportOrderDetail | null> {
    if (baseOrder) return baseOrder
    if (!activity) return null
    setCreatingOrder(true)
    try {
      let orderId = activity.linkedTransportOrderId ?? order?.id ?? null
      if (!orderId) {
        const updatedDossier = await createOrderForActivity(dossier.id, activity.id, dossier.version)
        onDossierUpdated(updatedDossier)
        orderId = updatedDossier.activities.find((a) => a.id === activity.id)?.linkedTransportOrderId ?? null
        if (!orderId) return null
      }
      const created = await getTransportOrder(orderId)
      setBaseOrder(created)
      return created
    } catch (err) {
      if (!onConflict(err)) setError(describeApiError(err, t('dossierSheet.route.createOrderFailed')).message)
      return null
    } finally {
      setCreatingOrder(false)
    }
  }

  /**
   * Route opslaan. A stop the planner added and left blank is never dropped silently: the save
   * first asks whether to remove it (one dialog for all of them). Rows with any entered value
   * are submitted and fail the location-or-place rule inline instead of vanishing.
   */
  function requestSave() {
    const untouched = stops.filter(isUntouchedAdded).map((row) => row.key)
    if (untouched.length > 0) {
      setEmptyStopPrompt(untouched)
      return
    }
    void save(stops, cargoItems)
  }

  /** "Lege stop(s) verwijderen & opslaan": drop the confirmed rows from the editor, then save the rest. */
  function confirmDropEmptyStops() {
    if (!emptyStopPrompt) return
    const drop = new Set(emptyStopPrompt)
    const remaining = stops.filter((row) => !drop.has(row.key))
    const remainingCargo = remapCargoStopIndices(cargoItems, stops, remaining)
    mutateStops(() => remaining)
    setEmptyStopPrompt(null)
    void save(remaining, remainingCargo)
  }

  async function save(rows: StopFormRow[], cargo: CargoFormRow[]) {
    // Untouched seeded placeholders are not data: drop them (they are seeded again from the saved
    // order) and keep the goods links pointing at the same stops (the backend links by position).
    // Every other row is submitted as-is.
    const effectiveStops = rows.filter((stop) => !isUntouchedSeed(stop))
    const effectiveCargo = remapCargoStopIndices(cargo, rows, effectiveStops)
    setSaving(true)
    setError(null)
    try {
      const target = await ensureOrder()
      if (!target) return
      const values = {
        ...orderValuesFromDetail(target, serviceOptions),
        stops: effectiveStops,
        cargoItems: effectiveCargo,
        // Only an editor that offers the choice owns the crane-job fields; otherwise they are not
        // sent and the server keeps kind, description and lift data as stored.
        craneJob: supportsOnSiteWork ? craneJob : undefined,
      }
      const routeErrors = validateOrderForm(values).filter((e) => e.section === 'route')
      setErrors(fieldErrorMap(routeErrors.map((e) => ({ ...e, field: toDisplayedField(e.field, effectiveStops, rows) }))))
      if (routeErrors.length > 0) {
        setError(t('dossiers.orderDrawer.checkStops'))
        return
      }
      const updated = await updateTransportOrder(target.id, buildSubmitPayload(values))
      setDirty(false)
      setBaseOrder(updated)
      setStops(seedStops(updated))
      setCargoItems(cargoFromOrder(updated))
      setCraneJob(craneJobFromOrder(updated))
      setErrors({})
      toast.showSuccess(t('dossierSheet.route.saved'))
      onOrderSaved(updated)
    } catch (err) {
      if (err instanceof ApiError && err.status === 409 && err.body && typeof err.body === 'object' && 'stops' in err.body) {
        setConflict(err.body as TransportOrderDetail)
      } else {
        // No success toast on a failure — and a refusal that names a stop (e.g. storing its
        // address in the address book) is shown ON that stop, re-indexed to the displayed rows.
        const described = describeApiError(err, t('dossierSheet.route.saveFailed'))
        setError(described.message)
        setErrors(
          Object.fromEntries(
            Object.entries(described.fieldErrors)
              .filter(([, messages]) => messages.length > 0)
              .map(([field, messages]) => [toDisplayedField(field, effectiveStops, rows), messages[0]]),
          ),
        )
      }
    } finally {
      setSaving(false)
    }
  }

  function reloadFromConflict() {
    if (!conflict) return
    setBaseOrder(conflict)
    setStops(seedStops(conflict))
    setCargoItems(cargoFromOrder(conflict))
    setCraneJob(craneJobFromOrder(conflict))
    setDirty(false)
    setConflict(null)
    setError(null)
    onOrderSaved(conflict)
  }

  if (loading) return <p className="placeholder-text">{t('dossiers.route.loading')}</p>
  if (activity?.linkedTransportOrderId && !order) {
    return (
      <div className="dossier-route-readonly">
        <p className="placeholder-text">{t('dossiers.route.loadFailed')}</p>
        {onRetryLoad && (
          <Button variant="secondary" onClick={onRetryLoad}>
            {t('dossierSheet.route.retryLoad')}
          </Button>
        )}
      </div>
    )
  }

  const statusEditable = !order || EDITABLE_ORDER_STATUSES.has(order.status)
  if (!canEdit || !statusEditable) {
    return (
      <div className="dossier-route-readonly">
        <DossierRouteSummary order={order} loading={false} canEdit={false} onEdit={() => undefined} />
        <p className="placeholder-text">{!canEdit ? t('dossierSheet.route.readOnlyRights') : t('dossierSheet.route.readOnlyStatus')}</p>
      </div>
    )
  }

  const busy = saving || creatingOrder
  return (
    <div className="dossier-route-editor" ref={rootRef}>
      <UnsavedChangesGuard when={dirty && !busy} />
      {conflict && (
        <div className="dossier-conflict-banner" role="alert">
          <span>{t('dossiers.orderDrawer.conflict')}</span>
          <Button variant="secondary" onClick={reloadFromConflict}>
            {t('dossiers.orderDrawer.reload')}
          </Button>
        </div>
      )}
      <ValidationSummary message={error} />
      <p className="dossier-route-hint">{onSite ? t('stopEditor.crane.onSiteHint') : t('dossierSheet.route.hint')}</p>
      <div className="tof dossier-route-sheet">
        {supportsOnSiteWork && (
          <CraneJobKindField idPrefix="dre" kind={craneJob.kind} onChange={requestCraneJobKind} disabled={busy} />
        )}
        <RouteSection
          stops={stops}
          canSaveToAddressBook={canCreateLocations && Boolean(customerId)}
          siteDurationHours={durationHours}
          customerId={customerId}
          saving={busy}
          locationHours={locationHours}
          errors={errors}
          onAddStop={(stopType) => mutateStops((rows) => [...rows, emptyStop(stopType)])}
          setStop={setStop}
          moveStop={moveStop}
          onRemoveStop={requestRemoveStop}
          onRequestRefresh={setRefreshTarget}
          onQuickCreate={
            canCreateLocations && customerId
              ? (name) => new Promise<LocationOption | null>((resolve) => setQuickCreate({ name, resolve }))
              : undefined
          }
          sheet
          hideHeader
        />
        {onSite && (
          <OnSiteWorkFields
            idPrefix="dre"
            job={craneJob}
            onChange={(patch) => {
              setCraneJob((job) => ({ ...job, ...patch }))
              setDirty(true)
            }}
            disabled={busy}
            descriptionError={errors.workDescription}
            duration={
              <p className="dossier-route-hint">
                {durationHours !== null
                  ? t('stopEditor.crane.durationReadOnly', { hours: String(durationHours).replace('.', ',') })
                  : t('stopEditor.crane.durationUnknown')}
              </p>
            }
          />
        )}
      </div>
      <div className="dossier-route-actions">
        <div className="dossier-route-add">
          {/* An on-site job is one work site: no loading/unloading stops can be added to it. */}
          {!onSite && (
            <>
              <Button variant="secondary" onClick={() => mutateStops((rows) => [...rows, emptyStop('Loading')])} disabled={busy}>
                {t('dossierSheet.route.addLoading')}
              </Button>
              <Button variant="secondary" onClick={() => mutateStops((rows) => [...rows, emptyStop('Unloading')])} disabled={busy}>
                {t('dossierSheet.route.addUnloading')}
              </Button>
            </>
          )}
        </div>
        <div className="dossier-route-save">
          {creatingOrder && <span className="dossier-route-state">{t('dossierSheet.route.creatingOrder')}</span>}
          {!creatingOrder && dirty && <span className="dossier-route-state dossier-route-state-dirty">{t('dossierSheet.route.unsaved')}</span>}
          <Button variant="ghost" onClick={discard} disabled={!dirty || busy}>
            {t('dossierSheet.route.discard')}
          </Button>
          <Button onClick={requestSave} disabled={!dirty || busy}>
            {t('dossierSheet.route.save')}
          </Button>
        </div>
      </div>

      {quickCreate && customerId && (
        <LocationQuickCreateDialog
          customerId={customerId}
          initialName={quickCreate.name}
          onClose={(created) => {
            quickCreate.resolve(created)
            setQuickCreate(null)
          }}
        />
      )}

      {refreshTarget && (
        <ConfirmDialog
          title={t('transportOrders.form.refreshTitle')}
          message={t('transportOrders.form.refreshMessage')}
          confirmLabel={t('transportOrders.form.refreshConfirm')}
          onConfirm={() => {
            // The re-copy also ends a deliberate deviation from the address book (D3).
            setStop(refreshTarget, { refreshSnapshot: true, addressOverridden: false })
            setRefreshTarget(null)
          }}
          onCancel={() => setRefreshTarget(null)}
        />
      )}

      {pendingKind && (
        <ConfirmDialog
          title={t('stopEditor.crane.switchTitle')}
          message={t('stopEditor.crane.switchMessage')}
          confirmLabel={t('stopEditor.crane.switchConfirm')}
          destructive
          onConfirm={() => {
            applyCraneJobKind(pendingKind)
            setPendingKind(null)
          }}
          onCancel={() => setPendingKind(null)}
        />
      )}

      {emptyStopPrompt && (
        <ConfirmDialog
          title={t('dossierSheet.route.emptyStopsTitle', { count: emptyStopPrompt.length })}
          message={t('dossierSheet.route.emptyStopsMessage', { count: emptyStopPrompt.length })}
          confirmLabel={t('dossierSheet.route.emptyStopsConfirm', { count: emptyStopPrompt.length })}
          cancelLabel={t('dossierSheet.route.emptyStopsBack')}
          destructive
          onConfirm={confirmDropEmptyStops}
          onCancel={() => setEmptyStopPrompt(null)}
        />
      )}

      {removeTarget && (
        <ConfirmDialog
          title={t('dossierSheet.route.removeStopTitle')}
          message={t('dossierSheet.route.removeStopMessage', { number: stops.findIndex((row) => row.key === removeTarget) + 1 })}
          confirmLabel={t('dossierSheet.route.removeStopConfirm')}
          destructive
          onConfirm={() => {
            removeStop(removeTarget)
            setRemoveTarget(null)
          }}
          onCancel={() => setRemoveTarget(null)}
        />
      )}
    </div>
  )
}
