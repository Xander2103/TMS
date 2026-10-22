import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { BackButton } from '../../../components/ui/BackButton'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { SearchableSelect, type SearchableSelectOption } from '../../../components/ui/SearchableSelect'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { SectionedForm, type SectionDef } from '../../../components/ui/SectionedForm'
import { useSectionNavigation } from '../../../components/ui/useSectionNavigation'
import { UnsavedChangesGuard } from '../../../components/ui/UnsavedChangesGuard'
import { NewDossierReview, type NewDossierSectionId } from '../components/NewDossierReview'
import { cargoWeightTotalsFromRows } from '../../transport-orders/components/sections/cargoWeight'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { listActivityTypes, type ActivityType } from '../api/activityTypesApi'
import { activityTypeIcon, DEFAULT_ACTIVITY_TYPE_ICON } from '../activityTypeIcons'
import { createDossierFast } from '../api/dossiersApi'
import { createTransportOrder } from '../../transport-orders/api/transportOrdersApi'
import { RouteSection } from '../../transport-orders/components/sections/RouteSection'
import { CraneJobKindField, OnSiteWorkFields } from '../../transport-orders/components/sections/CraneJobFields'
import { applyStopPatch, parseDurationHours, withAutoPlannedEnd } from '../../transport-orders/utils/plannedEnd'
import type { CraneJobKind } from '../../transport-orders/types'
import { GoodsSection } from '../../transport-orders/components/sections/GoodsSection'
import { useOrderFormData } from '../../transport-orders/components/sections/useOrderFormData'
import { useStopMutation } from '../../transport-orders/components/sections/useStopMutation'
import { buildSubmitPayload } from '../../transport-orders/components/sections/orderFormPayload'
import { useUnitTypes } from '../../master-data/hooks/useUnitTypes'
import { LocationQuickCreateDialog } from '../../locations/components/LocationQuickCreateDialog'
import { CustomerQuickCreateDialog } from '../../customers/components/CustomerQuickCreateDialog'
import type { CustomerDetail } from '../../customers/types'
import { searchActiveCustomerOptions } from '../../customers/api/customerOptions'
import type { LocationOption } from '../../locations/types'
import {
  applyUnitToCargoRow,
  cargoRowFromHeader,
  computeCargoSummary,
  duplicateCargoRowInList,
  emptyCargoRow,
  emptyCraneJob,
  emptyStop,
  fieldErrorMap,
  isCraneJobTouched,
  isEmptyCargoRow,
  isEmptyStopRow,
  remapCargoStopIndices,
  validateOrderForm,
  type CargoFormRow,
  type CraneJobFormValues,
  type OrderFormValidationError,
  type OrderFormValues,
  type StopFormRow,
} from '../../transport-orders/components/sections/orderFormState'
import './new-dossier.css'
import { todayLocalIsoDate } from '../../../utils/dates'

/**
 * One-page transport intake (2026-09 rework): klant → transporttype → the full operational
 * intake (route, planning, goederen) on the SAME page — one "Dossier aanmaken" click creates
 * dossier + order + stops + goods atomically via POST /api/transport-orders (auto-wrap).
 *
 * The fast path survives: klant + type with an untouched route/goods section submits exactly
 * what the old 4-field page submitted (dossier + first activity, no order yet); "Blanco
 * dossier" stays available as a quiet escape hatch below the type tiles.
 */
export function NewDossierPage() {
  const navigate = useNavigate()
  const { t } = useLocale()
  const toast = useToast()
  const { hasPermission } = useAuth()
  const canCreateOrders = hasPermission('orders.create') || hasPermission('orders.manage')
  const canCreateLocations = hasPermission('locations.create')

  const [customerId, setCustomerId] = useState<string | null>(null)
  const [customerReference, setCustomerReference] = useState('')
  const [dossierDate, setDossierDate] = useState(() => todayLocalIsoDate())
  const [quickStartTypes, setQuickStartTypes] = useState<ActivityType[]>([])
  const [typesFailed, setTypesFailed] = useState(false)
  /** 'blanco' = escape hatch; otherwise the selected quick-start type id; null = nothing chosen yet. */
  const [selection, setSelection] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [clientErrors, setClientErrors] = useState<OrderFormValidationError[]>([])
  /** Type switch that would discard filled extra stops, awaiting confirmation. */
  const [pendingSwitch, setPendingSwitch] = useState<{ nextSelection: string; dropKeys: string[] } | null>(null)
  /** Submit of a non-transport type while route/goods carry data, awaiting confirmation. */
  const [pendingDiscardSubmit, setPendingDiscardSubmit] = useState(false)

  // --- Transport intake state (order-shaped; only submitted for hasStops types) ---
  const [goodsDescription, setGoodsDescription] = useState('')
  const [notes, setNotes] = useState('')
  /** Kraantransport (AllowsDuration types): DurationHours for the wrapper ACTIVITY — never the order. */
  const [durationHours, setDurationHours] = useState('')
  const [quantity, setQuantity] = useState('')
  const [quantityUnitCode, setQuantityUnitCode] = useState<string | null>(null)
  const [weightKg, setWeightKg] = useState('')
  const [volumeM3, setVolumeM3] = useState('')
  const [palletCount, setPalletCount] = useState('')
  const [distanceKm, setDistanceKm] = useState('')
  const [loadingMeters, setLoadingMeters] = useState('')
  const [adrRequired, setAdrRequired] = useState(false)
  const [craneRequired, setCraneRequired] = useState(false)
  /** Kraantransport implies the crane flag; once the user touches the checkbox it is theirs. */
  const [craneTouched, setCraneTouched] = useState(false)
  const [plateauRequired, setPlateauRequired] = useState(false)
  const [moffettRequired, setMoffettRequired] = useState(false)
  const [isReturnMovement, setIsReturnMovement] = useState(false)
  const [stops, setStops] = useState<StopFormRow[]>(() => [emptyStop('Loading'), emptyStop('Unloading')])
  // The intake starts with one goods line: lines are the pricing source of truth, so the
  // legacy header-inputs-vs-lines duality never appears on a brand new dossier.
  const [cargoItems, setCargoItems] = useState<CargoFormRow[]>(() => [emptyCargoRow()])
  const mutateStops = useStopMutation(stops, setStops, setCargoItems)
  // D2 — hijswerk op locatie: ONE work-site stop with its own list, so switching the kind of
  // crane job back and forth never touches (or loses) the loading/unloading stops. Goods lines
  // never link to a site stop, hence the inert cargo setter.
  const [craneJob, setCraneJob] = useState<CraneJobFormValues>(() => emptyCraneJob('TransportWithCrane'))
  const [siteStops, setSiteStops] = useState<StopFormRow[]>(() => [emptyStop('Site')])
  const mutateSiteStops = useStopMutation(siteStops, setSiteStops, () => undefined)
  const [quickCreate, setQuickCreate] = useState<{ name: string; resolve: (created: LocationOption | null) => void } | null>(null)
  // "+ Nieuwe klant" without leaving the dossier: the dialog resolves the pending promise of the
  // customer combobox (or of the standalone action) with the created customer, which is then
  // selected at once and kept in the option list — the host form state is never touched.
  const [customerQuickCreate, setCustomerQuickCreate] = useState<{ name: string; resolve: (created: CustomerDetail | null) => void } | null>(null)
  const [createdCustomers, setCreatedCustomers] = useState<CustomerDetail[]>([])

  const { serviceOptions, unitMaster, locationHours, customerConfig, customerRequirements } = useOrderFormData(customerId ?? '', stops)
  const units = useUnitTypes()
  const preferredUnits = customerConfig?.preferredUnits ?? []
  // PERF-H3: the picker searches the server per keystroke (name, customer number, VAT); the
  // one-shot list of 200 made every later customer unpickable. Customers created from this page
  // are the only locally known options (so a brand-new one is named before any search).
  const customerOptions = useMemo<SearchableSelectOption[]>(
    () => createdCustomers.map((c) => ({ value: c.id, label: c.name, meta: c.customerNumber, keywords: c.customerNumber ?? undefined })),
    [createdCustomers],
  )
  // A customer created a moment ago is listed with the server's hits even before the search index
  // knows it (and never twice).
  const searchCustomersWithCreated = useMemo(
    () => async (query: string, signal: AbortSignal): Promise<SearchableSelectOption[]> => {
      const hits = await searchActiveCustomerOptions(query, signal)
      const needle = query.trim().toLowerCase()
      const created = customerOptions.filter(
        (option) =>
          !hits.some((hit) => hit.value === option.value) &&
          (!needle || `${option.label} ${option.keywords ?? ''}`.toLowerCase().includes(needle)),
      )
      return [...created, ...hits]
    },
    [customerOptions],
  )
  const canCreateCustomers = hasPermission('customers.create')
  const selectedCustomerIsNew = customerId !== null && createdCustomers.some((c) => c.id === customerId)

  useEffect(() => {
    listActivityTypes()
      .then((types) => {
        setQuickStartTypes(
          types
            .filter((t) => t.isActive && t.isQuickStart)
            .sort((a, b) => a.quickStartOrder - b.quickStartOrder || a.sortOrder - b.sortOrder),
        )
        setTypesFailed(false)
      })
      .catch(() => {
        setQuickStartTypes([])
        setTypesFailed(true)
      })
    // Autofocus the customer combobox: picking the klant is always the first step.
    document.getElementById('nd-klant')?.focus()
  }, [])

  const selectedType = selection && selection !== 'blanco' ? quickStartTypes.find((type) => type.id === selection) ?? null : null
  const isBlanco = selection === 'blanco'
  // The rich intake needs the order-create permission (the atomic create runs through the
  // order API); without it every type falls back to the classic dossier-only create.
  const showTransportIntake = Boolean(selectedType?.hasStops && canCreateOrders)
  const isDistribution = showTransportIntake && (selectedType?.code === 'DISTRIBUTIE')
  // Domain-driven, not hardcoded to a type code: Kraantransport is the seeded HasStops type
  // with AllowsDuration; the wrapper activity carries the DurationHours.
  const showDuration = showTransportIntake && Boolean(selectedType?.allowsDuration)
  const durationValue = showDuration && durationHours.trim() !== '' ? Number(durationHours.replace(',', '.')) : null
  const durationInvalid = durationValue !== null && (!Number.isFinite(durationValue) || durationValue < 0)
  // Capability flag of the activity type — never a type code — decides whether the kind of crane
  // job can be chosen at all.
  const supportsOnSiteWork = showTransportIntake && Boolean(selectedType?.supportsOnSiteWork)
  const onSite = supportsOnSiteWork && craneJob.kind === 'OnSiteLifting'

  const routeTouched = stops.some((stop) => !isEmptyStopRow(stop))
  // Every order field the intake renders counts: anything the user typed or ticked must go
  // through the order create, never silently through the dossier-only fast path.
  const craneImplied = selectedType?.code === 'KRAANTRANSPORT'
  // Goederen = what is transported; Vereisten = what is needed to move it (+ remarks). Both feed
  // the one "intake touched" rule below, and each drives its own section indicator.
  const goodsOnlyTouched =
    cargoItems.some((cargo) => !isEmptyCargoRow(cargo)) || goodsDescription.trim() !== '' ||
    quantity.trim() !== '' || quantityUnitCode !== null ||
    weightKg.trim() !== '' || volumeM3.trim() !== '' || palletCount.trim() !== '' ||
    distanceKm.trim() !== '' || loadingMeters.trim() !== ''
  const requirementsTouched = adrRequired || plateauRequired || moffettRequired || isReturnMovement || craneRequired !== craneImplied
  const goodsTouched = goodsOnlyTouched || notes.trim() !== '' || requirementsTouched
  const siteTouched = siteStops.some((stop) => !isEmptyStopRow(stop)) || isCraneJobTouched(craneJob)
  // An on-site job has no route and no goods of its own: only the site, the work and the duration count.
  const intakeTouched = onSite ? siteTouched || durationValue !== null : routeTouched || goodsTouched || durationValue !== null

  const derivedFromCargo = cargoItems.length > 0
  const cargoSummary = derivedFromCargo ? computeCargoSummary(cargoItems, units.displayUnits) : null
  // Order-entry units only; a chosen code that became unavailable stays visible as "(inactief)".
  const unitOptions = units.orderPickerOptions([quantityUnitCode, ...cargoItems.map((c) => c.quantityUnitCode)])

  const values: OrderFormValues = useMemo(
    () => ({
      customerId: customerId ?? '',
      customerReference,
      orderDate: dossierDate,
      goodsDescription,
      quantity,
      quantityUnit: '',
      quantityUnitCode,
      weightKg,
      volumeM3,
      palletCount,
      distanceKm,
      loadingMeters,
      adrRequired,
      craneRequired,
      plateauRequired,
      moffettRequired,
      isReturnMovement,
      agreedPrice: '',
      notes,
      legalEntityId: '',
      dieselSurchargeOverride: false,
      dieselSurchargePercentOverride: '',
      dieselSurchargeOverrideReason: '',
      stops,
      cargoItems,
      serviceOptions,
      selectedServiceOptionIds: [],
      serviceQuantities: {},
      servicePallets: {},
      serviceDays: {},
      serviceNotes: {},
      priceIsManual: false,
      priceOverrideReason: '',
      pricingSource: 'Contract',
      oneOffFixedAmount: '',
      oneOffTimeMode: 'none',
      oneOffIncludedLoadingMinutes: '',
      oneOffIncludedUnloadingMinutes: '',
      oneOffIncludedCombinedMinutes: '',
      oneOffExtraHourlyRate: '',
      oneOffNotes: '',
      includedLoadingMinutesOverride: '',
      includedUnloadingMinutesOverride: '',
      extraTimeHourlyRateOverride: '',
      extraTimeRoundingStepMinutes: '',
      extraTimeMinimumBillableMinutes: '',
      // Only a type with the on-site capability owns the crane-job fields; every other type
      // submits nothing about them (server default: None).
      craneJob: supportsOnSiteWork ? craneJob : undefined,
    }),
    [
      customerId, customerReference, dossierDate, goodsDescription, quantity, quantityUnitCode,
      weightKg, volumeM3, palletCount, distanceKm, loadingMeters, adrRequired, craneRequired,
      plateauRequired, moffettRequired, isReturnMovement, notes, stops, cargoItems, serviceOptions,
      supportsOnSiteWork, craneJob,
    ],
  )
  // Client validation first; a SERVER refusal that names a stop ("stops[1].saveToAddressBook") is
  // shown on that stop too, re-indexed from the submitted list (empty rows dropped) to the shown one.
  const shownStops = onSite ? siteStops : stops
  const submittedStops = onSite ? siteStops : stops.filter((stop) => !isEmptyStopRow(stop))
  const serverStopErrors = Object.fromEntries(
    Object.entries(fieldErrors)
      .filter(([field, messages]) => field.startsWith('stops[') && messages.length > 0)
      .map(([field, messages]) => [
        field.replace(/^stops[(d+)]/, (match, index: string) => {
          const position = shownStops.indexOf(submittedStops[Number(index)])
          return position >= 0 ? `stops[${position}]` : match
        }),
        messages[0],
      ]),
  )
  const inlineErrors = { ...serverStopErrors, ...fieldErrorMap(clientErrors) }

  // ---- Subnavigation (2026-10-08 create redesign) -------------------------------------------
  // ONE lifted form state, several sections: the subnav only decides which part renders. Which
  // sections exist follows the transport type's capabilities (route? goods?), never a type name.
  const sectionIds: NewDossierSectionId[] = [
    'basis',
    ...(showTransportIntake ? (['route'] as const) : []),
    ...(showTransportIntake && !onSite ? (['goederen'] as const) : []),
    ...(showTransportIntake ? (['vereisten'] as const) : []),
    'controle',
  ]
  const { activeId, setActive } = useSectionNavigation(sectionIds, 'basis')
  const activeSection = activeId as NewDossierSectionId
  const sectionHeading = useRef<HTMLHeadingElement>(null)
  const submittingRef = useRef(false)
  const sectionLabel = (id: NewDossierSectionId) =>
    id === 'vereisten' && onSite ? t('dossiers.new.sections.opmerkingen') : t(`dossiers.new.sections.${id}`)

  /** Which section owns a failing field (client errors carry the order-form section; server paths are matched by prefix). */
  function sectionForField(field: string): NewDossierSectionId {
    const client = clientErrors.find((error) => error.field === field)
    if (client) {
      if (client.section === 'route') return 'route'
      if (client.section === 'goederen') return sectionIds.includes('goederen') ? 'goederen' : 'basis'
      return 'basis'
    }
    if (/^(stops|siteStop|activityDurationHours|workDescription|craneJob|lift)/.test(field)) return 'route'
    if (/^(goodsDescription|cargoItems|cargo|quantity|weightKg|volumeM3|palletCount|distanceKm|loadingMeters)/.test(field)) {
      return sectionIds.includes('goederen') ? 'goederen' : 'basis'
    }
    if (/^(adrRequired|craneRequired|plateauRequired|moffettRequired|isReturnMovement|notes)/.test(field)) {
      return sectionIds.includes('vereisten') ? 'vereisten' : 'basis'
    }
    return 'basis'
  }
  const sectionErrorCounts = useMemo(() => {
    const counts: Record<NewDossierSectionId, number> = { basis: 0, route: 0, goederen: 0, vereisten: 0, controle: 0 }
    for (const error of clientErrors) counts[sectionForField(error.field)] += 1
    for (const [field, messages] of Object.entries(fieldErrors)) {
      if (messages.length > 0 && !clientErrors.some((error) => error.field === field)) counts[sectionForField(field)] += 1
    }
    return counts
    // eslint-disable-next-line react-hooks/exhaustive-deps -- sectionForField only reads clientErrors/sectionIds, both covered.
  }, [clientErrors, fieldErrors, showTransportIntake, onSite])

  /** Section switch from a button/link: the new section's heading takes focus (tabs keep their own focus). */
  function goTo(section: NewDossierSectionId) {
    setActive(section)
    setTimeout(() => sectionHeading.current?.focus({ preventScroll: true }), 0)
  }

  /** Validation-summary click: open the owning section and focus its first invalid control. */
  function jumpToField(field: string) {
    setActive(sectionForField(field))
    setTimeout(() => {
      const invalid = document.querySelector<HTMLElement>('.ui-sectioned-form-body [aria-invalid="true"]')
      ;(invalid ?? sectionHeading.current)?.focus({ preventScroll: true })
    }, 0)
  }

  // Leaving the page (router or tab close) with anything filled in asks first — the shared guard.
  const dirty = customerId !== null || selection !== null || customerReference.trim() !== '' || intakeTouched

  function setStop(key: string, patch: Partial<StopFormRow>) {
    // One patch path for both lists: for a site stop it also keeps the end time predictable
    // (automatic = start + duration until the planner types an end of their own).
    const apply = (rows: StopFormRow[]) =>
      rows.map((row) => (row.key === key ? applyStopPatch(row, patch, parseDurationHours(durationHours)) : row))
    if (onSite) mutateSiteStops(apply)
    else mutateStops(apply)
  }

  /** The planned duration drives the automatic end of the site stop — recomputed right here, no effect. */
  function changeDuration(text: string) {
    setDurationHours(text)
    mutateSiteStops((rows) => rows.map((row) => withAutoPlannedEnd(row, parseDurationHours(text))))
  }

  function changeCraneJobKind(kind: CraneJobKind) {
    setCraneJob((job) => ({ ...job, kind }))
    setClientErrors([])
  }

  function setCargo(key: string, patch: Partial<CargoFormRow>) {
    setCargoItems((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

  function applyCargoUnit(key: string, code: string | null) {
    const master = code ? unitMaster.find((u) => u.code.toUpperCase() === code.toUpperCase()) ?? null : null
    setCargoItems((rows) => rows.map((row) => (row.key === key ? applyUnitToCargoRow(row, code, master) : row)))
  }

  const cargoDimensionsFixed = (cargo: CargoFormRow) =>
    (cargo.quantityUnitCode
      ? unitMaster.find((u) => u.code.toUpperCase() === cargo.quantityUnitCode!.toUpperCase())
      : undefined
    )?.dimensionBehavior === 'Fixed'

  /**
   * Type switch. Shared fields (klant, referentie, datum, goederen, eerste laad-/losadres)
   * always survive silently. Only a switch that would actually DROP filled extra stops
   * (Distributie → A-naar-B with 2+ filled unload addresses) asks first; hidden-but-kept data
   * (→ Opslag/Blanco) is confirmed at submit instead, when it would really be discarded.
   */
  function selectType(nextSelection: string) {
    if (nextSelection === selection) return
    const nextType = nextSelection === 'blanco' ? null : quickStartTypes.find((type) => type.id === nextSelection) ?? null
    const nextIsAtoB = Boolean(nextType?.hasStops) && nextType?.code !== 'DISTRIBUTIE'
    if (nextIsAtoB) {
      // A→B: exactly one loading + one unloading stop. Extra FILLED stops need a confirmation;
      // extra empty rows are dropped silently.
      const keepKeys = new Set<string>()
      const firstLoading = stops.find((s) => s.stopType === 'Loading')
      const firstUnloading = stops.find((s) => s.stopType === 'Unloading')
      if (firstLoading) keepKeys.add(firstLoading.key)
      if (firstUnloading) keepKeys.add(firstUnloading.key)
      const dropped = stops.filter((s) => !keepKeys.has(s.key))
      const droppedFilled = dropped.filter((s) => !isEmptyStopRow(s))
      if (droppedFilled.length > 0) {
        setPendingSwitch({ nextSelection, dropKeys: dropped.map((s) => s.key) })
        return
      }
      mutateStops((rows) => rows.filter((row) => keepKeys.has(row.key)))
    }
    applySelection(nextSelection, nextType)
  }

  function applySelection(nextSelection: string, nextType: ActivityType | null) {
    setSelection(nextSelection)
    setClientErrors([])
    setFormError(null)
    // Kraantransport implies a crane requirement; the checkbox stays editable in Goederen. The
    // implied value follows the type until the user sets the flag themselves.
    if (!craneTouched) setCraneRequired(nextType?.code === 'KRAANTRANSPORT')
  }

  function setCraneRequiredByUser(value: boolean) {
    setCraneTouched(true)
    setCraneRequired(value)
  }

  function confirmPendingSwitch() {
    if (!pendingSwitch) return
    const { nextSelection, dropKeys } = pendingSwitch
    setPendingSwitch(null)
    const dropSet = new Set(dropKeys)
    mutateStops((rows) => rows.filter((row) => !dropSet.has(row.key)))
    applySelection(nextSelection, nextSelection === 'blanco' ? null : quickStartTypes.find((type) => type.id === nextSelection) ?? null)
  }

  /** Opens the quick-create dialog; resolves with the option to select (combobox contract) or null. */
  function quickCreateCustomer(name: string): Promise<SearchableSelectOption | null> {
    return new Promise((resolve) =>
      setCustomerQuickCreate({
        name,
        resolve: (created) => resolve(created ? { value: created.id, label: created.name, meta: created.customerNumber } : null),
      }),
    )
  }

  function closeCustomerQuickCreate(created: CustomerDetail | null) {
    const pending = customerQuickCreate
    setCustomerQuickCreate(null)
    if (created) {
      setCreatedCustomers((current) => [created, ...current.filter((c) => c.id !== created.id)])
      setCustomerId(created.id)
      toast.showSuccess(t('customers.quickCreate.created', { name: created.name }))
    }
    pending?.resolve(created)
    // Focus returns to the customer field (the dialog restores focus after it unmounts).
    setTimeout(() => document.getElementById('nd-klant')?.focus(), 0)
  }

  function addUnloadAddress() {
    mutateStops((rows) => [...rows, emptyStop('Unloading')])
  }

  async function createClassicDossier() {
    setBusy(true)
    setFormError(null)
    setFieldErrors({})
    try {
      const created = await createDossierFast({
        customerId: customerId!,
        dossierDate: dossierDate || null,
        customerReference: customerReference.trim() || null,
        activityTypeId: isBlanco ? null : (selectedType?.id ?? null),
      })
      toast.showSuccess(t('dossiers.new.created', { number: created.dossierNumber }))
      navigate(`/dossiers/${created.id}`)
    } catch (err) {
      const described = describeApiError(err, t('dossiers.new.createFailed'))
      setFormError(described.message)
      setFieldErrors(described.fieldErrors)
      setBusy(false)
    }
  }

  async function createTransportIntake() {
    // Untouched rows are intake scaffolding, not data: drop them and renumber the goods links
    // so cargo keeps pointing at the SAME stop rows (the backend links by position).
    const effectiveStops = stops.filter((stop) => !isEmptyStopRow(stop))
    const effectiveCargo = remapCargoStopIndices(cargoItems, stops, effectiveStops).filter((cargo) => !isEmptyCargoRow(cargo))
    // D2 — hijswerk op locatie: the work site is the ONLY stop and nothing is transported, so no
    // goods (lines, header summary or description) and no fictitious loading/unloading stop go out.
    const submitValues: OrderFormValues = onSite
      ? {
          ...values,
          stops: siteStops,
          cargoItems: [],
          goodsDescription: '', quantity: '', quantityUnitCode: null, weightKg: '', volumeM3: '', palletCount: '',
          distanceKm: '', loadingMeters: '',
          adrRequired: false, plateauRequired: false, moffettRequired: false, isReturnMovement: false,
        }
      : { ...values, stops: effectiveStops, cargoItems: effectiveCargo }

    const validationErrors = validateOrderForm(submitValues)
    if (durationInvalid) {
      validationErrors.push({
        section: 'route',
        field: 'activityDurationHours',
        label: t('dossiers.new.durationLabel'),
        message: t('dossiers.new.durationInvalid'),
      })
    }
    setClientErrors(validationErrors)
    if (validationErrors.length > 0) {
      // The ValidationSummary scrolls itself into view and lists every failing field; the subnav
      // opens the first section that owns one (errors come section-ordered from the validator).
      setFormError(t('dossiers.new.validationIntro'))
      setActive(validationErrors[0].section === 'route' ? 'route' : validationErrors[0].section === 'goederen' && sectionIds.includes('goederen') ? 'goederen' : 'basis')
      return
    }

    setBusy(true)
    setFormError(null)
    setFieldErrors({})
    try {
      const created = await createTransportOrder({
        ...buildSubmitPayload(submitValues),
        activityTypeId: selectedType!.id,
        activityDurationHours: durationValue,
      })
      toast.showSuccess(t('dossiers.new.created', { number: created.dossierNumber ?? created.orderNumber }))
      navigate(created.dossierId ? `/dossiers/${created.dossierId}` : `/orders/${created.id}`)
    } catch (err) {
      // The filled form state survives a server error — only the error surfaces.
      const described = describeApiError(err, t('dossiers.new.createFailed'))
      setFormError(described.message)
      setFieldErrors(described.fieldErrors)
      const firstField = Object.keys(described.fieldErrors)[0]
      if (firstField) setActive(sectionForField(firstField))
      setBusy(false)
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    // Transport acceptance T-H2: ONLY the explicit "Dossier aanmaken" button creates. Implicit
    // submission (Enter in a field) and any button that changed role while it was being clicked
    // never reach the create call.
    const submitter = (event.nativeEvent as SubmitEvent).submitter as HTMLElement | null
    if (submitter?.dataset.createDossier !== 'true') return
    // A double click fires two submit events before `busy` re-renders; the ref closes that gap.
    if (busy || submittingRef.current) return
    submittingRef.current = true
    try {
      await submitConfirmed()
    } finally {
      submittingRef.current = false
    }
  }

  async function submitConfirmed() {
    if (!customerId) {
      setFormError(t('dossiers.new.customerRequired'))
      goTo('basis')
      return
    }
    if (!selection) {
      setFormError(t('dossiers.new.typeRequired'))
      goTo('basis')
      return
    }
    if (showTransportIntake && intakeTouched) {
      await createTransportIntake()
      return
    }
    if (!showTransportIntake && intakeTouched) {
      // Blanco/Opslag (or no order permission): route/goods would NOT be saved — say so first.
      setPendingDiscardSubmit(true)
      return
    }
    await createClassicDossier()
  }

  const EmptyIcon = DEFAULT_ACTIVITY_TYPE_ICON
  const submitEnabled = Boolean(customerId && selection) && !busy
  // Nothing left to fill (blanco/Opslag/no order right): creating IS the forward action; with a
  // transport intake the forward action is the next section until Controle.
  const createIsPrimary = activeSection === 'controle' || !showTransportIntake
  const activeIndex = sectionIds.indexOf(activeSection)
  const prevSection = activeIndex > 0 ? sectionIds[activeIndex - 1] : null
  const nextSection = activeIndex < sectionIds.length - 1 ? sectionIds[activeIndex + 1] : null
  const selectedCustomerName =
    customerOptions.find((option) => option.value === customerId)?.label
    ?? (customerRequirements && customerRequirements.id === customerId ? customerRequirements.name : null)
  const submittedStopsForReview = onSite ? siteStops.filter((stop) => !isEmptyStopRow(stop)) : stops.filter((stop) => !isEmptyStopRow(stop))
  const weightTotals = cargoWeightTotalsFromRows(cargoItems)
  const reviewWarnings: string[] = []
  if (showTransportIntake) {
    const undated = submittedStopsForReview.filter((stop) => !stop.date).length
    if (undated > 0) reviewWarnings.push(t('dossiers.new.review.warnStopsWithoutDate', { count: undated }))
    if (!onSite && weightTotals.linesWithoutUnitWeight > 0 && cargoItems.some((cargo) => !isEmptyCargoRow(cargo))) {
      reviewWarnings.push(t('dossiers.new.review.warnLinesWithoutWeight', { count: weightTotals.linesWithoutUnitWeight }))
    }
  }
  const reviewHints: string[] = []
  if (selectedType && !selectedType.hasStops) reviewHints.push(t('dossiers.new.noRouteTypeHint', { name: selectedType.name }))
  if (selectedType?.hasStops && !canCreateOrders) reviewHints.push(t('dossiers.new.noOrderPermissionHint'))
  if (isBlanco) reviewHints.push(t('dossiers.new.blancoHint'))
  const tickedRequirements = [
    adrRequired ? t('transportOrders.goods.adr') : null,
    craneRequired ? t('transportOrders.goods.crane') : null,
    plateauRequired ? t('transportOrders.goods.plateau') : null,
    moffettRequired ? t('transportOrders.goods.moffett') : null,
    isReturnMovement ? t('transportOrders.goods.returnMovement') : null,
  ].filter((label): label is string => label !== null)

  const heading = (section: NewDossierSectionId) => (
    <h2 className="new-dossier-section-title" id={`nd-sectie-${section}`} tabIndex={-1} ref={sectionHeading}>
      {sectionLabel(section)}
    </h2>
  )

  const durationField = (
    <FormField
      label={onSite ? t('stopEditor.crane.duration') : t('dossiers.new.durationLabel')}
      htmlFor="nd-duration"
      hint={onSite ? t('stopEditor.crane.durationHint') : t('dossiers.new.durationHint')}
      error={durationInvalid ? t('dossiers.new.durationInvalid') : undefined}
    >
      <input
        id="nd-duration"
        type="number"
        min={0}
        step="0.25"
        value={durationHours}
        onChange={(event) => changeDuration(event.target.value)}
        disabled={busy}
        aria-invalid={durationInvalid ? true : undefined}
      />
    </FormField>
  )

  const notesField = (
    <FormField label={t('dossiers.new.notes')} htmlFor="nd-notes">
      <textarea
        id="nd-notes"
        rows={3}
        value={notes}
        onChange={(event) => setNotes(event.target.value)}
        disabled={busy}
        maxLength={2000}
      />
    </FormField>
  )

  const sections: SectionDef[] = [
    {
      id: 'basis',
      label: sectionLabel('basis'),
      complete: Boolean(customerId && selection),
      hasError: sectionErrorCounts.basis > 0,
      errorCount: sectionErrorCounts.basis,
      render: () => (
        <section className="new-dossier-section new-dossier-basis" aria-labelledby="nd-sectie-basis">
          {heading('basis')}
          <div className="new-dossier-basis-grid">
            <FormField label={t('dossiers.new.customer')} htmlFor="nd-klant" required error={getFieldError(fieldErrors, 'customerId')} className="new-dossier-basis-customer">
              <SearchableSelect
                id="nd-klant"
                value={customerId}
                onChange={setCustomerId}
                options={customerOptions}
                onSearch={searchCustomersWithCreated}
                selectedLabel={selectedCustomerName ?? undefined}
                placeholder={t('dossiers.new.customerPlaceholder')}
                emptyMessage={t('dossiers.new.noCustomerFound')}
                disabled={busy}
                onCreate={
                  canCreateCustomers
                    ? {
                        label: (query) => t('dossiers.new.newCustomerFor', { query }),
                        emptyQueryLabel: t('dossiers.new.newCustomer'),
                        alwaysShow: true,
                        create: quickCreateCustomer,
                      }
                    : undefined
                }
              />
              {(canCreateCustomers || selectedCustomerIsNew) && (
                <div className="new-dossier-field-actions">
                  {canCreateCustomers && (
                    <button type="button" className="link-button" onClick={() => void quickCreateCustomer('')} disabled={busy}>
                      {t('dossiers.new.newCustomer')}
                    </button>
                  )}
                  {selectedCustomerIsNew && (
                    <a className="new-dossier-field-link" href={`/customers/${customerId}`} target="_blank" rel="noopener noreferrer">
                      {t('customers.quickCreate.openCustomer')} ↗
                    </a>
                  )}
                </div>
              )}
            </FormField>
            <FormField label={t('dossiers.new.customerReference')} htmlFor="nd-ref" error={getFieldError(fieldErrors, 'customerReference')}>
              <input
                id="nd-ref"
                value={customerReference}
                onChange={(event) => setCustomerReference(event.target.value)}
                maxLength={100}
                disabled={busy}
              />
            </FormField>
            <FormField label={t('dossiers.new.date')} htmlFor="nd-datum" error={getFieldError(fieldErrors, 'dossierDate')}>
              <input
                id="nd-datum"
                type="date"
                value={dossierDate}
                onChange={(event) => setDossierDate(event.target.value)}
                disabled={busy}
              />
            </FormField>
          </div>

          <fieldset className="new-dossier-templates">
            <legend>{t('dossiers.new.transportType')}</legend>
            <div className="new-dossier-tiles">
              {quickStartTypes.map((type) => {
                const Icon = activityTypeIcon(type.icon)
                const selected = selection === type.id
                return (
                  <label key={type.id} className={`new-dossier-tile${selected ? ' new-dossier-tile-selected' : ''}`}>
                    <input
                      type="radio"
                      name="nd-transporttype"
                      value={type.id}
                      checked={selected}
                      onChange={() => selectType(type.id)}
                      disabled={busy}
                    />
                    <Icon size={20} aria-hidden="true" />
                    <span>{type.name}</span>
                    {selected && <span className="new-dossier-tile-check" aria-hidden="true">✓</span>}
                  </label>
                )
              })}
            </div>
            {typesFailed && (
              <p className="new-dossier-hint" role="note">
                {t('dossiers.new.typesLoadFailed')}
              </p>
            )}
            <label className={`new-dossier-blanco${isBlanco ? ' new-dossier-blanco-selected' : ''}`}>
              <input
                type="radio"
                name="nd-transporttype"
                value="blanco"
                checked={isBlanco}
                onChange={() => selectType('blanco')}
                disabled={busy}
              />
              <EmptyIcon size={16} aria-hidden="true" />
              <span>{t('dossiers.new.blancoOption')}</span>
            </label>
          </fieldset>

          {!selection && (
            <p className="new-dossier-hint" role="note">
              {t('dossiers.new.chooseTypeHint')}
            </p>
          )}
          {reviewHints.map((hint) => (
            <p key={hint} className="new-dossier-hint" role="note">
              {hint}
            </p>
          ))}
        </section>
      ),
    },
    ...(showTransportIntake
      ? ([
          {
            id: 'route',
            label: sectionLabel('route'),
            optional: true,
            filled: onSite ? siteTouched : routeTouched,
            hasError: sectionErrorCounts.route > 0,
            errorCount: sectionErrorCounts.route,
            render: () => (
              <section className="new-dossier-section" aria-labelledby="nd-sectie-route">
                <div className="new-dossier-section-header">
                  {heading('route')}
                  {isDistribution && (
                    <Button variant="secondary" onClick={addUnloadAddress} disabled={busy}>
                      {t('dossiers.new.addUnloadAddress')}
                    </Button>
                  )}
                </div>
                {supportsOnSiteWork && (
                  <CraneJobKindField idPrefix="nd" kind={craneJob.kind} onChange={changeCraneJobKind} disabled={busy} />
                )}
                <p className="new-dossier-hint">
                  {onSite
                    ? t('stopEditor.crane.onSiteHint')
                    : isDistribution ? t('dossiers.new.routeHintDistribution') : t('dossiers.new.routeHintDirect')}
                </p>
                {showDuration && !onSite && <div className="new-dossier-row new-dossier-row-narrow">{durationField}</div>}
                <RouteSection
                  stops={onSite ? siteStops : stops}
                  canSaveToAddressBook={canCreateLocations && Boolean(customerId)}
                  siteDurationHours={durationInvalid ? null : durationValue}
                  customerId={customerId ?? ''}
                  saving={busy}
                  locationHours={locationHours}
                  errors={inlineErrors}
                  onAddStop={() => undefined}
                  setStop={setStop}
                  moveStop={() => undefined}
                  onRemoveStop={(key) => mutateStops((rows) => rows.filter((row) => row.key !== key))}
                  onRequestRefresh={() => undefined}
                  onQuickCreate={
                    canCreateLocations && customerId
                      ? (name) => new Promise<LocationOption | null>((resolve) => setQuickCreate({ name, resolve }))
                      : undefined
                  }
                  compact
                  hideHeader
                  canRemoveStop={(stop) =>
                    isDistribution && stop.stopType === 'Unloading' && stops.filter((s) => s.stopType === 'Unloading').length > 1
                  }
                />
                {onSite && (
                  <OnSiteWorkFields
                    idPrefix="nd"
                    job={craneJob}
                    onChange={(patch) => setCraneJob((job) => ({ ...job, ...patch }))}
                    disabled={busy}
                    descriptionError={inlineErrors.workDescription}
                    duration={<div className="new-dossier-row new-dossier-row-narrow">{durationField}</div>}
                  />
                )}
              </section>
            ),
          },
        ] as SectionDef[])
      : []),
    ...(showTransportIntake && !onSite
      ? ([
          {
            id: 'goederen',
            label: sectionLabel('goederen'),
            optional: true,
            filled: goodsOnlyTouched,
            hasError: sectionErrorCounts.goederen > 0,
            errorCount: sectionErrorCounts.goederen,
            render: () => (
              <section className="new-dossier-section" aria-labelledby="nd-sectie-goederen">
                {heading('goederen')}
                <p className="new-dossier-hint">{t('dossiers.new.goodsHint')}</p>
                <GoodsSection
                  part="goods"
                  goodsDescription={goodsDescription}
                  setGoodsDescription={setGoodsDescription}
                  quantity={quantity}
                  setQuantity={setQuantity}
                  quantityUnit=""
                  quantityUnitCode={quantityUnitCode}
                  setQuantityUnitCode={setQuantityUnitCode}
                  weightKg={weightKg}
                  setWeightKg={setWeightKg}
                  volumeM3={volumeM3}
                  setVolumeM3={setVolumeM3}
                  palletCount={palletCount}
                  setPalletCount={setPalletCount}
                  distanceKm={distanceKm}
                  setDistanceKm={setDistanceKm}
                  loadingMeters={loadingMeters}
                  setLoadingMeters={setLoadingMeters}
                  adrRequired={adrRequired}
                  setAdrRequired={setAdrRequired}
                  craneRequired={craneRequired}
                  setCraneRequired={setCraneRequiredByUser}
                  plateauRequired={plateauRequired}
                  setPlateauRequired={setPlateauRequired}
                  moffettRequired={moffettRequired}
                  setMoffettRequired={setMoffettRequired}
                  isReturnMovement={isReturnMovement}
                  setIsReturnMovement={setIsReturnMovement}
                  derivedFromCargo={derivedFromCargo}
                  cargoSummary={cargoSummary}
                  cargoItems={cargoItems}
                  stops={stops}
                  unitOptions={unitOptions}
                  preferredUnits={preferredUnits}
                  setCargo={setCargo}
                  onAddCargoRow={() => setCargoItems((rows) => [...rows, emptyCargoRow()])}
                  onAddCargoRowFromHeader={() =>
                    setCargoItems((rows) => [
                      ...rows,
                      cargoRowFromHeader({ quantity, quantityUnit: '', quantityUnitCode, weightKg, volumeM3, palletCount }),
                    ])
                  }
                  onRemoveCargoRow={(key) => setCargoItems((rows) => rows.filter((row) => row.key !== key))}
                  onDuplicateCargoRow={(key) => setCargoItems((rows) => duplicateCargoRowInList(rows, key))}
                  applyCargoUnit={applyCargoUnit}
                  cargoDimensionsFixed={cargoDimensionsFixed}
                  saving={busy}
                  errors={inlineErrors}
                />
              </section>
            ),
          },
        ] as SectionDef[])
      : []),
    ...(showTransportIntake
      ? ([
          {
            id: 'vereisten',
            label: sectionLabel('vereisten'),
            optional: true,
            filled: (!onSite && requirementsTouched) || notes.trim() !== '',
            hasError: sectionErrorCounts.vereisten > 0,
            errorCount: sectionErrorCounts.vereisten,
            render: () => (
              <section className="new-dossier-section" aria-labelledby="nd-sectie-vereisten">
                {heading('vereisten')}
                {!onSite && (
                  <>
                    <h3 className="new-dossier-subtitle">{t('dossiers.new.sectionRequirements')}</h3>
                    <p className="new-dossier-hint">{t('dossiers.new.requirementsHint')}</p>
                    <GoodsSection
                      part="requirements"
                      goodsDescription={goodsDescription}
                      setGoodsDescription={setGoodsDescription}
                      quantity={quantity}
                      setQuantity={setQuantity}
                      quantityUnit=""
                      quantityUnitCode={quantityUnitCode}
                      setQuantityUnitCode={setQuantityUnitCode}
                      weightKg={weightKg}
                      setWeightKg={setWeightKg}
                      volumeM3={volumeM3}
                      setVolumeM3={setVolumeM3}
                      palletCount={palletCount}
                      setPalletCount={setPalletCount}
                      distanceKm={distanceKm}
                      setDistanceKm={setDistanceKm}
                      loadingMeters={loadingMeters}
                      setLoadingMeters={setLoadingMeters}
                      adrRequired={adrRequired}
                      setAdrRequired={setAdrRequired}
                      craneRequired={craneRequired}
                      setCraneRequired={setCraneRequiredByUser}
                      plateauRequired={plateauRequired}
                      setPlateauRequired={setPlateauRequired}
                      moffettRequired={moffettRequired}
                      setMoffettRequired={setMoffettRequired}
                      isReturnMovement={isReturnMovement}
                      setIsReturnMovement={setIsReturnMovement}
                      derivedFromCargo={derivedFromCargo}
                      cargoSummary={cargoSummary}
                      cargoItems={cargoItems}
                      stops={stops}
                      unitOptions={unitOptions}
                      preferredUnits={preferredUnits}
                      setCargo={setCargo}
                      onAddCargoRow={() => setCargoItems((rows) => [...rows, emptyCargoRow()])}
                      onAddCargoRowFromHeader={() => undefined}
                      onRemoveCargoRow={(key) => setCargoItems((rows) => rows.filter((row) => row.key !== key))}
                      onDuplicateCargoRow={(key) => setCargoItems((rows) => duplicateCargoRowInList(rows, key))}
                      applyCargoUnit={applyCargoUnit}
                      cargoDimensionsFixed={cargoDimensionsFixed}
                      saving={busy}
                      errors={inlineErrors}
                    />
                    <h3 className="new-dossier-subtitle">{t('dossiers.new.sectionNotes')}</h3>
                  </>
                )}
                {notesField}
              </section>
            ),
          },
        ] as SectionDef[])
      : []),
    {
      id: 'controle',
      label: sectionLabel('controle'),
      render: () => (
        <section className="new-dossier-section" aria-labelledby="nd-sectie-controle">
          {heading('controle')}
          <NewDossierReview
            customerName={selectedCustomerName}
            typeName={selectedType?.name ?? null}
            isBlanco={isBlanco}
            customerReference={customerReference}
            dossierDate={dossierDate}
            intake={showTransportIntake}
            onSite={onSite}
            stops={submittedStopsForReview}
            workDescription={craneJob.workDescription}
            goodsDescription={goodsDescription}
            cargoItems={cargoItems.filter((cargo) => !isEmptyCargoRow(cargo))}
            cargoSummary={cargoSummary}
            requirements={tickedRequirements}
            notes={notes}
            warnings={reviewWarnings}
            errors={clientErrors}
            hints={reviewHints}
            onEdit={goTo}
            onErrorSelect={jumpToField}
          />
        </section>
      ),
    },
  ]

  return (
    <div className="new-dossier">
      <UnsavedChangesGuard when={dirty && !busy} />
      <BackButton to="/dossiers" label={t('dossiers.new.back')} />
      <PageHeader title={t('dossiers.new.title')} subtitle={t('dossiers.new.subtitleIntake')} />

      <form
        onSubmit={(event) => void submit(event)}
        onKeyDown={(event) => {
          // T-H2: Enter in a text field must not trigger the browser's implicit submission
          // (which "clicks" the default button = Dossier aanmaken). Pickers handle Enter
          // themselves before this bubbles up; textareas keep their newline.
          const target = event.target as HTMLElement
          if (event.key === 'Enter' && target instanceof HTMLInputElement && !['submit', 'button', 'checkbox', 'radio'].includes(target.type)) {
            event.preventDefault()
          }
        }}
        noValidate
      >
        <ValidationSummary
          message={formError}
          fieldErrors={{
            ...fieldErrors,
            ...Object.fromEntries(clientErrors.map((error) => [error.field, [error.message]])),
          }}
          fieldLabels={Object.fromEntries(clientErrors.map((error) => [error.field, error.label]))}
          onSelect={jumpToField}
        />

        <SectionedForm
          sections={sections}
          activeId={activeSection}
          onActiveChange={(id) => setActive(id)}
          actions={
            <div className="new-dossier-actions">
              <div className="new-dossier-actions-side">
                {prevSection && (
                  <Button variant="secondary" onClick={() => goTo(prevSection)} disabled={busy}>
                    {t('dossiers.new.nav.prev', { section: sectionLabel(prevSection) })}
                  </Button>
                )}
              </div>
              <div className="new-dossier-actions-main">
                <Button variant="secondary" onClick={() => navigate('/dossiers')} disabled={busy}>
                  {t('ui.actions.cancel')}
                </Button>
                {createIsPrimary ? (
                  <>
                    {nextSection && activeSection !== 'controle' && (
                      <Button key="nav-next" variant="secondary" onClick={() => goTo(nextSection)} disabled={busy}>
                        {t('dossiers.new.nav.next', { section: sectionLabel(nextSection) })}
                      </Button>
                    )}
                    <Button key="create" type="submit" data-create-dossier="true" disabled={!submitEnabled}>
                      {busy ? t('dossiers.new.creating') : t('dossiers.new.create')}
                    </Button>
                  </>
                ) : (
                  <>
                    <Button key="create" type="submit" variant="secondary" data-create-dossier="true" disabled={!submitEnabled}>
                      {busy ? t('dossiers.new.creating') : t('dossiers.new.create')}
                    </Button>
                    {nextSection && (
                      <Button key="nav-next" onClick={() => goTo(nextSection)} disabled={busy}>
                        {t('dossiers.new.nav.next', { section: sectionLabel(nextSection) })}
                      </Button>
                    )}
                  </>
                )}
              </div>
            </div>
          }
        />
      </form>

      {customerQuickCreate && <CustomerQuickCreateDialog initialName={customerQuickCreate.name} onClose={closeCustomerQuickCreate} />}

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

      {pendingSwitch && (
        <ConfirmDialog
          title={t('dossiers.new.switchTypeTitle')}
          message={t('dossiers.new.switchTypeMessage', {
            count: pendingSwitch.dropKeys.filter((key) => {
              const stop = stops.find((s) => s.key === key)
              return stop && !isEmptyStopRow(stop)
            }).length,
          })}
          confirmLabel={t('dossiers.new.switchTypeConfirm')}
          destructive
          onConfirm={confirmPendingSwitch}
          onCancel={() => setPendingSwitch(null)}
        />
      )}

      {pendingDiscardSubmit && (
        <ConfirmDialog
          title={t('dossiers.new.discardIntakeTitle')}
          message={t('dossiers.new.discardIntakeMessage')}
          confirmLabel={t('dossiers.new.discardIntakeConfirm')}
          destructive
          onConfirm={() => {
            setPendingDiscardSubmit(false)
            void createClassicDossier()
          }}
          onCancel={() => setPendingDiscardSubmit(false)}
        />
      )}
    </div>
  )
}
