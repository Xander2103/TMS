import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { ApiError } from '../../../api/apiClient'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { SectionedForm, type SectionDef } from '../../../components/ui/SectionedForm'
import { useSectionNavigation } from '../../../components/ui/useSectionNavigation'
import { getCustomer, searchCustomers } from '../../customers/api/customersApi'
import type { CustomerDetail, CustomerListItem } from '../../customers/types'
import { getLegalEntityOptions } from '../../legal-entities/api/legalEntitiesApi'
import { listWarehouses } from '../../warehousing/api/warehousingApi'
import type { Warehouse } from '../../warehousing/types'
import type { LegalEntityOption } from '../../legal-entities/types'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { LocationSelect } from '../../locations/components/LocationSelect'
import { LocationQuickCreateDialog } from '../../locations/components/LocationQuickCreateDialog'
import type { LocationOption } from '../../locations/types'
import { useAuth } from '../../auth/authContextValue'
import { CountryCombobox } from '../../reference/components/CountryCombobox'
import { UNIT_TYPE_LABELS, type PackageUnitType } from '../../packages/types'
import { computeVolumeM3 } from '../../../utils/volume'
import {
  getCustomerPricingConfig,
  listServiceOptions,
  listUnitTypeMaster,
  previewPrice,
  type CustomerPricingConfig,
  type PriceCalculationResult,
  type ServiceOption,
  type UnitTypeMaster,
} from '../../tarification/api/pricingApi'
import { formatServiceValue } from '../../tarification/serviceValueFormat'
import { UnitSelect } from './UnitSelect'
import { STOP_TYPE_LABELS, type StopInput, type TransportOrderDetail, type TransportOrderInput } from '../types'
import './transport-order-form.css'

/** Dutch label per calculation method, for the services tab's "Berekeningswijze" and kind badges. */
const SERVICE_KIND_LABELS: Record<ServiceOption['kind'], string> = {
  Percent: 'Percentage',
  Fixed: 'Vast bedrag',
  PerHour: 'Per uur',
  PerStop: 'Per stop',
  PerUnit: 'Per eenheid',
  PerOrderLine: 'Per orderlijn',
  PerKg: 'Per kg',
  PerM3: 'Per m³',
  PerLdm: 'Per laadmeter',
  PerDay: 'Per dag',
  PerPalletDay: 'Per pallet-dag',
}

interface CargoFormRow {
  key: string
  /** Existing cargo line id (id-preserving update sync); null for a new/unsaved row. */
  id: string | null
  description: string
  barcode: string
  expectedQuantity: string
  quantityUnit: string
  quantityUnitCode: string | null
  notes: string
  unitType: PackageUnitType | ''
  unitTypeLabel: string
  totalWeightKg: string
  weightPerUnitKg: string
  lengthMeters: string
  widthMeters: string
  heightMeters: string
  volumeM3: string
  volumeIsManual: boolean
  adrRequired: boolean
  adrDetails: string
  stackable: boolean
  reference: string
  /** '' = automatic (unambiguous orders link themselves server-side). */
  loadingStopIndex: string
  unloadingStopIndex: string
}

function numberOrNullFrom(value: string): number | null {
  if (value.trim() === '') return null
  const parsed = Number(value.replace(',', '.'))
  return Number.isFinite(parsed) ? parsed : null
}

interface StopFormRow {
  key: string
  stopType: StopInput['stopType']
  locationId: string
  locationName: string
  address: string
  postalCode: string
  city: string
  countryCode: string
  plannedFrom: string
  plannedTo: string
  requestedFrom: string
  requestedTo: string
  confirmedFrom: string
  confirmedTo: string
  earliestAllowed: string
  latestAllowed: string
  appointmentRequired: boolean
  appointmentReference: string
  reference: string
  instructions: string
  accessInstructions: string
  loadingInstructions: string
  unloadingInstructions: string
}

let stopKeyCounter = 0
function nextStopKey(): string {
  stopKeyCounter += 1
  return `stop-${stopKeyCounter}`
}

function emptyStop(stopType: StopInput['stopType']): StopFormRow {
  return {
    key: nextStopKey(),
    stopType,
    locationId: '',
    locationName: '',
    address: '',
    postalCode: '',
    city: '',
    countryCode: 'BE',
    plannedFrom: '',
    plannedTo: '',
    requestedFrom: '',
    requestedTo: '',
    confirmedFrom: '',
    confirmedTo: '',
    earliestAllowed: '',
    latestAllowed: '',
    appointmentRequired: false,
    appointmentReference: '',
    reference: '',
    instructions: '',
    accessInstructions: '',
    loadingInstructions: '',
    unloadingInstructions: '',
  }
}

/** ISO datetime → value usable by <input type="datetime-local"> (minute precision). */
function toLocalInput(value: string | null): string {
  return value ? value.slice(0, 16) : ''
}

interface TransportOrderFormProps {
  order?: TransportOrderDetail
  onSubmit: (input: TransportOrderInput) => Promise<void>
  onCancel?: () => void
  submitLabel: string
  /** Documenten section body: create → PreparedOrderDocumentsEditor, edit → OrderDocumentsPanel. */
  documentsSection?: ReactNode
  /** True when the documents section saves itself (edit-mode panel). */
  documentsSectionIsPanel?: boolean
}

const SECTION_IDS = ['algemeen', 'route', 'goederen', 'services', 'documenten', 'prijs', 'samenvatting']

/**
 * Shared create/edit form, structured as internal subnavigation (Algemeen / Route & stops /
 * Goederen / Services & toeslagen / Documenten / Prijs / Samenvatting). All state is lifted
 * here, so switching tabs preserves values and never touches the router.
 */
export function TransportOrderForm({ order, onSubmit, onCancel, submitLabel, documentsSection, documentsSectionIsPanel }: TransportOrderFormProps) {
  const { hasPermission } = useAuth()
  const canCreateLocations = hasPermission('locations.create')
  const canOverridePrice = hasPermission('orders.override_price')
  const { activeId, setActive } = useSectionNavigation(SECTION_IDS, 'algemeen')
  const [customers, setCustomers] = useState<CustomerListItem[]>([])
  // Inline location creation from a stop's location picker: the promise resolves when the
  // dialog closes, so the SearchableSelect can auto-select the new location.
  const [quickCreate, setQuickCreate] = useState<{
    name: string
    resolve: (created: LocationOption | null) => void
  } | null>(null)

  const [customerId, setCustomerId] = useState(order?.customerId ?? '')
  const [customerReference, setCustomerReference] = useState(order?.customerReference ?? '')
  const [orderDate, setOrderDate] = useState(order?.orderDate ?? new Date().toISOString().slice(0, 10))
  const [goodsDescription, setGoodsDescription] = useState(order?.goodsDescription ?? '')
  const [quantity, setQuantity] = useState(order?.quantity === null || order === undefined ? '' : String(order.quantity))
  // Legacy free-text unit is preserved (still submitted) but no longer editable — the managed
  // unit-type code replaces it as the input.
  const [quantityUnit] = useState(order?.quantityUnit ?? '')
  const [quantityUnitCode, setQuantityUnitCode] = useState<string | null>(order?.quantityUnitCode ?? null)
  const [weightKg, setWeightKg] = useState(order?.weightKg === null || order === undefined ? '' : String(order.weightKg))
  const [volumeM3, setVolumeM3] = useState(order?.volumeM3 === null || order === undefined ? '' : String(order.volumeM3))
  const [palletCount, setPalletCount] = useState(
    order?.palletCount === null || order === undefined ? '' : String(order.palletCount),
  )
  const [adrRequired, setAdrRequired] = useState(order?.adrRequired ?? false)
  const [craneRequired, setCraneRequired] = useState(order?.craneRequired ?? false)
  const [agreedPrice, setAgreedPrice] = useState(
    order?.agreedPrice === null || order === undefined ? '' : String(order.agreedPrice),
  )
  const [notes, setNotes] = useState(order?.notes ?? '')

  const [legalEntities, setLegalEntities] = useState<LegalEntityOption[]>([])
  const [legalEntityId, setLegalEntityId] = useState(order?.legalEntityId ?? '')
  const [dieselSurchargeOverride, setDieselSurchargeOverride] = useState(order?.dieselSurchargeOverride ?? false)
  const [dieselSurchargePercentOverride, setDieselSurchargePercentOverride] = useState(
    order?.dieselSurchargePercentOverride === null || order?.dieselSurchargePercentOverride === undefined
      ? ''
      : String(order.dieselSurchargePercentOverride),
  )
  const [dieselSurchargeOverrideReason, setDieselSurchargeOverrideReason] = useState(order?.dieselSurchargeOverrideReason ?? '')

  const [stops, setStops] = useState<StopFormRow[]>(() =>
    order && order.stops.length > 0
      ? order.stops.map((s) => ({
          key: nextStopKey(),
          stopType: s.stopType,
          locationId: s.locationId ?? '',
          locationName: s.locationId ? '' : s.locationName,
          address: s.address ?? '',
          postalCode: s.postalCode ?? '',
          city: s.city ?? '',
          countryCode: s.countryCode ?? 'BE',
          plannedFrom: toLocalInput(s.plannedFrom),
          plannedTo: toLocalInput(s.plannedTo),
          requestedFrom: toLocalInput(s.requestedFrom),
          requestedTo: toLocalInput(s.requestedTo),
          confirmedFrom: toLocalInput(s.confirmedFrom),
          confirmedTo: toLocalInput(s.confirmedTo),
          earliestAllowed: toLocalInput(s.earliestAllowed),
          latestAllowed: toLocalInput(s.latestAllowed),
          appointmentRequired: s.appointmentRequired,
          appointmentReference: s.appointmentReference ?? '',
          reference: s.reference ?? '',
          instructions: s.instructions ?? '',
          accessInstructions: s.accessInstructions ?? '',
          loadingInstructions: s.loadingInstructions ?? '',
          unloadingInstructions: s.unloadingInstructions ?? '',
        }))
      : [emptyStop('Loading'), emptyStop('Unloading')],
  )

  const [cargoItems, setCargoItems] = useState<CargoFormRow[]>(() =>
    (order?.cargoItems ?? []).map((c) => {
      const loadingIndex = order?.stops.findIndex((s) => s.id === c.loadingStopId) ?? -1
      const unloadingIndex = order?.stops.findIndex((s) => s.id === c.unloadingStopId) ?? -1
      return {
        key: nextStopKey(),
        id: c.id,
        description: c.description ?? '',
        barcode: c.barcode ?? '',
        expectedQuantity: String(c.expectedQuantity),
        quantityUnit: c.quantityUnit ?? '',
        quantityUnitCode: c.quantityUnitCode ?? null,
        notes: c.notes ?? '',
        unitType: c.unitType ?? '',
        unitTypeLabel: c.unitTypeLabel ?? '',
        totalWeightKg: c.totalWeightKg !== null ? String(c.totalWeightKg) : '',
        weightPerUnitKg: c.weightPerUnitKg !== null ? String(c.weightPerUnitKg) : '',
        lengthMeters: c.lengthMeters !== null ? String(c.lengthMeters) : '',
        widthMeters: c.widthMeters !== null ? String(c.widthMeters) : '',
        heightMeters: c.heightMeters !== null ? String(c.heightMeters) : '',
        volumeM3: c.volumeM3 !== null ? String(c.volumeM3) : '',
        volumeIsManual: c.volumeIsManual,
        adrRequired: c.adrRequired,
        adrDetails: c.adrDetails ?? '',
        stackable: c.stackable,
        reference: c.reference ?? '',
        loadingStopIndex: loadingIndex >= 0 ? String(loadingIndex) : '',
        unloadingStopIndex: unloadingIndex >= 0 ? String(unloadingIndex) : '',
      }
    }),
  )

  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [loadedCustomerDetail, setLoadedCustomerDetail] = useState<{ id: string; detail: CustomerDetail } | null>(null)

  // --- Pricing state (spec 7): configured units, service options, live preview, override ---
  const { options: unitOptions } = useLookupOptions('/api/unit-types')
  const [serviceOptions, setServiceOptions] = useState<ServiceOption[]>([])
  const [selectedServiceOptionIds, setSelectedServiceOptionIds] = useState<string[]>(
    () => (order?.serviceLines ?? []).map((l) => l.serviceOptionId).filter((id): id is string => id !== null),
  )
  // Entered quantities for per-hour/per-stop services — and the billable pallet-days for
  // per-pallet-day services (auto-filled from pallets × days, manually correctable).
  const [serviceQuantities, setServiceQuantities] = useState<Record<string, string>>(() =>
    Object.fromEntries(
      (order?.serviceLines ?? [])
        .filter((l) => l.serviceOptionId !== null && l.quantity !== null && l.quantity !== undefined)
        .map((l) => [l.serviceOptionId as string, String(l.quantity)]),
    ),
  )
  // Per-pallet-day inputs: pallet count and day count, keyed by service option id.
  const [servicePallets, setServicePallets] = useState<Record<string, string>>(() =>
    Object.fromEntries(
      (order?.serviceLines ?? [])
        .filter((l) => l.serviceOptionId !== null && l.palletCount !== null && l.palletCount !== undefined)
        .map((l) => [l.serviceOptionId as string, String(l.palletCount)]),
    ),
  )
  const [serviceDays, setServiceDays] = useState<Record<string, string>>(() =>
    Object.fromEntries(
      (order?.serviceLines ?? [])
        .filter((l) => l.serviceOptionId !== null && l.dayCount !== null && l.dayCount !== undefined)
        .map((l) => [l.serviceOptionId as string, String(l.dayCount)]),
    ),
  )
  // Optional free-text note per manually selected service (e.g. "Afgesproken met klant").
  const [serviceNotes, setServiceNotes] = useState<Record<string, string>>(() =>
    Object.fromEntries(
      (order?.serviceLines ?? [])
        .filter((l) => l.serviceOptionId !== null && l.note)
        .map((l) => [l.serviceOptionId as string, l.note as string]),
    ),
  )
  // "+ Dienst of toeslag toevoegen" panel: the option being configured before it joins the
  // manually-selected set. Its quantity/pallet/day/note inputs write directly into the same state
  // maps used once selected, so nothing needs copying on "Toevoegen" — only the id moves.
  const [addServicePanelOpen, setAddServicePanelOpen] = useState(false)
  const [draftServiceOptionId, setDraftServiceOptionId] = useState('')
  const [pricingConfig, setPricingConfig] = useState<{ customerId: string; config: CustomerPricingConfig } | null>(null)
  const [unitMaster, setUnitMaster] = useState<UnitTypeMaster[]>([])
  const [warehouses, setWarehouses] = useState<Warehouse[]>([])
  const [preview, setPreview] = useState<PriceCalculationResult | null>(null)
  const [priceIsManual, setPriceIsManual] = useState(order?.priceIsManual ?? false)
  const [priceOverrideReason, setPriceOverrideReason] = useState(order?.priceOverrideReason ?? '')

  // --- One-off order pricing (Phase 6): the order carries its own price agreement, no contract ---
  const [pricingSource, setPricingSource] = useState<'Contract' | 'OneOff'>(order?.pricingSource ?? 'Contract')
  const [oneOffFixedAmount, setOneOffFixedAmount] = useState(
    order?.oneOffFixedAmount === null || order?.oneOffFixedAmount === undefined ? '' : String(order.oneOffFixedAmount),
  )
  const [oneOffTimeMode, setOneOffTimeMode] = useState<'none' | 'separate' | 'combined'>(() =>
    order?.oneOffIncludedCombinedMinutes != null
      ? 'combined'
      : order?.oneOffIncludedLoadingMinutes != null || order?.oneOffIncludedUnloadingMinutes != null
        ? 'separate'
        : 'none',
  )
  const [oneOffIncludedLoadingMinutes, setOneOffIncludedLoadingMinutes] = useState(
    order?.oneOffIncludedLoadingMinutes == null ? '' : String(order.oneOffIncludedLoadingMinutes),
  )
  const [oneOffIncludedUnloadingMinutes, setOneOffIncludedUnloadingMinutes] = useState(
    order?.oneOffIncludedUnloadingMinutes == null ? '' : String(order.oneOffIncludedUnloadingMinutes),
  )
  const [oneOffIncludedCombinedMinutes, setOneOffIncludedCombinedMinutes] = useState(
    order?.oneOffIncludedCombinedMinutes == null ? '' : String(order.oneOffIncludedCombinedMinutes),
  )
  const [oneOffExtraHourlyRate, setOneOffExtraHourlyRate] = useState(
    order?.oneOffExtraHourlyRate == null ? '' : String(order.oneOffExtraHourlyRate),
  )
  const [oneOffNotes, setOneOffNotes] = useState(order?.oneOffNotes ?? '')

  // --- Task 11: order-level included-time overrides (contract mode) — "Laad- en lostijd" ---
  const [includedLoadingMinutesOverride, setIncludedLoadingMinutesOverride] = useState(
    order?.includedLoadingMinutesOverride == null ? '' : String(order.includedLoadingMinutesOverride),
  )
  const [includedUnloadingMinutesOverride, setIncludedUnloadingMinutesOverride] = useState(
    order?.includedUnloadingMinutesOverride == null ? '' : String(order.includedUnloadingMinutesOverride),
  )
  const [extraTimeHourlyRateOverride, setExtraTimeHourlyRateOverride] = useState(
    order?.extraTimeHourlyRateOverride == null ? '' : String(order.extraTimeHourlyRateOverride),
  )
  const [extraTimeRoundingStepMinutes, setExtraTimeRoundingStepMinutes] = useState(
    order?.extraTimeRoundingStepMinutes == null ? '' : String(order.extraTimeRoundingStepMinutes),
  )
  const [extraTimeMinimumBillableMinutes, setExtraTimeMinimumBillableMinutes] = useState(
    order?.extraTimeMinimumBillableMinutes == null ? '' : String(order.extraTimeMinimumBillableMinutes),
  )
  // "Afwijken voor deze order" panel: starts open when the order already carries an override.
  const [includedTimeOverrideOpen, setIncludedTimeOverrideOpen] = useState(
    () => order?.includedLoadingMinutesOverride != null || order?.includedUnloadingMinutesOverride != null
      || order?.extraTimeHourlyRateOverride != null || order?.extraTimeRoundingStepMinutes != null
      || order?.extraTimeMinimumBillableMinutes != null,
  )

  useEffect(() => {
    let mounted = true
    // Only active services that are selectable during order entry (spec §20).
    listServiceOptions(false, true)
      .then((data) => {
        if (mounted) setServiceOptions(data)
      })
      .catch(() => {})
    // Physical defaults for dimension autofill; unavailable (e.g. no permission) is fine.
    listUnitTypeMaster()
      .then((data) => {
        if (mounted) setUnitMaster(data)
      })
      .catch(() => {})
    // Warehouses map stop locations onto warehouse-conditioned services so the PREVIEW matches
    // what the save will charge; unavailable (no permission) degrades to the save-time result.
    listWarehouses()
      .then((data) => {
        if (mounted) setWarehouses(data)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  useEffect(() => {
    if (!customerId) return
    let mounted = true
    getCustomerPricingConfig(customerId)
      .then((config) => {
        if (mounted) setPricingConfig({ customerId, config })
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [customerId])

  const customerConfig = pricingConfig?.customerId === customerId ? pricingConfig.config : null
  const preferredUnits = customerConfig?.preferredUnits ?? []
  const customerServiceById = new Map(
    (customerConfig?.serviceOptions ?? []).map((o) => [o.serviceOptionId, o]),
  )
  // Options currently rendered as read-only "Automatisch" rows (from the live preview) must not
  // also appear as a manually-selectable checkbox below — that would duplicate the row.
  const autoAppliedServiceOptionIds = new Set(
    (preview?.serviceLines ?? []).filter((line) => line.autoApplied).map((line) => line.serviceOptionId),
  )
  // Effective services for this order: globally selectable minus customer-disabled ones minus
  // ones already shown as auto-applied.
  const availableServiceOptions = serviceOptions.filter(
    (o) => customerServiceById.get(o.id)?.disabled !== true && !autoAppliedServiceOptionIds.has(o.id),
  )
  // Manually-ticked services rendered under "Handmatig geselecteerd" — never one that meanwhile
  // became auto-applied (that one only shows once, in the "Automatisch toegepast" block).
  const manuallySelectedServiceOptionIds = selectedServiceOptionIds.filter((id) => !autoAppliedServiceOptionIds.has(id))
  // Not-yet-selected options offered by the "+ Dienst of toeslag toevoegen" panel.
  const addableServiceOptions = availableServiceOptions.filter((o) => !selectedServiceOptionIds.includes(o.id))
  const serviceSelections = () =>
    selectedServiceOptionIds.map((id) => {
      const kind = serviceOptions.find((o) => o.id === id)?.kind
      const quantity = serviceQuantities[id]?.trim() ? Number(serviceQuantities[id]) : null
      const pallets = servicePallets[id]?.trim() ? Number(servicePallets[id]) : null
      const days = serviceDays[id]?.trim() ? Number(serviceDays[id]) : null
      const note = serviceNotes[id]?.trim() ? serviceNotes[id].trim() : null
      if (kind === 'PerDay') {
        // The day count IS the billable quantity for a per-day service.
        return { serviceOptionId: id, quantity: days, dayCount: days, note }
      }
      if (kind === 'PerPalletDay') {
        return { serviceOptionId: id, quantity, palletCount: pallets, dayCount: days, note }
      }
      return { serviceOptionId: id, quantity, note }
    })
  const withoutKey = (record: Record<string, string>, id: string): Record<string, string> => {
    const next = { ...record }
    delete next[id]
    return next
  }
  // Removing a manually selected service unticks it AND drops its entered inputs so a later
  // re-add starts clean instead of resurrecting stale values.
  const removeSelectedService = (id: string) => {
    setSelectedServiceOptionIds((ids) => ids.filter((existing) => existing !== id))
    setServiceQuantities((q) => withoutKey(q, id))
    setServicePallets((q) => withoutKey(q, id))
    setServiceDays((q) => withoutKey(q, id))
    setServiceNotes((q) => withoutKey(q, id))
  }
  // Adds the option currently configured in the "+ Dienst of toeslag toevoegen" panel to the
  // manually-selected set; its quantity/pallet/day/note inputs already live in the shared state
  // maps (the panel writes into them directly), so only the id needs to move.
  const addDraftService = () => {
    if (!draftServiceOptionId) return
    setSelectedServiceOptionIds((ids) => (ids.includes(draftServiceOptionId) ? ids : [...ids, draftServiceOptionId]))
    setDraftServiceOptionId('')
    setAddServicePanelOpen(false)
  }

  // One-off pricing input built from the form state; undefined when Contract mode (nothing to send).
  const oneOffPreviewInput = () =>
    pricingSource !== 'OneOff' || oneOffFixedAmount.trim() === ''
      ? null
      : {
          fixedAmount: Number(oneOffFixedAmount),
          includedLoadingMinutes: oneOffTimeMode === 'separate' && oneOffIncludedLoadingMinutes.trim()
            ? Number(oneOffIncludedLoadingMinutes)
            : null,
          includedUnloadingMinutes: oneOffTimeMode === 'separate' && oneOffIncludedUnloadingMinutes.trim()
            ? Number(oneOffIncludedUnloadingMinutes)
            : null,
          includedCombinedMinutes: oneOffTimeMode === 'combined' && oneOffIncludedCombinedMinutes.trim()
            ? Number(oneOffIncludedCombinedMinutes)
            : null,
          extraHourlyRate: oneOffExtraHourlyRate.trim() ? Number(oneOffExtraHourlyRate) : null,
          notes: oneOffNotes.trim() || null,
        }

  // Live price preview, debounced on the pricing-relevant inputs.
  const lastUnloading = [...stops].reverse().find((s) => s.stopType === 'Unloading')
  // Warehouses the order touches (stop at a warehouse's master location) — mirrors the
  // save-time derivation so warehouse-conditioned services preview identically.
  const touchedWarehouseIds = warehouses
    .filter((w) => stops.some((s) => s.locationId !== null && s.locationId === w.locationId))
    .map((w) => w.id)
  const previewKey = JSON.stringify([
    customerId, orderDate, quantity, quantityUnitCode, weightKg, palletCount,
    lastUnloading?.postalCode, lastUnloading?.countryCode, selectedServiceOptionIds, serviceQuantities,
    servicePallets, serviceDays, touchedWarehouseIds,
    adrRequired, cargoItems.length,
    pricingSource, oneOffFixedAmount, oneOffTimeMode, oneOffIncludedLoadingMinutes, oneOffIncludedUnloadingMinutes,
    oneOffIncludedCombinedMinutes, oneOffExtraHourlyRate, oneOffNotes,
  ])
  useEffect(() => {
    const unitTypeId = unitOptions.find((u) => u.code === quantityUnitCode)?.id ?? null
    const lines = unitTypeId && quantity !== '' && Number(quantity) > 0
      ? [{ unitTypeId, quantity: Number(quantity) }]
      : []
    const oneOff = oneOffPreviewInput()
    const shouldPreview = Boolean(customerId)
      && (lines.length > 0 || selectedServiceOptionIds.length > 0 || cargoItems.length > 0 || adrRequired || oneOff !== null)
    const timer = window.setTimeout(() => {
      if (!shouldPreview) {
        setPreview(null)
        return
      }
      previewPrice({
        customerId,
        date: orderDate || new Date().toISOString().slice(0, 10),
        lines,
        deliveryCountryCode: lastUnloading?.countryCode || null,
        deliveryPostalCode: lastUnloading?.postalCode || null,
        weightKg: weightKg === '' ? null : Number(weightKg),
        distanceKm: null,
        palletCount: palletCount === '' ? null : Number(palletCount),
        serviceOptionIds: selectedServiceOptionIds,
        services: serviceSelections(),
        adrRequired,
        cargoLineCount: cargoItems.length > 0 ? cargoItems.length : null,
        oneOff,
        warehouseIds: touchedWarehouseIds.length > 0 ? touchedWarehouseIds : null,
      })
        .then(setPreview)
        .catch(() => setPreview(null))
    }, shouldPreview ? 400 : 0)
    return () => window.clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [previewKey, unitOptions.length])

  useEffect(() => {
    let mounted = true
    searchCustomers({ isActive: true, page: 1, pageSize: 200 })
      .then((data) => {
        if (mounted) setCustomers(data.items)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  // Facturerende entiteit is optioneel; bij laadfout of ontbrekende rechten blijft alleen "klantstandaard".
  useEffect(() => {
    let mounted = true
    getLegalEntityOptions()
      .then((data) => {
        if (mounted) setLegalEntities(data)
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [])

  // Surface the selected customer's intake requirements (reference/PO/signed CMR) as hints.
  useEffect(() => {
    if (!customerId) return
    let mounted = true
    getCustomer(customerId)
      .then((detail) => {
        if (mounted) setLoadedCustomerDetail({ id: customerId, detail })
      })
      .catch(() => {})
    return () => {
      mounted = false
    }
  }, [customerId])

  // Derive so a stale detail (from a previously selected customer) is never shown.
  const customerRequirements = loadedCustomerDetail?.id === customerId ? loadedCustomerDetail.detail : null
  const requirementHints = customerRequirements
    ? [
        customerRequirements.customerReferenceRequired ? 'een klantreferentie is verplicht' : null,
        customerRequirements.purchaseOrderRequired ? 'een bestelbon (PO) is vereist' : null,
        customerRequirements.signedDeliveryNoteRequired ? 'een getekende leverbon (CMR) is vereist' : null,
      ].filter((hint): hint is string => hint !== null)
    : []

  function setStop(key: string, patch: Partial<StopFormRow>) {
    setStops((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

  function setCargo(key: string, patch: Partial<CargoFormRow>) {
    setCargoItems((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

  const masterByCode = (code: string | null) =>
    code ? unitMaster.find((u) => u.code.toUpperCase() === code.toUpperCase()) ?? null : null

  /** Fixed dimensions come from the unit definition and are not editable per order line. */
  const cargoDimensionsFixed = (cargo: CargoFormRow) =>
    masterByCode(cargo.quantityUnitCode)?.dimensionBehavior === 'Fixed'

  /**
   * Selecting a unit auto-fills the physical defaults from the unit master data (spec §2.2):
   * Fixed always sets them, DefaultButOverridable only fills what is still empty, Variable
   * leaves everything to the planner.
   */
  function applyCargoUnit(key: string, code: string | null) {
    const master = masterByCode(code)
    setCargoItems((rows) =>
      rows.map((row) => {
        if (row.key !== key) return row
        const next: CargoFormRow = { ...row, quantityUnitCode: code }
        if (!master || master.dimensionBehavior === 'Variable') return next
        const fixed = master.dimensionBehavior === 'Fixed'
        const cmToM = (cm: number | null) => (cm === null ? null : String(cm / 100))
        const fill = (current: string, cm: number | null) => {
          const value = cmToM(cm)
          if (value === null) return fixed ? '' : current
          return fixed || current.trim() === '' ? value : current
        }
        next.lengthMeters = fill(row.lengthMeters, master.defaultLengthCm)
        next.widthMeters = fill(row.widthMeters, master.defaultWidthCm)
        next.heightMeters = fill(row.heightMeters, master.defaultHeightCm)
        if (master.defaultWeightKg !== null && (fixed || row.weightPerUnitKg.trim() === '')) {
          next.weightPerUnitKg = String(master.defaultWeightKg)
        }
        return next
      }),
    )
  }

  function moveStop(index: number, delta: number) {
    setStops((rows) => {
      const target = index + delta
      if (target < 0 || target >= rows.length) return rows
      const next = [...rows]
      ;[next[index], next[target]] = [next[target], next[index]]
      return next
    })
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)
    if (!customerId) {
      setFormError('Selecteer een klant.')
      return
    }
    for (const stop of stops) {
      if (!stop.locationId && !stop.city.trim()) {
        setFormError('Elke stop heeft een locatie of minstens een plaatsnaam nodig.')
        return
      }
      const windowPairs: Array<[string, string]> = [
        [stop.plannedFrom, stop.plannedTo],
        [stop.requestedFrom, stop.requestedTo],
        [stop.confirmedFrom, stop.confirmedTo],
      ]
      if (windowPairs.some(([from, to]) => from && to && to < from)) {
        setFormError('Het einde van een tijdvenster moet na het begin liggen.')
        return
      }
      if (stop.earliestAllowed && stop.latestAllowed && stop.latestAllowed < stop.earliestAllowed) {
        setFormError('Het uiterste tijdstip moet na het vroegst toegelaten tijdstip liggen.')
        return
      }
    }
    for (const cargo of cargoItems) {
      if (cargo.expectedQuantity === '' || Number(cargo.expectedQuantity) <= 0) {
        setFormError('De verwachte hoeveelheid van een goederenlijn moet groter dan nul zijn.')
        return
      }
    }
    if (!goodsDescription.trim() && !cargoItems.some((c) => c.description.trim())) {
      setFormError('Geef minstens één omschrijving van de goederen op: algemeen of per goederenlijn.')
      return
    }
    const barcodes = cargoItems.map((c) => c.barcode.trim().toLowerCase()).filter(Boolean)
    if (barcodes.length !== new Set(barcodes).size) {
      setFormError('Een barcode mag maar één keer voorkomen binnen dezelfde opdracht.')
      return
    }
    if (dieselSurchargeOverride && !dieselSurchargeOverrideReason.trim()) {
      setFormError('Geef een reden op voor het afwijkende dieseltoeslagpercentage.')
      return
    }
    if (priceIsManual && !priceOverrideReason.trim()) {
      setActive('prijs')
      setFormError('Geef een reden op voor de handmatige prijs.')
      return
    }
    if (pricingSource === 'OneOff' && oneOffFixedAmount.trim() === '') {
      setActive('prijs')
      setFormError('Geef het vaste bedrag van de eenmalige prijsafspraak op.')
      return
    }

    const input: TransportOrderInput = {
      customerId,
      customerReference: customerReference.trim() || null,
      orderDate: orderDate || null,
      goodsDescription: goodsDescription.trim() || null,
      quantity: quantity === '' ? null : Number(quantity),
      quantityUnit: quantityUnit.trim() || null,
      quantityUnitCode: quantityUnitCode,
      weightKg: weightKg === '' ? null : Number(weightKg),
      volumeM3: volumeM3 === '' ? null : Number(volumeM3),
      palletCount: palletCount === '' ? null : Number(palletCount),
      adrRequired,
      craneRequired,
      agreedPrice: agreedPrice === '' ? null : Number(agreedPrice),
      notes: notes.trim() || null,
      legalEntityId: legalEntityId || null,
      dieselSurchargeOverride,
      dieselSurchargePercentOverride: dieselSurchargeOverride ? numberOrNullFrom(dieselSurchargePercentOverride) : null,
      dieselSurchargeOverrideReason: dieselSurchargeOverride ? dieselSurchargeOverrideReason.trim() || null : null,
      serviceOptionIds: selectedServiceOptionIds,
      services: serviceSelections(),
      priceIsManual,
      priceOverrideReason: priceIsManual ? priceOverrideReason.trim() || null : null,
      pricingSource,
      oneOffFixedAmount: pricingSource === 'OneOff' && oneOffFixedAmount.trim() ? Number(oneOffFixedAmount) : null,
      oneOffIncludedLoadingMinutes: pricingSource === 'OneOff' && oneOffTimeMode === 'separate' && oneOffIncludedLoadingMinutes.trim()
        ? Number(oneOffIncludedLoadingMinutes)
        : null,
      oneOffIncludedUnloadingMinutes: pricingSource === 'OneOff' && oneOffTimeMode === 'separate' && oneOffIncludedUnloadingMinutes.trim()
        ? Number(oneOffIncludedUnloadingMinutes)
        : null,
      oneOffIncludedCombinedMinutes: pricingSource === 'OneOff' && oneOffTimeMode === 'combined' && oneOffIncludedCombinedMinutes.trim()
        ? Number(oneOffIncludedCombinedMinutes)
        : null,
      oneOffExtraHourlyRate: pricingSource === 'OneOff' && oneOffExtraHourlyRate.trim() ? Number(oneOffExtraHourlyRate) : null,
      oneOffNotes: pricingSource === 'OneOff' ? oneOffNotes.trim() || null : null,
      includedLoadingMinutesOverride: pricingSource === 'OneOff' ? null : numberOrNullFrom(includedLoadingMinutesOverride),
      includedUnloadingMinutesOverride: pricingSource === 'OneOff' ? null : numberOrNullFrom(includedUnloadingMinutesOverride),
      extraTimeHourlyRateOverride: pricingSource === 'OneOff' ? null : numberOrNullFrom(extraTimeHourlyRateOverride),
      extraTimeRoundingStepMinutes: pricingSource === 'OneOff' ? null : numberOrNullFrom(extraTimeRoundingStepMinutes),
      extraTimeMinimumBillableMinutes: pricingSource === 'OneOff' ? null : numberOrNullFrom(extraTimeMinimumBillableMinutes),
      stops: stops.map((stop) => ({
        stopType: stop.stopType,
        locationId: stop.locationId || null,
        locationName: stop.locationId ? null : stop.locationName.trim() || null,
        address: stop.address.trim() || null,
        postalCode: stop.postalCode.trim() || null,
        city: stop.city.trim() || null,
        countryCode: stop.countryCode.trim() || null,
        plannedFrom: stop.plannedFrom ? `${stop.plannedFrom}:00Z` : null,
        plannedTo: stop.plannedTo ? `${stop.plannedTo}:00Z` : null,
        requestedFrom: stop.requestedFrom ? `${stop.requestedFrom}:00Z` : null,
        requestedTo: stop.requestedTo ? `${stop.requestedTo}:00Z` : null,
        confirmedFrom: stop.confirmedFrom ? `${stop.confirmedFrom}:00Z` : null,
        confirmedTo: stop.confirmedTo ? `${stop.confirmedTo}:00Z` : null,
        earliestAllowed: stop.earliestAllowed ? `${stop.earliestAllowed}:00Z` : null,
        latestAllowed: stop.latestAllowed ? `${stop.latestAllowed}:00Z` : null,
        appointmentRequired: stop.appointmentRequired,
        appointmentReference: stop.appointmentReference.trim() || null,
        reference: stop.reference.trim() || null,
        instructions: stop.instructions.trim() || null,
        accessInstructions: stop.accessInstructions.trim() || null,
        loadingInstructions: stop.loadingInstructions.trim() || null,
        unloadingInstructions: stop.unloadingInstructions.trim() || null,
      })),
      cargoItems: cargoItems.map((cargo) => ({
        id: cargo.id,
        description: cargo.description.trim() || null,
        barcode: cargo.barcode.trim() || null,
        expectedQuantity: Number(cargo.expectedQuantity),
        quantityUnit: cargo.quantityUnit.trim() || null,
        quantityUnitCode: cargo.quantityUnitCode,
        notes: cargo.notes.trim() || null,
        unitType: cargo.unitType || null,
        unitTypeLabel: cargo.unitType === 'Other' ? cargo.unitTypeLabel.trim() || null : null,
        totalWeightKg: numberOrNullFrom(cargo.totalWeightKg),
        weightPerUnitKg: numberOrNullFrom(cargo.weightPerUnitKg),
        lengthMeters: numberOrNullFrom(cargo.lengthMeters),
        widthMeters: numberOrNullFrom(cargo.widthMeters),
        heightMeters: numberOrNullFrom(cargo.heightMeters),
        volumeM3: cargo.volumeIsManual
          ? numberOrNullFrom(cargo.volumeM3)
          : computeVolumeM3(
              numberOrNullFrom(cargo.lengthMeters),
              numberOrNullFrom(cargo.widthMeters),
              numberOrNullFrom(cargo.heightMeters),
            ),
        volumeIsManual: cargo.volumeIsManual,
        adrRequired: cargo.adrRequired,
        adrDetails: cargo.adrRequired ? cargo.adrDetails.trim() || null : null,
        stackable: cargo.stackable,
        reference: cargo.reference.trim() || null,
        loadingStopIndex: cargo.loadingStopIndex === '' ? null : Number(cargo.loadingStopIndex),
        unloadingStopIndex: cargo.unloadingStopIndex === '' ? null : Number(cargo.unloadingStopIndex),
      })),
    }

    setSaving(true)
    try {
      await onSubmit(input)
    } catch (err) {
      setFormError(
        err instanceof ApiError && err.status === 400
          ? 'De opdracht kon niet worden opgeslagen — controleer de invoer.'
          : 'De opdracht kon niet worden opgeslagen.',
      )
    } finally {
      setSaving(false)
    }
  }

  const algemeenContent = (
    <>
      <div className="tof-row">
        <FormField label="Klant" htmlFor="to-customer" required>
          <select id="to-customer" value={customerId} onChange={(e) => setCustomerId(e.target.value)} disabled={saving}>
            <option value="">Selecteer een klant…</option>
            {/* The list offers only active customers; an existing order keeps its (possibly
                deactivated) customer selectable so editing never silently switches customers. */}
            {order && !customers.some((customer) => customer.id === order.customerId) && (
              <option value={order.customerId}>{order.customerName} (gedeactiveerd)</option>
            )}
            {customers.map((customer) => (
              <option key={customer.id} value={customer.id}>
                {customer.name} ({customer.customerNumber})
              </option>
            ))}
          </select>
        </FormField>
        <FormField label="Klantreferentie" htmlFor="to-ref">
          <input id="to-ref" value={customerReference} onChange={(e) => setCustomerReference(e.target.value)} disabled={saving} maxLength={100} />
        </FormField>
        <FormField label="Opdrachtdatum" htmlFor="to-date">
          <input id="to-date" type="date" value={orderDate} onChange={(e) => setOrderDate(e.target.value)} disabled={saving} />
        </FormField>
      </div>

      {requirementHints.length > 0 && (
        <p className="tof-customer-requirements" role="note">
          Let op voor deze klant: {requirementHints.join(', ')}.
        </p>
      )}
      {customerRequirements?.isBlocked && (
        <p className="tof-error" role="alert">
          Deze klant is geblokkeerd{customerRequirements.blockReason ? ` (${customerRequirements.blockReason})` : ''}; er
          kunnen geen nieuwe opdrachten voor worden aangemaakt.
        </p>
      )}

      <div className="tof-row">
        <FormField
          label="Facturerende entiteit"
          htmlFor="to-legal-entity"
          hint="Leeg = de standaardentiteit van de klant."
        >
          <select id="to-legal-entity" value={legalEntityId} onChange={(e) => setLegalEntityId(e.target.value)} disabled={saving}>
            <option value="">— Klantstandaard —</option>
            {legalEntities.map((entity) => (
              <option key={entity.id} value={entity.id}>
                {entity.displayName}
                {entity.isDefault ? ' (standaard)' : ''}
                {!entity.isActive ? ' — inactief' : ''}
              </option>
            ))}
          </select>
        </FormField>
      </div>

      <details className="tof-stop-extended" open={dieselSurchargeOverride}>
        <summary>Dieseltoeslag afwijking</summary>
        <label className="tof-checkbox">
          <input
            type="checkbox"
            checked={dieselSurchargeOverride}
            onChange={(e) => setDieselSurchargeOverride(e.target.checked)}
            disabled={saving}
          />
          Afwijkend percentage voor deze opdracht
        </label>
        {dieselSurchargeOverride && (
          <div className="tof-row">
            <FormField label="Percentage (%)" htmlFor="to-diesel-pct">
              <input
                id="to-diesel-pct"
                type="number"
                min={0}
                max={100}
                step="0.01"
                value={dieselSurchargePercentOverride}
                onChange={(e) => setDieselSurchargePercentOverride(e.target.value)}
                disabled={saving}
              />
            </FormField>
            <FormField
              label="Reden"
              htmlFor="to-diesel-reason"
              required
              hint="De klantconfiguratie is de standaard; afwijkingen worden gelogd."
            >
              <textarea
                id="to-diesel-reason"
                rows={2}
                value={dieselSurchargeOverrideReason}
                onChange={(e) => setDieselSurchargeOverrideReason(e.target.value)}
                disabled={saving}
                maxLength={500}
              />
            </FormField>
          </div>
        )}
      </details>
    </>
  )

  const routeContent = (
    <>
      <div className="tof-stops-header">
        <h3>Stops</h3>
        <div className="tof-stops-actions">
          <Button variant="secondary" onClick={() => setStops((rows) => [...rows, emptyStop('Loading')])} disabled={saving}>
            + Laadstop
          </Button>
          <Button variant="secondary" onClick={() => setStops((rows) => [...rows, emptyStop('Unloading')])} disabled={saving}>
            + Losstop
          </Button>
        </div>
      </div>

      {stops.map((stop, index) => (
        <fieldset key={stop.key} className="tof-stop">
          <legend>
            {index + 1}. {STOP_TYPE_LABELS[stop.stopType]}
          </legend>
          <div className="tof-stop-toolbar">
            <select
              value={stop.stopType}
              onChange={(e) => setStop(stop.key, { stopType: e.target.value as StopInput['stopType'] })}
              disabled={saving}
              aria-label="Stoptype"
            >
              <option value="Loading">Laden</option>
              <option value="Unloading">Lossen</option>
            </select>
            <button type="button" className="tof-link" onClick={() => moveStop(index, -1)} disabled={saving || index === 0}>
              ↑
            </button>
            <button type="button" className="tof-link" onClick={() => moveStop(index, 1)} disabled={saving || index === stops.length - 1}>
              ↓
            </button>
            <button
              type="button"
              className="tof-link tof-link-danger"
              onClick={() => setStops((rows) => rows.filter((row) => row.key !== stop.key))}
              disabled={saving}
            >
              Verwijderen
            </button>
          </div>
          <div className="tof-row">
            <FormField label="Locatie (stamgegevens)" htmlFor={`st-loc-${stop.key}`}>
              <LocationSelect
                id={`st-loc-${stop.key}`}
                value={stop.locationId}
                onChange={(locationId) => setStop(stop.key, { locationId })}
                customerId={customerId || undefined}
                disabled={saving}
                placeholder="Geen — adres hieronder"
                onCreateNew={
                  customerId && canCreateLocations
                    ? (name) => new Promise<LocationOption | null>((resolve) => setQuickCreate({ name, resolve }))
                    : undefined
                }
              />
            </FormField>
            <FormField label="Naam (vrij adres)" htmlFor={`st-name-${stop.key}`}>
              <input
                id={`st-name-${stop.key}`}
                value={stop.locationName}
                onChange={(e) => setStop(stop.key, { locationName: e.target.value })}
                disabled={saving || stop.locationId !== ''}
                maxLength={200}
              />
            </FormField>
          </div>
          {stop.locationId === '' && (
            <div className="tof-row tof-row-4">
              <FormField label="Adres" htmlFor={`st-addr-${stop.key}`}>
                <input id={`st-addr-${stop.key}`} value={stop.address} onChange={(e) => setStop(stop.key, { address: e.target.value })} disabled={saving} maxLength={300} />
              </FormField>
              <FormField label="Postcode" htmlFor={`st-pc-${stop.key}`}>
                <input id={`st-pc-${stop.key}`} value={stop.postalCode} onChange={(e) => setStop(stop.key, { postalCode: e.target.value })} disabled={saving} maxLength={20} />
              </FormField>
              <FormField label="Plaats" htmlFor={`st-city-${stop.key}`} required>
                <input id={`st-city-${stop.key}`} value={stop.city} onChange={(e) => setStop(stop.key, { city: e.target.value })} disabled={saving} maxLength={100} />
              </FormField>
              <FormField label="Land" htmlFor={`st-cc-${stop.key}`}>
                <CountryCombobox
                  id={`st-cc-${stop.key}`}
                  value={stop.countryCode || null}
                  onChange={(code) => setStop(stop.key, { countryCode: code ?? '' })}
                  disabled={saving}
                />
              </FormField>
            </div>
          )}
          <div className="tof-row tof-row-4">
            <FormField label="Venster van" htmlFor={`st-from-${stop.key}`}>
              <input id={`st-from-${stop.key}`} type="datetime-local" value={stop.plannedFrom} onChange={(e) => setStop(stop.key, { plannedFrom: e.target.value })} disabled={saving} />
            </FormField>
            <FormField label="Venster tot" htmlFor={`st-to-${stop.key}`}>
              <input id={`st-to-${stop.key}`} type="datetime-local" value={stop.plannedTo} onChange={(e) => setStop(stop.key, { plannedTo: e.target.value })} disabled={saving} />
            </FormField>
            <FormField label="Referentie" htmlFor={`st-ref-${stop.key}`}>
              <input id={`st-ref-${stop.key}`} value={stop.reference} onChange={(e) => setStop(stop.key, { reference: e.target.value })} disabled={saving} maxLength={100} />
            </FormField>
            <FormField label="Instructies" htmlFor={`st-instr-${stop.key}`}>
              <input id={`st-instr-${stop.key}`} value={stop.instructions} onChange={(e) => setStop(stop.key, { instructions: e.target.value })} disabled={saving} maxLength={2000} />
            </FormField>
          </div>
          <details
            className="tof-stop-extended"
            open={Boolean(
              stop.requestedFrom || stop.requestedTo || stop.confirmedFrom || stop.confirmedTo ||
              stop.earliestAllowed || stop.latestAllowed || stop.appointmentRequired ||
              stop.accessInstructions || stop.loadingInstructions || stop.unloadingInstructions,
            )}
          >
            <summary>Tijdvensters, afspraak &amp; instructies</summary>
            <div className="tof-row tof-row-4">
              <FormField label="Gevraagd van" htmlFor={`st-reqfrom-${stop.key}`} hint="Venster gevraagd door de klant">
                <input id={`st-reqfrom-${stop.key}`} type="datetime-local" value={stop.requestedFrom} onChange={(e) => setStop(stop.key, { requestedFrom: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Gevraagd tot" htmlFor={`st-reqto-${stop.key}`}>
                <input id={`st-reqto-${stop.key}`} type="datetime-local" value={stop.requestedTo} onChange={(e) => setStop(stop.key, { requestedTo: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Bevestigd van" htmlFor={`st-conffrom-${stop.key}`} hint="Venster bevestigd aan de klant">
                <input id={`st-conffrom-${stop.key}`} type="datetime-local" value={stop.confirmedFrom} onChange={(e) => setStop(stop.key, { confirmedFrom: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Bevestigd tot" htmlFor={`st-confto-${stop.key}`}>
                <input id={`st-confto-${stop.key}`} type="datetime-local" value={stop.confirmedTo} onChange={(e) => setStop(stop.key, { confirmedTo: e.target.value })} disabled={saving} />
              </FormField>
            </div>
            <div className="tof-row tof-row-4">
              <FormField label="Vroegst toegelaten" htmlFor={`st-earliest-${stop.key}`}>
                <input id={`st-earliest-${stop.key}`} type="datetime-local" value={stop.earliestAllowed} onChange={(e) => setStop(stop.key, { earliestAllowed: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Uiterste tijdstip" htmlFor={`st-latest-${stop.key}`} hint="Na dit tijdstip is een reden voor late aankomst verplicht">
                <input id={`st-latest-${stop.key}`} type="datetime-local" value={stop.latestAllowed} onChange={(e) => setStop(stop.key, { latestAllowed: e.target.value })} disabled={saving} />
              </FormField>
              <label className="tof-checkbox">
                <input type="checkbox" checked={stop.appointmentRequired} onChange={(e) => setStop(stop.key, { appointmentRequired: e.target.checked })} disabled={saving} />
                Afspraak verplicht
              </label>
              <FormField label="Afspraakreferentie" htmlFor={`st-appref-${stop.key}`}>
                <input id={`st-appref-${stop.key}`} value={stop.appointmentReference} onChange={(e) => setStop(stop.key, { appointmentReference: e.target.value })} disabled={saving} maxLength={100} placeholder="bv. slotnummer" />
              </FormField>
            </div>
            <div className="tof-row">
              <FormField label="Toegangsinstructies" htmlFor={`st-access-${stop.key}`}>
                <input id={`st-access-${stop.key}`} value={stop.accessInstructions} onChange={(e) => setStop(stop.key, { accessInstructions: e.target.value })} disabled={saving} maxLength={2000} />
              </FormField>
              {stop.stopType === 'Loading' ? (
                <FormField label="Laadinstructies" htmlFor={`st-loadinstr-${stop.key}`}>
                  <input id={`st-loadinstr-${stop.key}`} value={stop.loadingInstructions} onChange={(e) => setStop(stop.key, { loadingInstructions: e.target.value })} disabled={saving} maxLength={2000} />
                </FormField>
              ) : (
                <FormField label="Losinstructies" htmlFor={`st-unloadinstr-${stop.key}`}>
                  <input id={`st-unloadinstr-${stop.key}`} value={stop.unloadingInstructions} onChange={(e) => setStop(stop.key, { unloadingInstructions: e.target.value })} disabled={saving} maxLength={2000} />
                </FormField>
              )}
            </div>
          </details>
        </fieldset>
      ))}
    </>
  )

  const goederenContent = (
    <>
      <FormField label="Omschrijving goederen" htmlFor="to-goods" hint="Optioneel wanneer de goederen hieronder per lijn worden beschreven.">
        <textarea id="to-goods" rows={2} value={goodsDescription} onChange={(e) => setGoodsDescription(e.target.value)} disabled={saving} maxLength={1000} />
      </FormField>

      <div className="tof-row tof-row-4">
        <FormField label="Aantal" htmlFor="to-qty">
          <input id="to-qty" type="number" min={0} step="0.01" value={quantity} onChange={(e) => setQuantity(e.target.value)} disabled={saving} />
        </FormField>
        <FormField
          label="Eenheid"
          htmlFor="to-unit"
          hint={!quantityUnitCode && quantityUnit ? `Bestaande waarde: ${quantityUnit}` : undefined}
        >
          {/* Customer units first (favourites ★, customer label); the full active list stays reachable. */}
          <UnitSelect
            id="to-unit"
            value={quantityUnitCode}
            onChange={setQuantityUnitCode}
            units={unitOptions}
            preferredUnits={preferredUnits}
            disabled={saving}
          />
        </FormField>
        <FormField label="Gewicht (kg)" htmlFor="to-weight">
          <input id="to-weight" type="number" min={0} step="0.01" value={weightKg} onChange={(e) => setWeightKg(e.target.value)} disabled={saving} />
        </FormField>
        <FormField label="Volume (m³)" htmlFor="to-volume">
          <input id="to-volume" type="number" min={0} step="0.01" value={volumeM3} onChange={(e) => setVolumeM3(e.target.value)} disabled={saving} />
        </FormField>
      </div>

      <div className="tof-row tof-row-4">
        <FormField label="Paletten" htmlFor="to-pallets">
          <input id="to-pallets" type="number" min={0} value={palletCount} onChange={(e) => setPalletCount(e.target.value)} disabled={saving} />
        </FormField>
        <label className="tof-checkbox">
          <input type="checkbox" checked={adrRequired} onChange={(e) => setAdrRequired(e.target.checked)} disabled={saving} />
          ADR-transport
        </label>
        <label className="tof-checkbox">
          <input type="checkbox" checked={craneRequired} onChange={(e) => setCraneRequired(e.target.checked)} disabled={saving} />
          Kraan vereist
        </label>
      </div>

      <div className="tof-stops-header">
        <h3>Goederenlijnen (scanbaar)</h3>
        <div className="tof-stops-actions">
          <Button
            variant="secondary"
            onClick={() =>
              setCargoItems((rows) => [
                ...rows,
                {
                  key: nextStopKey(),
                  id: null,
                  description: '',
                  barcode: '',
                  expectedQuantity: '1',
                  quantityUnit: '',
                  quantityUnitCode: null,
                  notes: '',
                  unitType: '',
                  unitTypeLabel: '',
                  totalWeightKg: '',
                  weightPerUnitKg: '',
                  lengthMeters: '',
                  widthMeters: '',
                  heightMeters: '',
                  volumeM3: '',
                  volumeIsManual: false,
                  adrRequired: false,
                  adrDetails: '',
                  stackable: true,
                  reference: '',
                  loadingStopIndex: '',
                  unloadingStopIndex: '',
                },
              ])
            }
            disabled={saving}
          >
            + Goederenlijn
          </Button>
        </div>
      </div>
      {cargoItems.length === 0 && (
        <p className="tof-cargo-hint">
          Zonder goederenlijnen kan de chauffeur niet scannen; de opdracht blijft wel gewoon uitvoerbaar.
        </p>
      )}
      {cargoItems.map((cargo, index) => (
        <fieldset key={cargo.key} className="tof-stop">
          <legend>Lijn {index + 1}</legend>
          <div className="tof-row tof-row-4">
            <FormField label="Omschrijving" htmlFor={`cg-desc-${cargo.key}`} hint="Optioneel als de algemene omschrijving is ingevuld.">
              <input id={`cg-desc-${cargo.key}`} value={cargo.description} onChange={(e) => setCargo(cargo.key, { description: e.target.value })} disabled={saving} maxLength={300} />
            </FormField>
            <FormField label="Barcode" htmlFor={`cg-bc-${cargo.key}`}>
              <input id={`cg-bc-${cargo.key}`} value={cargo.barcode} onChange={(e) => setCargo(cargo.key, { barcode: e.target.value })} disabled={saving} maxLength={100} />
            </FormField>
            <FormField label="Verwacht aantal" htmlFor={`cg-qty-${cargo.key}`} required>
              <input id={`cg-qty-${cargo.key}`} type="number" min={0.01} step="0.01" value={cargo.expectedQuantity} onChange={(e) => setCargo(cargo.key, { expectedQuantity: e.target.value })} disabled={saving} />
            </FormField>
            <FormField
              label="Eenheid"
              htmlFor={`cg-unit-${cargo.key}`}
              hint={!cargo.quantityUnitCode && cargo.quantityUnit ? `Bestaande waarde: ${cargo.quantityUnit}` : undefined}
            >
              <UnitSelect
                id={`cg-unit-${cargo.key}`}
                value={cargo.quantityUnitCode}
                onChange={(code) => applyCargoUnit(cargo.key, code)}
                units={unitOptions}
                preferredUnits={preferredUnits}
                disabled={saving}
              />
            </FormField>
          </div>
          <div className="tof-row">
            <FormField label="Verpakkingstype" htmlFor={`cg-type-${cargo.key}`}>
              <select
                id={`cg-type-${cargo.key}`}
                value={cargo.unitType}
                onChange={(e) => setCargo(cargo.key, { unitType: e.target.value as PackageUnitType | '' })}
                disabled={saving}
              >
                <option value="">— Niet opgegeven —</option>
                {Object.entries(UNIT_TYPE_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </FormField>
            {cargo.unitType === 'Other' && (
              <FormField label="Eigen typenaam" htmlFor={`cg-typelabel-${cargo.key}`}>
                <input id={`cg-typelabel-${cargo.key}`} value={cargo.unitTypeLabel} onChange={(e) => setCargo(cargo.key, { unitTypeLabel: e.target.value })} disabled={saving} maxLength={50} />
              </FormField>
            )}
            <FormField label="Laadstop" htmlFor={`cg-load-${cargo.key}`} hint="Automatisch bij één laad- en losstop.">
              <select
                id={`cg-load-${cargo.key}`}
                value={cargo.loadingStopIndex}
                onChange={(e) => setCargo(cargo.key, { loadingStopIndex: e.target.value })}
                disabled={saving}
              >
                <option value="">— Automatisch —</option>
                {stops.map((stop, stopIndex) =>
                  stop.stopType === 'Loading' ? (
                    <option key={stop.key} value={stopIndex}>
                      {stopIndex + 1}. Laden — {stop.city || stop.locationName || 'stop'}
                    </option>
                  ) : null,
                )}
              </select>
            </FormField>
            <FormField label="Losstop" htmlFor={`cg-unload-${cargo.key}`}>
              <select
                id={`cg-unload-${cargo.key}`}
                value={cargo.unloadingStopIndex}
                onChange={(e) => setCargo(cargo.key, { unloadingStopIndex: e.target.value })}
                disabled={saving}
              >
                <option value="">— Automatisch —</option>
                {stops.map((stop, stopIndex) =>
                  stop.stopType === 'Unloading' ? (
                    <option key={stop.key} value={stopIndex}>
                      {stopIndex + 1}. Lossen — {stop.city || stop.locationName || 'stop'}
                    </option>
                  ) : null,
                )}
              </select>
            </FormField>
          </div>
          <details className="tof-stop-details">
            <summary>Gewicht, afmetingen &amp; ADR</summary>
            <div className="tof-row tof-row-4">
              <FormField label="Totaal gewicht (kg)" htmlFor={`cg-weight-${cargo.key}`}>
                <input id={`cg-weight-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.totalWeightKg} onChange={(e) => setCargo(cargo.key, { totalWeightKg: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Gewicht per stuk (kg)" htmlFor={`cg-unitweight-${cargo.key}`}>
                <input id={`cg-unitweight-${cargo.key}`} type="number" min={0} step="0.001" value={cargo.weightPerUnitKg} onChange={(e) => setCargo(cargo.key, { weightPerUnitKg: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Referentie" htmlFor={`cg-ref-${cargo.key}`}>
                <input id={`cg-ref-${cargo.key}`} value={cargo.reference} onChange={(e) => setCargo(cargo.key, { reference: e.target.value })} disabled={saving} maxLength={100} />
              </FormField>
              <FormField label="Opmerkingen" htmlFor={`cg-notes-${cargo.key}`}>
                <input id={`cg-notes-${cargo.key}`} value={cargo.notes} onChange={(e) => setCargo(cargo.key, { notes: e.target.value })} disabled={saving} maxLength={500} />
              </FormField>
            </div>
            <div className="tof-row tof-row-4">
              <FormField
                label="Lengte (m)"
                htmlFor={`cg-length-${cargo.key}`}
                hint={cargoDimensionsFixed(cargo) ? 'Vast volgens de eenheid.' : undefined}
              >
                <input id={`cg-length-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.lengthMeters} onChange={(e) => setCargo(cargo.key, { lengthMeters: e.target.value })} disabled={saving || cargoDimensionsFixed(cargo)} />
              </FormField>
              <FormField label="Breedte (m)" htmlFor={`cg-width-${cargo.key}`}>
                <input id={`cg-width-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.widthMeters} onChange={(e) => setCargo(cargo.key, { widthMeters: e.target.value })} disabled={saving || cargoDimensionsFixed(cargo)} />
              </FormField>
              <FormField label="Hoogte (m)" htmlFor={`cg-height-${cargo.key}`}>
                <input id={`cg-height-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.heightMeters} onChange={(e) => setCargo(cargo.key, { heightMeters: e.target.value })} disabled={saving || cargoDimensionsFixed(cargo)} />
              </FormField>
              <FormField
                label="Volume per stuk (m³)"
                htmlFor={`cg-volume-${cargo.key}`}
                hint={cargo.volumeIsManual ? 'Handmatige waarde.' : 'Automatisch uit L × B × H.'}
              >
                <input
                  id={`cg-volume-${cargo.key}`}
                  type="number"
                  min={0}
                  step="0.001"
                  value={
                    cargo.volumeIsManual
                      ? cargo.volumeM3
                      : (computeVolumeM3(
                          numberOrNullFrom(cargo.lengthMeters),
                          numberOrNullFrom(cargo.widthMeters),
                          numberOrNullFrom(cargo.heightMeters),
                        ) ?? '')
                  }
                  onChange={(e) => setCargo(cargo.key, { volumeM3: e.target.value })}
                  disabled={saving || !cargo.volumeIsManual}
                />
                <label className="tof-checkbox">
                  <input
                    type="checkbox"
                    checked={cargo.volumeIsManual}
                    onChange={(e) => setCargo(cargo.key, { volumeIsManual: e.target.checked })}
                    disabled={saving}
                  />
                  Handmatig
                </label>
              </FormField>
            </div>
            <div className="tof-row">
              <label className="tof-checkbox">
                <input type="checkbox" checked={cargo.adrRequired} onChange={(e) => setCargo(cargo.key, { adrRequired: e.target.checked })} disabled={saving} />
                ADR-goederen
              </label>
              {cargo.adrRequired && (
                <FormField label="ADR-details" htmlFor={`cg-adr-${cargo.key}`} hint="UN-nummer, klasse, verpakkingsgroep…">
                  <input id={`cg-adr-${cargo.key}`} value={cargo.adrDetails} onChange={(e) => setCargo(cargo.key, { adrDetails: e.target.value })} disabled={saving} maxLength={500} />
                </FormField>
              )}
              <label className="tof-checkbox">
                <input type="checkbox" checked={cargo.stackable} onChange={(e) => setCargo(cargo.key, { stackable: e.target.checked })} disabled={saving} />
                Stapelbaar
              </label>
            </div>
          </details>
          <div className="tof-stop-toolbar">
            <button
              type="button"
              className="tof-link tof-link-danger"
              onClick={() => setCargoItems((rows) => rows.filter((row) => row.key !== cargo.key))}
              disabled={saving}
            >
              Verwijderen
            </button>
          </div>
        </fieldset>
      ))}

    </>
  )

  const effectiveOptionPrice = (option: ServiceOption): number => {
    const customerService = customerServiceById.get(option.id)
    return customerService?.effectiveValue ?? customerService?.customerValue ?? option.defaultValue
  }

  // Auto-applied (contract) services from the live preview: the engine added them without a
  // manual selection, quantified from the order itself — shown read-only, never uncheckable here
  // (disabling happens via the customer's service configuration, not on the order).
  const autoAppliedServiceLines = (preview?.serviceLines ?? []).filter((line) => line.autoApplied)

  // Shared per-kind quantity input block (PerHour/PerStop, PerDay, PerPalletDay) — used both for a
  // manually-selected row and for the option currently being configured in the add panel; both
  // read/write the same serviceQuantities/servicePallets/serviceDays state keyed by option id.
  const renderServiceQuantityInputs = (option: ServiceOption) => {
    const needsQuantity = option.kind === 'PerHour' || option.kind === 'PerStop'
    const isPerDay = option.kind === 'PerDay'
    const isPerPalletDay = option.kind === 'PerPalletDay'
    const palletsValue = servicePallets[option.id] ?? ''
    const daysValue = serviceDays[option.id] ?? ''
    const palletDaysValue = serviceQuantities[option.id] ?? ''
    // Auto-derive pallet-days from pallets × days; the result stays manually correctable.
    const updatePalletDays = (pallets: string, days: string) => {
      setServicePallets((q) => ({ ...q, [option.id]: pallets }))
      setServiceDays((q) => ({ ...q, [option.id]: days }))
      const p = pallets.trim() === '' ? NaN : Number(pallets)
      const d = days.trim() === '' ? NaN : Number(days)
      if (!Number.isNaN(p) && !Number.isNaN(d)) {
        setServiceQuantities((q) => ({ ...q, [option.id]: String(p * d) }))
      }
    }
    return (
      <>
        {needsQuantity && (
          <FormField
            label={option.kind === 'PerHour' ? `Aantal uur — ${option.name}` : `Aantal stops — ${option.name}`}
            htmlFor={`svc-qty-${option.id}`}
          >
            <input
              id={`svc-qty-${option.id}`}
              type="number"
              min={0}
              step={option.kind === 'PerHour' ? 0.25 : 1}
              value={serviceQuantities[option.id] ?? ''}
              onChange={(e) => setServiceQuantities((q) => ({ ...q, [option.id]: e.target.value }))}
              disabled={saving}
            />
          </FormField>
        )}
        {isPerDay && (
          <FormField label={`Aantal dagen — ${option.name}`} htmlFor={`svc-days-${option.id}`}>
            <input
              id={`svc-days-${option.id}`}
              type="number"
              min={0}
              step={1}
              value={daysValue}
              onChange={(e) => setServiceDays((q) => ({ ...q, [option.id]: e.target.value }))}
              disabled={saving}
            />
          </FormField>
        )}
        {isPerPalletDay && (
          <div className="tof-row">
            <FormField label={`Pallets — ${option.name}`} htmlFor={`svc-pallets-${option.id}`}>
              <input
                id={`svc-pallets-${option.id}`}
                type="number"
                min={0}
                step={1}
                value={palletsValue}
                onChange={(e) => updatePalletDays(e.target.value, daysValue)}
                disabled={saving}
              />
            </FormField>
            <FormField label={`Dagen — ${option.name}`} htmlFor={`svc-pd-days-${option.id}`}>
              <input
                id={`svc-pd-days-${option.id}`}
                type="number"
                min={0}
                step={1}
                value={daysValue}
                onChange={(e) => updatePalletDays(palletsValue, e.target.value)}
                disabled={saving}
              />
            </FormField>
            <FormField
              label={`Pallet-dagen — ${option.name}`}
              htmlFor={`svc-qty-${option.id}`}
              hint={
                palletsValue.trim() && daysValue.trim()
                  ? `${palletsValue} pallets × ${daysValue} dagen = ${Number(palletsValue) * Number(daysValue)} pallet-dagen; handmatig aanpasbaar.`
                  : 'Wordt berekend als pallets × dagen; handmatig aanpasbaar.'
              }
            >
              <input
                id={`svc-qty-${option.id}`}
                type="number"
                min={0}
                step={0.5}
                value={palletDaysValue}
                onChange={(e) => setServiceQuantities((q) => ({ ...q, [option.id]: e.target.value }))}
                disabled={saving}
              />
            </FormField>
          </div>
        )}
      </>
    )
  }

  const renderServiceNoteInput = (option: ServiceOption) => (
    <FormField label={`Notitie — ${option.name}`} htmlFor={`svc-note-${option.id}`}>
      <input
        id={`svc-note-${option.id}`}
        type="text"
        value={serviceNotes[option.id] ?? ''}
        onChange={(e) => setServiceNotes((q) => ({ ...q, [option.id]: e.target.value }))}
        disabled={saving}
        placeholder="Optionele notitie, bv. afspraak met de klant"
      />
    </FormField>
  )

  const draftServiceOption = serviceOptions.find((o) => o.id === draftServiceOptionId) ?? null
  // Client-side price indication while configuring the add panel — never calls the preview
  // endpoint; the option only enters the priced selection once "Toevoegen" is clicked.
  const draftPriceIndication = (() => {
    if (!draftServiceOption) return null
    const price = effectiveOptionPrice(draftServiceOption)
    if (draftServiceOption.kind === 'Percent') return formatServiceValue(draftServiceOption.kind, price)
    if (draftServiceOption.kind === 'Fixed') return `€ ${price.toFixed(2)}`
    const quantity = draftServiceOption.kind === 'PerDay'
      ? (serviceDays[draftServiceOption.id]?.trim() ? Number(serviceDays[draftServiceOption.id]) : null)
      : (serviceQuantities[draftServiceOption.id]?.trim() ? Number(serviceQuantities[draftServiceOption.id]) : null)
    return quantity != null
      ? `€ ${(price * quantity).toFixed(2)}`
      : `${formatServiceValue(draftServiceOption.kind, price)} — vul een aantal in`
  })()

  // "Niet toegepast": informational preview lines the engine emits for a selected service that
  // currently isn't charged (disabled, missing quantity, condition not met, ...). Every such line
  // is labelled "{option.Name}: {reden}" by the engine, so matching on that prefix reliably scopes
  // this list to services without needing extra plumbing through the preview DTO.
  const notAppliedServiceLines = (preview?.lines ?? []).filter(
    (line) => line.informational && serviceOptions.some((option) => line.label.startsWith(`${option.name}: `)),
  )

  // --- Task 11: "Laad- en lostijd" — effective included time + source, order override ---
  const includedTimeInfo = preview?.includedTimeInfo ?? null
  const hasIncludedTimeOverride = [
    includedLoadingMinutesOverride, includedUnloadingMinutesOverride, extraTimeHourlyRateOverride,
    extraTimeRoundingStepMinutes, extraTimeMinimumBillableMinutes,
  ].some((value) => value.trim() !== '')
  const includedTimeSourceLabel = hasIncludedTimeOverride
    ? 'Bron: Afwijking op order'
    : includedTimeInfo?.source === 'Contract'
      ? 'Bron: Klantcontract'
      : 'Bron: Geen contractwaarde'
  // The contract's combined allowance is only shown as one row while nothing overrides it — an
  // override always targets loading/unloading separately, so setting one switches the display to
  // the two per-activity rows too (mirrors PricingEngine.ResolveIncludedTime).
  const includedCombinedMinutes = !hasIncludedTimeOverride ? includedTimeInfo?.includedCombinedMinutes ?? null : null
  const effectiveIncludedLoadingMinutes = includedLoadingMinutesOverride.trim() !== ''
    ? Number(includedLoadingMinutesOverride)
    : includedTimeInfo?.includedLoadingMinutes ?? null
  const effectiveIncludedUnloadingMinutes = includedUnloadingMinutesOverride.trim() !== ''
    ? Number(includedUnloadingMinutesOverride)
    : includedTimeInfo?.includedUnloadingMinutes ?? null
  const resetIncludedTimeOverrides = () => {
    setIncludedLoadingMinutesOverride('')
    setIncludedUnloadingMinutesOverride('')
    setExtraTimeHourlyRateOverride('')
    setExtraTimeRoundingStepMinutes('')
    setExtraTimeMinimumBillableMinutes('')
  }

  const servicesContent = (
    <>
      <p className="ui-form-section-description">
        Gekozen diensten tellen mee in de prijsberekening en worden bij facturatie aparte factuurlijnen. De effectieve
        prijs komt uit de klantenfiche (klanttarief) of de algemene standaard.
      </p>

      <div className="tof-service-group">
        <h4>Automatisch toegepast</h4>
        {autoAppliedServiceLines.length === 0 && (
          <p className="placeholder-text">Geen automatisch toegepaste diensten voor deze opdracht.</p>
        )}
        {autoAppliedServiceLines.length > 0 && (
          <div className="tof-service-options">
            {autoAppliedServiceLines.map((line) => (
              <div key={line.serviceOptionId} className="tof-service-option">
                <label className="tof-checkbox">
                  <input type="checkbox" checked readOnly disabled />
                  <span>
                    {line.name} <Badge>AUTO</Badge>{' '}
                    <span className="customer-form-muted">
                      ({formatServiceValue(line.kind, line.value)}
                      {line.quantity != null ? ` × ${line.quantity}` : ''} = € {line.amount.toFixed(2)})
                    </span>
                  </span>
                </label>
              </div>
            ))}
          </div>
        )}
      </div>

      <div className="tof-service-group">
        <h4>Handmatig geselecteerd</h4>
        {manuallySelectedServiceOptionIds.length === 0 && (
          <p className="placeholder-text">Nog geen diensten of toeslagen handmatig geselecteerd.</p>
        )}
        {manuallySelectedServiceOptionIds.length > 0 && (
          <div className="tof-service-options">
            {manuallySelectedServiceOptionIds.map((id) => {
              const option = serviceOptions.find((o) => o.id === id)
              if (!option) return null
              const source = customerServiceById.get(option.id)?.source ?? 'Algemene standaard'
              return (
                <div key={id} className="tof-service-option">
                  <div className="tof-service-option-header">
                    <span>
                      {option.name} <Badge tone="info">MANUEEL</Badge>{' '}
                      <span className="customer-form-muted">
                        ({SERVICE_KIND_LABELS[option.kind]} — {formatServiceValue(option.kind, effectiveOptionPrice(option))} — {source})
                      </span>
                    </span>
                    <button
                      type="button"
                      className="tof-link tof-link-danger"
                      onClick={() => removeSelectedService(id)}
                      disabled={saving}
                    >
                      Verwijderen
                    </button>
                  </div>
                  {renderServiceQuantityInputs(option)}
                  {renderServiceNoteInput(option)}
                </div>
              )
            })}
          </div>
        )}

        <div className="tof-stop-toolbar">
          <Button variant="secondary" onClick={() => setAddServicePanelOpen((open) => !open)} disabled={saving}>
            + Dienst of toeslag toevoegen
          </Button>
        </div>

        {addServicePanelOpen && (
          <div className="tof-service-add-panel">
            {addableServiceOptions.length === 0 && (
              <p className="placeholder-text">Geen extra diensten meer beschikbaar voor deze klant.</p>
            )}
            <FormField label="Dienst of toeslag" htmlFor="svc-add-select">
              <select
                id="svc-add-select"
                value={draftServiceOptionId}
                onChange={(e) => {
                  const id = e.target.value
                  setDraftServiceOptionId(id)
                  const option = serviceOptions.find((o) => o.id === id)
                  if (option?.kind === 'PerStop' && !serviceQuantities[id]) {
                    // Sensible default: every unloading stop beyond the first is an extra stop.
                    const extraStops = Math.max(0, stops.filter((s) => s.stopType === 'Unloading').length - 1)
                    if (extraStops > 0) {
                      setServiceQuantities((q) => ({ ...q, [id]: String(extraStops) }))
                    }
                  }
                  if (option?.kind === 'PerPalletDay' && !servicePallets[id] && palletCount.trim()) {
                    // Sensible default: the order's pallet-place count.
                    setServicePallets((q) => ({ ...q, [id]: palletCount }))
                    const days = serviceDays[id]?.trim() ? Number(serviceDays[id]) : NaN
                    const pallets = Number(palletCount)
                    if (!Number.isNaN(pallets) && !Number.isNaN(days)) {
                      setServiceQuantities((q) => ({ ...q, [id]: String(pallets * days) }))
                    }
                  }
                }}
                disabled={saving}
              >
                <option value="">Kies een dienst of toeslag…</option>
                {addableServiceOptions.map((option) => (
                  <option key={option.id} value={option.id}>
                    {option.name}
                  </option>
                ))}
              </select>
            </FormField>
            {draftServiceOption && (
              <>
                <FormField label="Berekeningswijze" htmlFor="svc-add-kind">
                  <input id="svc-add-kind" type="text" value={SERVICE_KIND_LABELS[draftServiceOption.kind]} readOnly disabled />
                </FormField>
                {renderServiceQuantityInputs(draftServiceOption)}
                {renderServiceNoteInput(draftServiceOption)}
                <p className="customer-form-muted">Prijsindicatie: {draftPriceIndication}</p>
              </>
            )}
            <div className="tof-stop-toolbar">
              <Button variant="secondary" onClick={addDraftService} disabled={saving || !draftServiceOptionId}>
                Toevoegen
              </Button>
              <button
                type="button"
                className="tof-link"
                onClick={() => {
                  setAddServicePanelOpen(false)
                  setDraftServiceOptionId('')
                }}
                disabled={saving}
              >
                Annuleren
              </button>
            </div>
          </div>
        )}
      </div>

      <div className="tof-service-group">
        <h4>Niet toegepast</h4>
        {notAppliedServiceLines.length === 0 ? (
          <p className="placeholder-text">Alle geselecteerde diensten worden op dit moment toegepast.</p>
        ) : (
          <ul className="tof-service-not-applied">
            {notAppliedServiceLines.map((line, index) => (
              <li key={`${line.label}-${index}`}>{line.label}</li>
            ))}
          </ul>
        )}
      </div>

      <div className="tof-service-group">
        <h4>Laad- en lostijd</h4>
        {pricingSource === 'OneOff' ? (
          <p className="ui-form-field-hint">
            Bij een eenmalige prijsafspraak gebruik je de eenmalige laad- en lostijdvelden.
          </p>
        ) : (
          <>
            {includedCombinedMinutes != null ? (
              <p>Inbegrepen laad- en lostijd (gecombineerd): {includedCombinedMinutes} minuten</p>
            ) : (
              <>
                <p>
                  Inbegrepen laadtijd: {effectiveIncludedLoadingMinutes != null ? `${effectiveIncludedLoadingMinutes} minuten` : '—'}
                </p>
                <p>
                  Inbegrepen lostijd: {effectiveIncludedUnloadingMinutes != null ? `${effectiveIncludedUnloadingMinutes} minuten` : '—'}
                </p>
              </>
            )}
            <p className="customer-form-muted">{includedTimeSourceLabel}</p>

            {!includedTimeOverrideOpen && (
              <div className="tof-stop-toolbar">
                <Button variant="secondary" onClick={() => setIncludedTimeOverrideOpen(true)} disabled={saving}>
                  Afwijken voor deze order
                </Button>
              </div>
            )}

            {includedTimeOverrideOpen && (
              <div className="tof-stop">
                <div className="tof-row">
                  <FormField label="Inbegrepen laadtijd (minuten)" htmlFor="to-included-loading-override">
                    <input
                      id="to-included-loading-override"
                      type="number"
                      min={0}
                      step={1}
                      value={includedLoadingMinutesOverride}
                      onChange={(e) => setIncludedLoadingMinutesOverride(e.target.value)}
                      disabled={saving}
                    />
                  </FormField>
                  <FormField label="Inbegrepen lostijd (minuten)" htmlFor="to-included-unloading-override">
                    <input
                      id="to-included-unloading-override"
                      type="number"
                      min={0}
                      step={1}
                      value={includedUnloadingMinutesOverride}
                      onChange={(e) => setIncludedUnloadingMinutesOverride(e.target.value)}
                      disabled={saving}
                    />
                  </FormField>
                </div>
                <div className="tof-row">
                  <FormField label="Uurtarief extra tijd (€)" htmlFor="to-extra-rate-override">
                    <input
                      id="to-extra-rate-override"
                      type="number"
                      min={0}
                      step="0.01"
                      value={extraTimeHourlyRateOverride}
                      onChange={(e) => setExtraTimeHourlyRateOverride(e.target.value)}
                      disabled={saving}
                    />
                  </FormField>
                  <FormField label="Afronding (minuten)" htmlFor="to-extra-rounding-override">
                    <input
                      id="to-extra-rounding-override"
                      type="number"
                      min={0}
                      step={1}
                      value={extraTimeRoundingStepMinutes}
                      onChange={(e) => setExtraTimeRoundingStepMinutes(e.target.value)}
                      disabled={saving}
                    />
                  </FormField>
                  <FormField label="Minimum extra tijd (minuten)" htmlFor="to-extra-minimum-override">
                    <input
                      id="to-extra-minimum-override"
                      type="number"
                      min={0}
                      step={1}
                      value={extraTimeMinimumBillableMinutes}
                      onChange={(e) => setExtraTimeMinimumBillableMinutes(e.target.value)}
                      disabled={saving}
                    />
                  </FormField>
                </div>
                {hasIncludedTimeOverride && (
                  <div className="tof-stop-toolbar">
                    <Button variant="secondary" onClick={resetIncludedTimeOverrides} disabled={saving}>
                      Terugzetten naar contractwaarde
                    </Button>
                  </div>
                )}
              </div>
            )}
          </>
        )}
      </div>
    </>
  )

  const documentenContent = documentsSection ?? (
    <p className="placeholder-text">Documenten zijn beschikbaar na het aanmaken van de opdracht.</p>
  )

  const prijsContent = (
    <>
      <div className="tof-row">
        <label className="tof-checkbox">
          <input
            type="radio"
            name="to-pricing-source"
            checked={pricingSource === 'Contract'}
            onChange={() => setPricingSource('Contract')}
            disabled={saving}
          />
          Klantcontract
        </label>
        <label className="tof-checkbox">
          <input
            type="radio"
            name="to-pricing-source"
            checked={pricingSource === 'OneOff'}
            onChange={() => setPricingSource('OneOff')}
            disabled={saving}
          />
          Eenmalige prijsafspraak
        </label>
      </div>

      {pricingSource === 'OneOff' && (
        <fieldset className="tof-stop">
          <legend>Eenmalige prijsafspraak</legend>
          <div className="tof-row">
            <FormField label="Vast bedrag (€)" htmlFor="to-oneoff-amount" required>
              <input
                id="to-oneoff-amount"
                type="number"
                min={0}
                step="0.01"
                value={oneOffFixedAmount}
                onChange={(e) => setOneOffFixedAmount(e.target.value)}
                disabled={saving}
              />
            </FormField>
          </div>
          <p className="customer-form-muted">Inbegrepen tijd</p>
          <div className="tof-row">
            <label className="tof-checkbox">
              <input
                type="radio"
                name="to-oneoff-time-mode"
                checked={oneOffTimeMode === 'none'}
                onChange={() => setOneOffTimeMode('none')}
                disabled={saving}
              />
              Geen
            </label>
            <label className="tof-checkbox">
              <input
                type="radio"
                name="to-oneoff-time-mode"
                checked={oneOffTimeMode === 'separate'}
                onChange={() => setOneOffTimeMode('separate')}
                disabled={saving}
              />
              Per activiteit
            </label>
            <label className="tof-checkbox">
              <input
                type="radio"
                name="to-oneoff-time-mode"
                checked={oneOffTimeMode === 'combined'}
                onChange={() => setOneOffTimeMode('combined')}
                disabled={saving}
              />
              Gecombineerd
            </label>
          </div>
          {oneOffTimeMode === 'separate' && (
            <div className="tof-row">
              <FormField label="Laden (min)" htmlFor="to-oneoff-loading">
                <input
                  id="to-oneoff-loading"
                  type="number"
                  min={0}
                  value={oneOffIncludedLoadingMinutes}
                  onChange={(e) => setOneOffIncludedLoadingMinutes(e.target.value)}
                  disabled={saving}
                />
              </FormField>
              <FormField label="Lossen (min)" htmlFor="to-oneoff-unloading">
                <input
                  id="to-oneoff-unloading"
                  type="number"
                  min={0}
                  value={oneOffIncludedUnloadingMinutes}
                  onChange={(e) => setOneOffIncludedUnloadingMinutes(e.target.value)}
                  disabled={saving}
                />
              </FormField>
            </div>
          )}
          {oneOffTimeMode === 'combined' && (
            <div className="tof-row">
              <FormField label="Totaal (min)" htmlFor="to-oneoff-combined">
                <input
                  id="to-oneoff-combined"
                  type="number"
                  min={0}
                  value={oneOffIncludedCombinedMinutes}
                  onChange={(e) => setOneOffIncludedCombinedMinutes(e.target.value)}
                  disabled={saving}
                />
              </FormField>
            </div>
          )}
          {oneOffTimeMode !== 'none' && (
            <div className="tof-row">
              <FormField label="Uurtarief extra tijd (€/u)" htmlFor="to-oneoff-rate">
                <input
                  id="to-oneoff-rate"
                  type="number"
                  min={0}
                  step="0.01"
                  value={oneOffExtraHourlyRate}
                  onChange={(e) => setOneOffExtraHourlyRate(e.target.value)}
                  disabled={saving}
                />
              </FormField>
            </div>
          )}
          <FormField label="Notities" htmlFor="to-oneoff-notes" hint="Bv. 'Afgesproken via telefoon met dhr. Peeters'.">
            <textarea
              id="to-oneoff-notes"
              rows={2}
              value={oneOffNotes}
              onChange={(e) => setOneOffNotes(e.target.value)}
              disabled={saving}
              maxLength={500}
            />
          </FormField>
        </fieldset>
      )}

      {preview ? (
        <div className="tof-price-breakdown">
          <p className="customer-form-muted">
            Tariefdatum: {preview.tariffDate ?? (orderDate || '—')}
            {preview.zoneName ? ` · Zone: ${preview.zoneName} (${preview.zoneCode})` : ''}
            {(() => {
              const agreementNames = [...new Set(preview.lines.map((l) => l.agreementName).filter(Boolean))]
              return agreementNames.length > 0 ? ` · Tarief: ${agreementNames.join(', ')}` : ''
            })()}
          </p>
          {preview.configurationError && (
            <div className="issued-items-form-error" role="alert">
              {preview.configurationError}
            </div>
          )}
          <table className="issued-items-table">
            <thead>
              <tr>
                <th>Omschrijving</th>
                <th>Bron</th>
                <th className="tof-price-amount">Bedrag</th>
              </tr>
            </thead>
            <tbody>
              {preview.lines.filter((line) => !line.proposed).map((line, index) => (
                <tr key={index} className={line.informational ? 'tof-price-informational' : undefined}>
                  <td>
                    {line.label}
                    {(line.billableQuantity ?? null) !== null && (line.actualQuantity ?? null) !== null
                      && line.billableQuantity !== line.actualQuantity && (
                      <span className="customer-form-muted">
                        {' '}
                        ({line.actualQuantity} werkelijk / {line.billableQuantity} factureerbaar)
                      </span>
                    )}
                  </td>
                  <td>{line.source}</td>
                  <td className="tof-price-amount">€ {line.amount.toFixed(2)}</td>
                </tr>
              ))}
              {preview.lines.some((line) => line.proposed) && (
                <>
                  <tr>
                    <th colSpan={2}>Subtotaal</th>
                    <th className="tof-price-amount">€ {preview.total.toFixed(2)}</th>
                  </tr>
                  {preview.lines.filter((line) => line.proposed).map((line, index) => (
                    <tr key={`proposed-${index}`} className="tof-price-proposed">
                      <td>
                        {line.label} <Badge tone="warning">VOORSTEL</Badge>
                      </td>
                      <td>{line.source}</td>
                      <td className="tof-price-amount">€ {line.amount.toFixed(2)}</td>
                    </tr>
                  ))}
                </>
              )}
            </tbody>
            <tfoot>
              <tr>
                <th>
                  Totaal
                  {preview.zoneCode ? ` (zone ${preview.zoneCode})` : ''}
                </th>
                <th />
                <th className="tof-price-amount">€ {preview.total.toFixed(2)}</th>
              </tr>
              {preview.lines.some((line) => line.proposed) && (
                <tr>
                  <th>Totaal incl. voorstellen</th>
                  <th />
                  <th className="tof-price-amount">€ {preview.totalWithProposed.toFixed(2)}</th>
                </tr>
              )}
            </tfoot>
          </table>
          {preview.requiresManualPrice && !preview.configurationError && (
            <div className="tof-customer-requirements" role="note">
              <p>Geen geldig tarief gevonden voor deze order — vul een handmatige prijs in of configureer tarieven.</p>
              {preview.diagnostics && preview.diagnostics.length > 0 && (
                <ul>
                  {preview.diagnostics.map((line, index) => (
                    <li key={index}>{line}</li>
                  ))}
                </ul>
              )}
            </div>
          )}
        </div>
      ) : (
        <p className="placeholder-text">
          Vul klant, aantal en eenheid in om de prijs automatisch te berekenen. Zonder geconfigureerde tarieven blijft
          een handmatige prijs mogelijk.
        </p>
      )}

      {canOverridePrice && (
        <label className="tof-checkbox">
          <input type="checkbox" checked={priceIsManual} onChange={(e) => setPriceIsManual(e.target.checked)} disabled={saving} />
          Handmatige prijs (overschrijft de berekende prijs)
        </label>
      )}
      {(priceIsManual || (!preview && !canOverridePrice) || (!preview && canOverridePrice)) && (
        <div className="tof-row">
          <FormField
            label={priceIsManual ? 'Handmatige prijs (€)' : 'Afgesproken prijs (€)'}
            htmlFor="to-price"
            hint={priceIsManual ? undefined : 'Gebruikt zolang er geen berekende prijs is.'}
          >
            <input id="to-price" type="number" min={0} step="0.01" value={agreedPrice} onChange={(e) => setAgreedPrice(e.target.value)} disabled={saving} />
          </FormField>
          {priceIsManual && (
            <FormField label="Reden" htmlFor="to-price-reason" required hint="Verplicht bij het overschrijven van de berekende prijs.">
              <input id="to-price-reason" value={priceOverrideReason} onChange={(e) => setPriceOverrideReason(e.target.value)} disabled={saving} maxLength={300} />
            </FormField>
          )}
        </div>
      )}
    </>
  )

  const stopSummary = (kind: StopInput['stopType']) => {
    const matching = stops.filter((s) => s.stopType === kind)
    return matching.map((s) => s.city || s.locationName || '—').join(' → ') || '—'
  }

  const samenvattingContent = (
    <>
      <dl className="tof-summary">
        <div>
          <dt>Klant</dt>
          <dd>{customers.find((c) => c.id === customerId)?.name ?? order?.customerName ?? '—'}</dd>
        </div>
        <div>
          <dt>Laden</dt>
          <dd>{stopSummary('Loading')}</dd>
        </div>
        <div>
          <dt>Lossen</dt>
          <dd>{stopSummary('Unloading')}</dd>
        </div>
        <div>
          <dt>Goederen</dt>
          <dd>
            {quantity || '—'} {unitOptions.find((u) => u.code === quantityUnitCode)?.name ?? quantityUnit ?? ''}
            {weightKg ? ` · ${weightKg} kg` : ''}
            {cargoItems.length > 0 ? ` · ${cargoItems.length} goederenlijn(en)` : ''}
          </dd>
        </div>
        <div>
          <dt>Diensten</dt>
          <dd>
            {selectedServiceOptionIds.length > 0
              ? serviceOptions.filter((o) => selectedServiceOptionIds.includes(o.id)).map((o) => o.name).join(', ')
              : '—'}
          </dd>
        </div>
        <div>
          <dt>Prijs</dt>
          <dd>
            {priceIsManual
              ? `€ ${agreedPrice || '0'} (handmatig)`
              : preview
                ? `€ ${preview.total.toFixed(2)} (berekend)`
                : agreedPrice
                  ? `€ ${agreedPrice}`
                  : '—'}
          </dd>
        </div>
      </dl>
      <FormField label="Notities" htmlFor="to-notes">
        <textarea id="to-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} disabled={saving} maxLength={4000} />
      </FormField>
    </>
  )

  const sections: SectionDef[] = [
    { id: 'algemeen', label: 'Algemeen', render: () => algemeenContent },
    { id: 'route', label: 'Route & stops', render: () => routeContent },
    { id: 'goederen', label: 'Goederen', render: () => goederenContent },
    { id: 'services', label: 'Services & toeslagen', optional: true, render: () => servicesContent },
    { id: 'documenten', label: 'Documenten', optional: true, panel: documentsSectionIsPanel, render: () => documentenContent },
    { id: 'prijs', label: 'Prijs', render: () => prijsContent },
    { id: 'samenvatting', label: 'Samenvatting', render: () => samenvattingContent },
  ]

  return (
    <form className="tof" onSubmit={handleSubmit} noValidate>
      {formError && (
        <div className="tof-error" role="alert">
          {formError}
        </div>
      )}

      <SectionedForm
        sections={sections}
        activeId={activeId}
        onActiveChange={setActive}
        actions={
          <div className="tof-actions">
            {onCancel && (
              <Button variant="secondary" onClick={onCancel} disabled={saving}>
                Annuleren
              </Button>
            )}
            <Button type="submit" disabled={saving}>
              {saving ? 'Opslaan…' : submitLabel}
            </Button>
          </div>
        }
      />

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
    </form>
  )
}
