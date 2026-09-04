import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '../../../components/layout/PageHeader'
import { BackButton } from '../../../components/ui/BackButton'
import { Button } from '../../../components/ui/Button'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { FormField } from '../../../components/ui/FormField'
import { SearchableSelect } from '../../../components/ui/SearchableSelect'
import { ValidationSummary } from '../../../components/ui/ValidationSummary'
import { useToast } from '../../../components/ui/toastContext'
import { describeApiError, getFieldError, type FieldErrors } from '../../../api/problemDetails'
import { useLocale } from '../../../i18n/localeContext'
import { useAuth } from '../../auth/authContextValue'
import { listActivityTypes, type ActivityType } from '../api/activityTypesApi'
import { activityTypeIcon, DEFAULT_ACTIVITY_TYPE_ICON } from '../activityTypeIcons'
import { createDossierFast } from '../api/dossiersApi'
import { createTransportOrder } from '../../transport-orders/api/transportOrdersApi'
import { RouteSection } from '../../transport-orders/components/sections/RouteSection'
import { GoodsSection } from '../../transport-orders/components/sections/GoodsSection'
import { useOrderFormData } from '../../transport-orders/components/sections/useOrderFormData'
import { useStopMutation } from '../../transport-orders/components/sections/useStopMutation'
import { buildSubmitPayload } from '../../transport-orders/components/sections/orderFormPayload'
import { useLookupOptions } from '../../master-data/hooks/useLookupOptions'
import { LocationQuickCreateDialog } from '../../locations/components/LocationQuickCreateDialog'
import type { LocationOption } from '../../locations/types'
import {
  applyUnitToCargoRow,
  cargoRowFromHeader,
  computeCargoSummary,
  emptyCargoRow,
  emptyStop,
  fieldErrorMap,
  isEmptyCargoRow,
  isEmptyStopRow,
  remapCargoStopIndices,
  validateOrderForm,
  type CargoFormRow,
  type OrderFormValidationError,
  type OrderFormValues,
  type StopFormRow,
} from '../../transport-orders/components/sections/orderFormState'
import './new-dossier.css'

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
  const [dossierDate, setDossierDate] = useState(() => new Date().toISOString().slice(0, 10))
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
  const [plateauRequired, setPlateauRequired] = useState(false)
  const [moffettRequired, setMoffettRequired] = useState(false)
  const [isReturnMovement, setIsReturnMovement] = useState(false)
  const [stops, setStops] = useState<StopFormRow[]>(() => [emptyStop('Loading'), emptyStop('Unloading')])
  // The intake starts with one goods line: lines are the pricing source of truth, so the
  // legacy header-inputs-vs-lines duality never appears on a brand new dossier.
  const [cargoItems, setCargoItems] = useState<CargoFormRow[]>(() => [emptyCargoRow()])
  const mutateStops = useStopMutation(stops, setStops, setCargoItems)
  const [quickCreate, setQuickCreate] = useState<{ name: string; resolve: (created: LocationOption | null) => void } | null>(null)

  const { customers, serviceOptions, unitMaster, locationHours, customerConfig } = useOrderFormData(customerId ?? '', stops)
  const { options: unitOptions } = useLookupOptions('/api/unit-types')
  const preferredUnits = customerConfig?.preferredUnits ?? []
  const customerOptions = useMemo(() => customers.map((c) => ({ value: c.id, label: c.name })), [customers])

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

  const routeTouched = stops.some((stop) => !isEmptyStopRow(stop))
  const goodsTouched =
    cargoItems.some((cargo) => !isEmptyCargoRow(cargo)) || goodsDescription.trim() !== '' ||
    quantity.trim() !== '' || notes.trim() !== ''
  const intakeTouched = routeTouched || goodsTouched || durationValue !== null

  const derivedFromCargo = cargoItems.length > 0
  const cargoSummary = derivedFromCargo ? computeCargoSummary(cargoItems, unitOptions) : null

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
    }),
    [
      customerId, customerReference, dossierDate, goodsDescription, quantity, quantityUnitCode,
      weightKg, volumeM3, palletCount, distanceKm, loadingMeters, adrRequired, craneRequired,
      plateauRequired, moffettRequired, isReturnMovement, notes, stops, cargoItems, serviceOptions,
    ],
  )
  const inlineErrors = fieldErrorMap(clientErrors)

  function setStop(key: string, patch: Partial<StopFormRow>) {
    mutateStops((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
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
    // Kraantransport implies a crane requirement; the checkbox stays editable in Goederen.
    if (nextType?.code === 'KRAANTRANSPORT') setCraneRequired(true)
  }

  function confirmPendingSwitch() {
    if (!pendingSwitch) return
    const { nextSelection, dropKeys } = pendingSwitch
    setPendingSwitch(null)
    const dropSet = new Set(dropKeys)
    mutateStops((rows) => rows.filter((row) => !dropSet.has(row.key)))
    applySelection(nextSelection, nextSelection === 'blanco' ? null : quickStartTypes.find((type) => type.id === nextSelection) ?? null)
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
    const submitValues: OrderFormValues = { ...values, stops: effectiveStops, cargoItems: effectiveCargo }

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
      // The ValidationSummary scrolls itself into view and lists every failing field.
      setFormError(t('dossiers.new.validationIntro'))
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
      setBusy(false)
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (busy) return
    if (!customerId) {
      setFormError(t('dossiers.new.customerRequired'))
      return
    }
    if (!selection) {
      setFormError(t('dossiers.new.typeRequired'))
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

  return (
    <div className="new-dossier">
      <BackButton to="/dossiers" label={t('dossiers.new.back')} />
      <PageHeader title={t('dossiers.new.title')} subtitle={t('dossiers.new.subtitleIntake')} />

      <form onSubmit={(event) => void submit(event)} noValidate>
        <ValidationSummary
          message={formError}
          fieldErrors={{
            ...fieldErrors,
            ...Object.fromEntries(clientErrors.map((error) => [error.field, [error.message]])),
          }}
          fieldLabels={Object.fromEntries(clientErrors.map((error) => [error.field, error.label]))}
        />

        <section className="new-dossier-section" aria-labelledby="nd-sectie-klant">
          <h2 id="nd-sectie-klant">{t('dossiers.new.sectionCustomer')}</h2>
          <FormField label={t('dossiers.new.customer')} htmlFor="nd-klant" required error={getFieldError(fieldErrors, 'customerId')}>
            <SearchableSelect
              id="nd-klant"
              value={customerId}
              onChange={setCustomerId}
              options={customerOptions}
              placeholder={t('dossiers.new.customerPlaceholder')}
              disabled={busy}
            />
          </FormField>

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
        </section>

        <section className="new-dossier-section" aria-labelledby="nd-sectie-ref">
          <h2 id="nd-sectie-ref">{t('dossiers.new.sectionReference')}</h2>
          <div className="new-dossier-row">
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
        </section>

        {!selection && (
          <p className="new-dossier-hint" role="note">
            {t('dossiers.new.chooseTypeHint')}
          </p>
        )}

        {showTransportIntake && (
          <>
            <section className="new-dossier-section" aria-labelledby="nd-sectie-route">
              <div className="new-dossier-section-header">
                <h2 id="nd-sectie-route">{t('dossiers.new.sectionRoute')}</h2>
                {isDistribution && (
                  <Button variant="secondary" onClick={addUnloadAddress} disabled={busy}>
                    {t('dossiers.new.addUnloadAddress')}
                  </Button>
                )}
              </div>
              <p className="new-dossier-hint">
                {isDistribution ? t('dossiers.new.routeHintDistribution') : t('dossiers.new.routeHintDirect')}
              </p>
              {showDuration && (
                <div className="new-dossier-row">
                  <FormField
                    label={t('dossiers.new.durationLabel')}
                    htmlFor="nd-duration"
                    hint={t('dossiers.new.durationHint')}
                    error={durationInvalid ? t('dossiers.new.durationInvalid') : undefined}
                  >
                    <input
                      id="nd-duration"
                      type="number"
                      min={0}
                      step="0.25"
                      value={durationHours}
                      onChange={(event) => setDurationHours(event.target.value)}
                      disabled={busy}
                      aria-invalid={durationInvalid ? true : undefined}
                    />
                  </FormField>
                </div>
              )}
              <RouteSection
                stops={stops}
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
            </section>

            <section className="new-dossier-section" aria-labelledby="nd-sectie-goederen">
              <h2 id="nd-sectie-goederen">{t('dossiers.new.sectionGoods')}</h2>
              <GoodsSection
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
                setCraneRequired={setCraneRequired}
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
                applyCargoUnit={applyCargoUnit}
                cargoDimensionsFixed={cargoDimensionsFixed}
                saving={busy}
                errors={inlineErrors}
              />
            </section>

            <section className="new-dossier-section" aria-labelledby="nd-sectie-opmerkingen">
              <h2 id="nd-sectie-opmerkingen">{t('dossiers.new.sectionNotes')}</h2>
              <FormField label={t('dossiers.new.notes')} htmlFor="nd-notes">
                <textarea
                  id="nd-notes"
                  rows={2}
                  value={notes}
                  onChange={(event) => setNotes(event.target.value)}
                  disabled={busy}
                  maxLength={2000}
                />
              </FormField>
            </section>
          </>
        )}

        {selectedType && !selectedType.hasStops && (
          <p className="new-dossier-hint" role="note">
            {t('dossiers.new.noRouteTypeHint', { name: selectedType.name })}
          </p>
        )}
        {selectedType?.hasStops && !canCreateOrders && (
          <p className="new-dossier-hint" role="note">
            {t('dossiers.new.noOrderPermissionHint')}
          </p>
        )}
        {isBlanco && (
          <p className="new-dossier-hint" role="note">
            {t('dossiers.new.blancoHint')}
          </p>
        )}

        <div className="new-dossier-actions">
          <Button variant="secondary" onClick={() => navigate('/dossiers')} disabled={busy}>
            {t('ui.actions.cancel')}
          </Button>
          <Button type="submit" disabled={!submitEnabled}>
            {busy ? t('dossiers.new.creating') : t('dossiers.new.create')}
          </Button>
        </div>
      </form>

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
