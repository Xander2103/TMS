import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { ApiError } from '../../../api/apiClient'
import { Button } from '../../../components/ui/Button'
import { FormField } from '../../../components/ui/FormField'
import { SectionedForm, type SectionDef } from '../../../components/ui/SectionedForm'
import { useSectionNavigation } from '../../../components/ui/useSectionNavigation'
import { getCustomer, searchCustomers } from '../../customers/api/customersApi'
import type { CustomerDetail, CustomerListItem } from '../../customers/types'
import { getLegalEntityOptions } from '../../legal-entities/api/legalEntitiesApi'
import type { LegalEntityOption } from '../../legal-entities/types'
import { LookupSelect } from '../../master-data/components/LookupSelect'
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
  previewPrice,
  type CustomerPricingConfig,
  type PriceCalculationResult,
  type ServiceOption,
} from '../../tarification/api/pricingApi'
import { STOP_TYPE_LABELS, type StopInput, type TransportOrderDetail, type TransportOrderInput } from '../types'
import './transport-order-form.css'

interface CargoFormRow {
  key: string
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
        description: c.description,
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
  const [pricingConfig, setPricingConfig] = useState<{ customerId: string; config: CustomerPricingConfig } | null>(null)
  const [showAllUnits, setShowAllUnits] = useState(false)
  const [preview, setPreview] = useState<PriceCalculationResult | null>(null)
  const [priceIsManual, setPriceIsManual] = useState(order?.priceIsManual ?? false)
  const [priceOverrideReason, setPriceOverrideReason] = useState(order?.priceOverrideReason ?? '')

  useEffect(() => {
    let mounted = true
    listServiceOptions()
      .then((data) => {
        if (mounted) setServiceOptions(data)
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
  const preferredCodes = new Set(preferredUnits.map((u) => u.code))
  const otherUnits = unitOptions.filter((u) => !preferredCodes.has(u.code))

  // Live price preview, debounced on the pricing-relevant inputs.
  const lastUnloading = [...stops].reverse().find((s) => s.stopType === 'Unloading')
  const previewKey = JSON.stringify([
    customerId, orderDate, quantity, quantityUnitCode, weightKg, palletCount,
    lastUnloading?.postalCode, lastUnloading?.countryCode, selectedServiceOptionIds,
  ])
  useEffect(() => {
    const unitTypeId = unitOptions.find((u) => u.code === quantityUnitCode)?.id ?? null
    const lines = unitTypeId && quantity !== '' && Number(quantity) > 0
      ? [{ unitTypeId, quantity: Number(quantity) }]
      : []
    const shouldPreview = Boolean(customerId) && (lines.length > 0 || selectedServiceOptionIds.length > 0)
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
      if (!cargo.description.trim()) {
        setFormError('Elke goederenlijn heeft een omschrijving nodig.')
        return
      }
      if (cargo.expectedQuantity === '' || Number(cargo.expectedQuantity) <= 0) {
        setFormError('De verwachte hoeveelheid van een goederenlijn moet groter dan nul zijn.')
        return
      }
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
      priceIsManual,
      priceOverrideReason: priceIsManual ? priceOverrideReason.trim() || null : null,
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
        description: cargo.description.trim(),
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
      <FormField label="Omschrijving goederen" htmlFor="to-goods" hint="Optioneel — gestructureerde gegevens horen bij de goederenlijnen.">
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
          {/* Customer-preferred units first; "Andere eenheden tonen" reveals the full active list. */}
          <select
            id="to-unit"
            value={quantityUnitCode ?? ''}
            onChange={(e) => setQuantityUnitCode(e.target.value || null)}
            disabled={saving}
          >
            <option value="">— Eenheid —</option>
            {preferredUnits.length > 0 && (
              <optgroup label="Gebruikelijk voor deze klant">
                {preferredUnits.map((unit) => (
                  <option key={unit.unitTypeId} value={unit.code}>
                    {unit.name}
                  </option>
                ))}
              </optgroup>
            )}
            {(preferredUnits.length === 0 || showAllUnits || (quantityUnitCode !== null && !preferredCodes.has(quantityUnitCode))) &&
              (preferredUnits.length > 0 ? (
                <optgroup label="Andere eenheden">
                  {otherUnits.map((unit) => (
                    <option key={unit.id} value={unit.code}>
                      {unit.name}
                    </option>
                  ))}
                </optgroup>
              ) : (
                unitOptions.map((unit) => (
                  <option key={unit.id} value={unit.code}>
                    {unit.name}
                  </option>
                ))
              ))}
          </select>
          {preferredUnits.length > 0 && !showAllUnits && (
            <button type="button" className="tof-link" onClick={() => setShowAllUnits(true)}>
              Andere eenheden tonen
            </button>
          )}
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
            <FormField label="Omschrijving" htmlFor={`cg-desc-${cargo.key}`} required>
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
              <LookupSelect
                id={`cg-unit-${cargo.key}`}
                basePath="/api/unit-types"
                managePermission="unit_types.manage"
                singular="eenheid"
                valueKey="code"
                value={cargo.quantityUnitCode}
                onChange={(code) => setCargo(cargo.key, { quantityUnitCode: code })}
                disabled={saving}
                placeholder="— Eenheid —"
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
              <FormField label="Lengte (m)" htmlFor={`cg-length-${cargo.key}`}>
                <input id={`cg-length-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.lengthMeters} onChange={(e) => setCargo(cargo.key, { lengthMeters: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Breedte (m)" htmlFor={`cg-width-${cargo.key}`}>
                <input id={`cg-width-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.widthMeters} onChange={(e) => setCargo(cargo.key, { widthMeters: e.target.value })} disabled={saving} />
              </FormField>
              <FormField label="Hoogte (m)" htmlFor={`cg-height-${cargo.key}`}>
                <input id={`cg-height-${cargo.key}`} type="number" min={0} step="0.01" value={cargo.heightMeters} onChange={(e) => setCargo(cargo.key, { heightMeters: e.target.value })} disabled={saving} />
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
    const customerPrice = customerConfig?.serviceOptions.find((o) => o.serviceOptionId === option.id)?.customerValue
    return customerPrice ?? option.defaultValue
  }

  const servicesContent = (
    <>
      <p className="ui-form-section-description">
        Gekozen diensten tellen mee in de prijsberekening en worden bij facturatie aparte factuurlijnen.
      </p>
      {serviceOptions.length === 0 && <p className="placeholder-text">Nog geen diensten geconfigureerd.</p>}
      <div className="tof-service-options">
        {serviceOptions.map((option) => {
          const checked = selectedServiceOptionIds.includes(option.id)
          const isCustomerPrice = customerConfig?.serviceOptions.some(
            (o) => o.serviceOptionId === option.id && o.customerValue !== null,
          )
          return (
            <label key={option.id} className="tof-checkbox tof-service-option">
              <input
                type="checkbox"
                checked={checked}
                onChange={(e) =>
                  setSelectedServiceOptionIds((ids) =>
                    e.target.checked ? [...ids, option.id] : ids.filter((id) => id !== option.id),
                  )
                }
                disabled={saving}
              />
              <span>
                {option.name}{' '}
                <span className="customer-form-muted">
                  ({option.kind === 'Percent' ? `${effectiveOptionPrice(option)}%` : `€ ${effectiveOptionPrice(option).toFixed(2)}`}
                  {isCustomerPrice ? ' — klantprijs' : ''})
                </span>
              </span>
            </label>
          )
        })}
      </div>
    </>
  )

  const documentenContent = documentsSection ?? (
    <p className="placeholder-text">Documenten zijn beschikbaar na het aanmaken van de opdracht.</p>
  )

  const prijsContent = (
    <>
      {preview ? (
        <div className="tof-price-breakdown">
          <table className="issued-items-table">
            <thead>
              <tr>
                <th>Omschrijving</th>
                <th>Bron</th>
                <th className="tof-price-amount">Bedrag</th>
              </tr>
            </thead>
            <tbody>
              {preview.lines.map((line, index) => (
                <tr key={index} className={line.informational ? 'tof-price-informational' : undefined}>
                  <td>{line.label}</td>
                  <td>{line.source}</td>
                  <td className="tof-price-amount">€ {line.amount.toFixed(2)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr>
                <th>
                  Berekend totaal
                  {preview.zoneCode ? ` (zone ${preview.zoneCode})` : ''}
                </th>
                <th />
                <th className="tof-price-amount">€ {preview.total.toFixed(2)}</th>
              </tr>
            </tfoot>
          </table>
          {preview.requiresManualPrice && (
            <p className="tof-customer-requirements" role="note">
              Niet alles kon automatisch geprijsd worden — vul een handmatige prijs in of configureer tarieven.
            </p>
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
