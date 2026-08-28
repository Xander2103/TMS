import { useState, type FormEvent, type ReactNode } from 'react'
import { ApiError } from '../../../api/apiClient'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { SectionedForm, type SectionDef } from '../../../components/ui/SectionedForm'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useSectionNavigation } from '../../../components/ui/useSectionNavigation'
import { useLocale } from '../../../i18n/localeContext'
import { LocationQuickCreateDialog } from '../../locations/components/LocationQuickCreateDialog'
import type { LocationOption } from '../../locations/types'
import { useAuth } from '../../auth/authContextValue'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import type { TransportOrderDetail, TransportOrderInput } from '../types'
import { GeneralSection } from './sections/GeneralSection'
import { RouteSection } from './sections/RouteSection'
import { GoodsSection } from './sections/GoodsSection'
import { ServicesSection } from './sections/ServicesSection'
import { PriceSection } from './sections/PriceSection'
import { SummarySection } from './sections/SummarySection'
import { useOrderFormData, useOrderPricePreview } from './sections/useOrderFormData'
import { buildSubmitPayload } from './sections/orderFormPayload'
import {
  cargoFromOrder,
  cargoRowFromHeader,
  computeCargoSummary,
  emptyCargoRow,
  emptyStop,
  fieldErrorMap,
  serviceDaysFromOrder,
  serviceIdsFromOrder,
  serviceNotesFromOrder,
  servicePalletsFromOrder,
  serviceQuantitiesFromOrder,
  stopsFromOrder,
  validateOrderForm,
  type CargoFormRow,
  type OrderFormValidationError,
  type OrderFormValues,
  type StopFormRow,
} from './sections/orderFormState'
import './transport-order-form.css'

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
 * here — the section bodies live in `./sections/` — so switching tabs preserves values and
 * never touches the router.
 */
export function TransportOrderForm({ order, onSubmit, onCancel, submitLabel, documentsSection, documentsSectionIsPanel }: TransportOrderFormProps) {
  const { t } = useLocale()
  const { hasPermission } = useAuth()
  const canCreateLocations = hasPermission('locations.create')
  const canOverridePrice = hasPermission('orders.override_price')
  const { activeId, setActive } = useSectionNavigation(SECTION_IDS, 'algemeen')
  // Inline location creation from a stop's location picker: the promise resolves when the
  // dialog closes, so the SearchableSelect can auto-select the new location.
  const [quickCreate, setQuickCreate] = useState<{
    name: string
    resolve: (created: LocationOption | null) => void
  } | null>(null)
  // Pending "Adres opnieuw overnemen" confirmation for one stop row (keyed by row key).
  const [refreshTarget, setRefreshTarget] = useState<string | null>(null)

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
  const [distanceKm, setDistanceKm] = useState(
    order?.distanceKm === null || order?.distanceKm === undefined ? '' : String(order.distanceKm),
  )
  const [loadingMeters, setLoadingMeters] = useState(
    order?.loadingMeters === null || order?.loadingMeters === undefined ? '' : String(order.loadingMeters),
  )
  const [adrRequired, setAdrRequired] = useState(order?.adrRequired ?? false)
  const [craneRequired, setCraneRequired] = useState(order?.craneRequired ?? false)
  const [plateauRequired, setPlateauRequired] = useState(order?.plateauRequired ?? false)
  const [moffettRequired, setMoffettRequired] = useState(order?.moffettRequired ?? false)
  const [isReturnMovement, setIsReturnMovement] = useState(order?.isReturnMovement ?? false)
  const [agreedPrice, setAgreedPrice] = useState(
    order?.agreedPrice === null || order === undefined ? '' : String(order.agreedPrice),
  )
  const [notes, setNotes] = useState(order?.notes ?? '')

  const [legalEntityId, setLegalEntityId] = useState(order?.legalEntityId ?? '')
  const [dieselSurchargeOverride, setDieselSurchargeOverride] = useState(order?.dieselSurchargeOverride ?? false)
  const [dieselSurchargePercentOverride, setDieselSurchargePercentOverride] = useState(
    order?.dieselSurchargePercentOverride === null || order?.dieselSurchargePercentOverride === undefined
      ? ''
      : String(order.dieselSurchargePercentOverride),
  )
  const [dieselSurchargeOverrideReason, setDieselSurchargeOverrideReason] = useState(order?.dieselSurchargeOverrideReason ?? '')

  const [stops, setStops] = useState<StopFormRow[]>(() => stopsFromOrder(order))
  const [cargoItems, setCargoItems] = useState<CargoFormRow[]>(() => cargoFromOrder(order))

  const [formError, setFormError] = useState<string | null>(null)
  const [clientErrors, setClientErrors] = useState<OrderFormValidationError[]>([])
  const [saving, setSaving] = useState(false)

  // --- Pricing state (spec 7): configured units, service options, live preview, override ---
  const { options: unitOptions } = useLookupOptions('/api/unit-types')
  const [selectedServiceOptionIds, setSelectedServiceOptionIds] = useState<string[]>(() => serviceIdsFromOrder(order))
  // Entered quantities for per-hour/per-stop services — and the billable pallet-days for
  // per-pallet-day services (auto-filled from pallets × days, manually correctable).
  const [serviceQuantities, setServiceQuantities] = useState<Record<string, string>>(() => serviceQuantitiesFromOrder(order))
  // Per-pallet-day inputs: pallet count and day count, keyed by service option id.
  const [servicePallets, setServicePallets] = useState<Record<string, string>>(() => servicePalletsFromOrder(order))
  const [serviceDays, setServiceDays] = useState<Record<string, string>>(() => serviceDaysFromOrder(order))
  // Optional free-text note per manually selected service (e.g. "Afgesproken met klant").
  const [serviceNotes, setServiceNotes] = useState<Record<string, string>>(() => serviceNotesFromOrder(order))
  // "+ Dienst of toeslag toevoegen" panel: the option being configured before it joins the
  // manually-selected set. Its quantity/pallet/day/note inputs write directly into the same state
  // maps used once selected, so nothing needs copying on "Toevoegen" — only the id moves.
  const [addServicePanelOpen, setAddServicePanelOpen] = useState(false)
  const [draftServiceOptionId, setDraftServiceOptionId] = useState('')
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

  // Reference data (customers, entities, services, unit master, warehouses, customer config +
  // requirements, structured location hours) — fetched by the extracted hook.
  const { customers, legalEntities, serviceOptions, unitMaster, warehouses, locationHours, customerConfig, customerRequirements } =
    useOrderFormData(customerId, stops)
  const preferredUnits = customerConfig?.preferredUnits ?? []
  const customerServiceById = new Map(
    (customerConfig?.serviceOptions ?? []).map((o) => [o.serviceOptionId, o]),
  )
  const requirementHints = customerRequirements
    ? [
        customerRequirements.customerReferenceRequired ? t('transportOrders.form.hintReferenceRequired') : null,
        customerRequirements.purchaseOrderRequired ? t('transportOrders.form.hintPoRequired') : null,
        customerRequirements.signedDeliveryNoteRequired ? t('transportOrders.form.hintCmrRequired') : null,
      ].filter((hint): hint is string => hint !== null)
    : []

  // Whole-form value snapshot: validation, submit payload and preview inputs read this.
  const values: OrderFormValues = {
    customerId, customerReference, orderDate, goodsDescription,
    quantity, quantityUnit, quantityUnitCode, weightKg, volumeM3, palletCount,
    distanceKm, loadingMeters,
    adrRequired, craneRequired, plateauRequired, moffettRequired, isReturnMovement,
    agreedPrice, notes, legalEntityId,
    dieselSurchargeOverride, dieselSurchargePercentOverride, dieselSurchargeOverrideReason,
    stops, cargoItems,
    serviceOptions, selectedServiceOptionIds, serviceQuantities, servicePallets, serviceDays, serviceNotes,
    priceIsManual, priceOverrideReason,
    pricingSource, oneOffFixedAmount, oneOffTimeMode, oneOffIncludedLoadingMinutes,
    oneOffIncludedUnloadingMinutes, oneOffIncludedCombinedMinutes, oneOffExtraHourlyRate, oneOffNotes,
    includedLoadingMinutesOverride, includedUnloadingMinutesOverride, extraTimeHourlyRateOverride,
    extraTimeRoundingStepMinutes, extraTimeMinimumBillableMinutes,
    // Wave 1 §10: echo the loaded order's concurrency token on update.
    version: order?.version,
  }
  const errors = fieldErrorMap(clientErrors)

  // Wave 2026-08-04 §2: commercial goods lines are the source of truth as soon as any row
  // exists — the order summary derives from them and the header inputs disappear.
  const derivedFromCargo = cargoItems.length > 0
  const cargoSummary = derivedFromCargo ? computeCargoSummary(cargoItems, unitOptions) : null
  const effectiveWeightKg = cargoSummary?.weight ?? (weightKg === '' ? null : Number(weightKg))
  const effectivePalletCount = cargoSummary?.pallets ?? (palletCount === '' ? null : Number(palletCount))

  // Warehouses the order touches (stop at a warehouse's master location) — mirrors the
  // save-time derivation so warehouse-conditioned services preview identically.
  const touchedWarehouseIds = warehouses
    .filter((w) => stops.some((s) => s.locationId !== null && s.locationId === w.locationId))
    .map((w) => w.id)
  // Live price preview, debounced on the pricing-relevant inputs (extracted hook).
  const preview = useOrderPricePreview(values, unitOptions, touchedWarehouseIds, effectiveWeightKg, effectivePalletCount)

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
    // Wave 1 §12: one validate() pass listing EVERY failing field, rendered by the
    // ValidationSummary; submit jumps to the first failing section automatically.
    const validationErrors = validateOrderForm(values)
    setClientErrors(validationErrors)
    if (validationErrors.length > 0) {
      setActive(validationErrors[0].section)
      return
    }

    const input = buildSubmitPayload(values)
    setSaving(true)
    try {
      await onSubmit(input)
    } catch (err) {
      setFormError(
        err instanceof ApiError && err.status === 409
          ? t('transportOrders.form.conflict')
          : err instanceof ApiError && err.status === 400
            ? t('transportOrders.form.badRequest')
            : t('transportOrders.form.saveFailed'),
      )
    } finally {
      setSaving(false)
    }
  }

  const documentenContent = documentsSection ?? (
    <p className="placeholder-text">{t('transportOrders.form.docsAfterCreate')}</p>
  )

  const sections: SectionDef[] = [
    {
      id: 'algemeen',
      label: t('transportOrders.form.sections.algemeen'),
      render: () => (
        <GeneralSection
          {...{
            order, customers, legalEntities, customerRequirements, requirementHints,
            customerId, setCustomerId, customerReference, setCustomerReference, orderDate, setOrderDate,
            legalEntityId, setLegalEntityId, dieselSurchargeOverride, setDieselSurchargeOverride,
            dieselSurchargePercentOverride, setDieselSurchargePercentOverride,
            dieselSurchargeOverrideReason, setDieselSurchargeOverrideReason, saving, errors,
          }}
        />
      ),
    },
    {
      id: 'route',
      label: t('transportOrders.form.sections.route'),
      render: () => (
        <RouteSection
          {...{ stops, customerId, saving, locationHours, errors, setStop, moveStop }}
          onAddStop={(stopType) => setStops((rows) => [...rows, emptyStop(stopType)])}
          onRemoveStop={(key) => setStops((rows) => rows.filter((row) => row.key !== key))}
          onRequestRefresh={setRefreshTarget}
          onQuickCreate={
            customerId && canCreateLocations
              ? (name) => new Promise<LocationOption | null>((resolve) => setQuickCreate({ name, resolve }))
              : undefined
          }
        />
      ),
    },
    {
      id: 'goederen',
      label: t('transportOrders.form.sections.goederen'),
      render: () => (
        <GoodsSection
          {...{
            goodsDescription, setGoodsDescription, quantity, setQuantity, quantityUnit,
            quantityUnitCode, setQuantityUnitCode, weightKg, setWeightKg, volumeM3, setVolumeM3,
            palletCount, setPalletCount, distanceKm, setDistanceKm, loadingMeters, setLoadingMeters,
            adrRequired, setAdrRequired, craneRequired, setCraneRequired,
            plateauRequired, setPlateauRequired, moffettRequired, setMoffettRequired,
            isReturnMovement, setIsReturnMovement,
            derivedFromCargo, cargoSummary, cargoItems, stops, unitOptions, preferredUnits,
            setCargo, applyCargoUnit, cargoDimensionsFixed, saving, errors,
          }}
          onAddCargoRow={() => setCargoItems((rows) => [...rows, emptyCargoRow()])}
          onAddCargoRowFromHeader={() =>
            setCargoItems((rows) => [
              ...rows,
              cargoRowFromHeader({ quantity, quantityUnit, quantityUnitCode, weightKg, volumeM3, palletCount }),
            ])
          }
          onRemoveCargoRow={(key) => setCargoItems((rows) => rows.filter((row) => row.key !== key))}
        />
      ),
    },
    {
      id: 'services',
      label: t('transportOrders.form.sections.services'),
      optional: true,
      render: () => (
        <ServicesSection
          {...{
            saving, serviceOptions, customerServiceById, preview, stops, palletCount, pricingSource,
            selectedServiceOptionIds, setSelectedServiceOptionIds,
            serviceQuantities, setServiceQuantities, servicePallets, setServicePallets,
            serviceDays, setServiceDays, serviceNotes, setServiceNotes,
            addServicePanelOpen, setAddServicePanelOpen, draftServiceOptionId, setDraftServiceOptionId,
            includedLoadingMinutesOverride, setIncludedLoadingMinutesOverride,
            includedUnloadingMinutesOverride, setIncludedUnloadingMinutesOverride,
            extraTimeHourlyRateOverride, setExtraTimeHourlyRateOverride,
            extraTimeRoundingStepMinutes, setExtraTimeRoundingStepMinutes,
            extraTimeMinimumBillableMinutes, setExtraTimeMinimumBillableMinutes,
            includedTimeOverrideOpen, setIncludedTimeOverrideOpen,
          }}
        />
      ),
    },
    {
      id: 'documenten',
      label: t('transportOrders.form.sections.documenten'),
      optional: true,
      panel: documentsSectionIsPanel,
      render: () => documentenContent,
    },
    {
      id: 'prijs',
      label: t('transportOrders.form.sections.prijs'),
      render: () => (
        <PriceSection
          {...{
            saving, orderDate, preview, canOverridePrice, pricingSource, setPricingSource,
            oneOffFixedAmount, setOneOffFixedAmount, oneOffTimeMode, setOneOffTimeMode,
            oneOffIncludedLoadingMinutes, setOneOffIncludedLoadingMinutes,
            oneOffIncludedUnloadingMinutes, setOneOffIncludedUnloadingMinutes,
            oneOffIncludedCombinedMinutes, setOneOffIncludedCombinedMinutes,
            oneOffExtraHourlyRate, setOneOffExtraHourlyRate, oneOffNotes, setOneOffNotes,
            priceIsManual, setPriceIsManual, priceOverrideReason, setPriceOverrideReason,
            agreedPrice, setAgreedPrice, errors,
          }}
        />
      ),
    },
    {
      id: 'samenvatting',
      label: t('transportOrders.form.sections.samenvatting'),
      render: () => (
        <SummarySection
          {...{
            order, customers, customerId, stops, cargoItems, quantity, quantityUnit, quantityUnitCode,
            weightKg, unitOptions, serviceOptions, selectedServiceOptionIds, preview,
            priceIsManual, agreedPrice, notes, setNotes, saving,
          }}
        />
      ),
    },
  ]

  const summaryFieldErrors = Object.fromEntries(
    clientErrors.map((error) => [error.field, clientErrors.filter((e) => e.field === error.field).map((e) => e.message)]),
  )
  const summaryFieldLabels = Object.fromEntries(clientErrors.map((error) => [error.field, error.label]))

  return (
    <form className="tof" onSubmit={handleSubmit} noValidate>
      <ValidationSummary
        message={formError}
        fieldErrors={summaryFieldErrors}
        fieldLabels={summaryFieldLabels}
        onSelect={(path) => {
          const target = clientErrors.find((error) => error.field === path)
          if (target) setActive(target.section)
        }}
      />

      <SectionedForm
        sections={sections}
        activeId={activeId}
        onActiveChange={setActive}
        actions={
          <div className="tof-actions">
            {onCancel && (
              <Button variant="secondary" onClick={onCancel} disabled={saving}>
                {t('ui.actions.cancel')}
              </Button>
            )}
            <Button type="submit" disabled={saving}>
              {saving ? t('transportOrders.form.saving') : submitLabel}
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

      {refreshTarget && (
        <ConfirmDialog
          title={t('transportOrders.form.refreshTitle')}
          message={t('transportOrders.form.refreshMessage')}
          confirmLabel={t('transportOrders.form.refreshConfirm')}
          onConfirm={() => {
            setStop(refreshTarget, { refreshSnapshot: true })
            setRefreshTarget(null)
          }}
          onCancel={() => setRefreshTarget(null)}
        />
      )}
    </form>
  )
}
