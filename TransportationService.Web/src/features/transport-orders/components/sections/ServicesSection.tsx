import type { Dispatch, SetStateAction } from 'react'
import { Badge } from '../../../../components/ui/Badge'
import { Button } from '../../../../components/ui/Button'
import { FormField } from '../../../../components/ui/FormField'
import { useLocale } from '../../../../i18n/localeContext'
import type {
  CustomerServiceOptionPrice,
  PriceCalculationResult,
  ServiceOption,
} from '../../../tarification/api/pricingApi'
import { formatServiceValue } from '../../../tarification/serviceValueFormat'
import { SERVICE_KIND_LABELS, type StopFormRow } from './orderFormState'

interface ServicesSectionProps {
  saving: boolean
  serviceOptions: ServiceOption[]
  customerServiceById: Map<string, CustomerServiceOptionPrice>
  preview: PriceCalculationResult | null
  stops: StopFormRow[]
  /** Order-header pallet count, prefilled into per-pallet-day services. */
  palletCount: string
  pricingSource: 'Contract' | 'OneOff'
  selectedServiceOptionIds: string[]
  setSelectedServiceOptionIds: Dispatch<SetStateAction<string[]>>
  serviceQuantities: Record<string, string>
  setServiceQuantities: Dispatch<SetStateAction<Record<string, string>>>
  servicePallets: Record<string, string>
  setServicePallets: Dispatch<SetStateAction<Record<string, string>>>
  serviceDays: Record<string, string>
  setServiceDays: Dispatch<SetStateAction<Record<string, string>>>
  serviceNotes: Record<string, string>
  setServiceNotes: Dispatch<SetStateAction<Record<string, string>>>
  addServicePanelOpen: boolean
  setAddServicePanelOpen: Dispatch<SetStateAction<boolean>>
  draftServiceOptionId: string
  setDraftServiceOptionId: Dispatch<SetStateAction<string>>
  includedLoadingMinutesOverride: string
  setIncludedLoadingMinutesOverride: (value: string) => void
  includedUnloadingMinutesOverride: string
  setIncludedUnloadingMinutesOverride: (value: string) => void
  extraTimeHourlyRateOverride: string
  setExtraTimeHourlyRateOverride: (value: string) => void
  extraTimeRoundingStepMinutes: string
  setExtraTimeRoundingStepMinutes: (value: string) => void
  extraTimeMinimumBillableMinutes: string
  setExtraTimeMinimumBillableMinutes: (value: string) => void
  includedTimeOverrideOpen: boolean
  setIncludedTimeOverrideOpen: (value: boolean) => void
}

/** Services & toeslagen section: auto-applied, manual selection + add panel, laad- en lostijd. */
export function ServicesSection({
  saving,
  serviceOptions,
  customerServiceById,
  preview,
  stops,
  palletCount,
  pricingSource,
  selectedServiceOptionIds,
  setSelectedServiceOptionIds,
  serviceQuantities,
  setServiceQuantities,
  servicePallets,
  setServicePallets,
  serviceDays,
  setServiceDays,
  serviceNotes,
  setServiceNotes,
  addServicePanelOpen,
  setAddServicePanelOpen,
  draftServiceOptionId,
  setDraftServiceOptionId,
  includedLoadingMinutesOverride,
  setIncludedLoadingMinutesOverride,
  includedUnloadingMinutesOverride,
  setIncludedUnloadingMinutesOverride,
  extraTimeHourlyRateOverride,
  setExtraTimeHourlyRateOverride,
  extraTimeRoundingStepMinutes,
  setExtraTimeRoundingStepMinutes,
  extraTimeMinimumBillableMinutes,
  setExtraTimeMinimumBillableMinutes,
  includedTimeOverrideOpen,
  setIncludedTimeOverrideOpen,
}: ServicesSectionProps) {
  const { t } = useLocale()
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
            label={
              option.kind === 'PerHour'
                ? t('transportOrders.services.qtyHours', { name: option.name })
                : t('transportOrders.services.qtyStops', { name: option.name })
            }
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
          <FormField label={t('transportOrders.services.qtyDays', { name: option.name })} htmlFor={`svc-days-${option.id}`}>
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
            <FormField label={t('transportOrders.services.pallets', { name: option.name })} htmlFor={`svc-pallets-${option.id}`}>
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
            <FormField label={t('transportOrders.services.days', { name: option.name })} htmlFor={`svc-pd-days-${option.id}`}>
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
              label={t('transportOrders.services.palletDays', { name: option.name })}
              htmlFor={`svc-qty-${option.id}`}
              hint={
                palletsValue.trim() && daysValue.trim()
                  ? t('transportOrders.services.palletDaysHintCalc', {
                      pallets: palletsValue,
                      days: daysValue,
                      total: Number(palletsValue) * Number(daysValue),
                    })
                  : t('transportOrders.services.palletDaysHint')
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
    <FormField label={t('transportOrders.services.note', { name: option.name })} htmlFor={`svc-note-${option.id}`}>
      <input
        id={`svc-note-${option.id}`}
        type="text"
        value={serviceNotes[option.id] ?? ''}
        onChange={(e) => setServiceNotes((q) => ({ ...q, [option.id]: e.target.value }))}
        disabled={saving}
        placeholder={t('transportOrders.services.notePlaceholder')}
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
      : t('transportOrders.services.indicationFillQuantity', { value: formatServiceValue(draftServiceOption.kind, price) })
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
  const includedTimeSourceLabel = stops.some((s) => s.includedTimeMinutesOverride.trim() !== '')
    ? t('transportOrders.services.sourceStop')
    : hasIncludedTimeOverride
      ? t('transportOrders.services.sourceOrder')
      : includedTimeInfo?.source === 'Contract'
        ? t('transportOrders.services.sourceContract')
        : t('transportOrders.services.sourceNone')
  // Mirrors PricingEngine.ResolveIncludedTime: the combined allowance only survives while NEITHER
  // per-activity minutes override is set — an override to the rate/rounding/minimum alone does not
  // switch the agreement out of combined mode, so the combined row must still show.
  const hasIncludedTimeMinutesOverride =
    includedLoadingMinutesOverride.trim() !== '' || includedUnloadingMinutesOverride.trim() !== ''
  const includedCombinedMinutes = !hasIncludedTimeMinutesOverride ? includedTimeInfo?.includedCombinedMinutes ?? null : null
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

  return (
    <>
      <p className="ui-form-section-description">
        {t('transportOrders.services.description')}
      </p>

      <div className="tof-service-group">
        <h4>{t('transportOrders.services.autoTitle')}</h4>
        {autoAppliedServiceLines.length === 0 && (
          <p className="placeholder-text">{t('transportOrders.services.autoEmpty')}</p>
        )}
        {autoAppliedServiceLines.length > 0 && (
          <div className="tof-service-options">
            {autoAppliedServiceLines.map((line) => (
              <div key={line.serviceOptionId} className="tof-service-option">
                <label className="tof-checkbox">
                  <input type="checkbox" checked readOnly disabled />
                  <span>
                    {line.name} <Badge>{t('transportOrders.lineKind.Auto')}</Badge>{' '}
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
        <h4>{t('transportOrders.services.manualTitle')}</h4>
        {manuallySelectedServiceOptionIds.length === 0 && (
          <p className="placeholder-text">{t('transportOrders.services.manualEmpty')}</p>
        )}
        {manuallySelectedServiceOptionIds.length > 0 && (
          <div className="tof-service-options">
            {manuallySelectedServiceOptionIds.map((id) => {
              const option = serviceOptions.find((o) => o.id === id)
              if (!option) return null
              const source = customerServiceById.get(option.id)?.source ?? t('transportOrders.services.sourceDefault')
              return (
                <div key={id} className="tof-service-option">
                  <div className="tof-service-option-header">
                    <span>
                      {option.name} <Badge tone="info">{t('transportOrders.lineKind.Manual')}</Badge>{' '}
                      <span className="customer-form-muted">
                        ({t(SERVICE_KIND_LABELS[option.kind])} — {formatServiceValue(option.kind, effectiveOptionPrice(option))} — {source})
                      </span>
                    </span>
                    <button
                      type="button"
                      className="tof-link tof-link-danger"
                      onClick={() => removeSelectedService(id)}
                      disabled={saving}
                    >
                      {t('ui.actions.delete')}
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
            {t('transportOrders.services.addService')}
          </Button>
        </div>

        {addServicePanelOpen && (
          <div className="tof-service-add-panel">
            {addableServiceOptions.length === 0 && (
              <p className="placeholder-text">{t('transportOrders.services.noMore')}</p>
            )}
            <FormField label={t('transportOrders.services.serviceField')} htmlFor="svc-add-select">
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
                <option value="">{t('transportOrders.services.choose')}</option>
                {addableServiceOptions.map((option) => (
                  <option key={option.id} value={option.id}>
                    {option.name}
                  </option>
                ))}
              </select>
            </FormField>
            {draftServiceOption && (
              <>
                <FormField label={t('transportOrders.services.calcMethod')} htmlFor="svc-add-kind">
                  <input id="svc-add-kind" type="text" value={t(SERVICE_KIND_LABELS[draftServiceOption.kind])} readOnly disabled />
                </FormField>
                {renderServiceQuantityInputs(draftServiceOption)}
                {renderServiceNoteInput(draftServiceOption)}
                <p className="customer-form-muted">{t('transportOrders.services.priceIndication', { value: draftPriceIndication ?? '' })}</p>
              </>
            )}
            <div className="tof-stop-toolbar">
              <Button variant="secondary" onClick={addDraftService} disabled={saving || !draftServiceOptionId}>
                {t('ui.actions.add')}
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
                {t('ui.actions.cancel')}
              </button>
            </div>
          </div>
        )}
      </div>

      <div className="tof-service-group">
        <h4>{t('transportOrders.services.notAppliedTitle')}</h4>
        {notAppliedServiceLines.length === 0 ? (
          <p className="placeholder-text">{t('transportOrders.services.allApplied')}</p>
        ) : (
          <ul className="tof-service-not-applied">
            {notAppliedServiceLines.map((line, index) => (
              <li key={`${line.label}-${index}`}>{line.label}</li>
            ))}
          </ul>
        )}
      </div>

      <div className="tof-service-group">
        <h4>{t('transportOrders.services.timeTitle')}</h4>
        {pricingSource === 'OneOff' ? (
          <p className="ui-form-field-hint">
            {t('transportOrders.services.oneOffTimeHint')}
          </p>
        ) : (
          <>
            {includedCombinedMinutes != null ? (
              <p>{t('transportOrders.services.combinedIncluded', { minutes: includedCombinedMinutes })}</p>
            ) : (
              <>
                <p>
                  {t('transportOrders.services.includedLoading', {
                    value:
                      effectiveIncludedLoadingMinutes != null
                        ? t('transportOrders.services.minutesValue', { count: effectiveIncludedLoadingMinutes })
                        : '—',
                  })}
                </p>
                <p>
                  {t('transportOrders.services.includedUnloading', {
                    value:
                      effectiveIncludedUnloadingMinutes != null
                        ? t('transportOrders.services.minutesValue', { count: effectiveIncludedUnloadingMinutes })
                        : '—',
                  })}
                </p>
              </>
            )}
            <p className="customer-form-muted">{includedTimeSourceLabel}</p>

            {!includedTimeOverrideOpen && (
              <div className="tof-stop-toolbar">
                <Button variant="secondary" onClick={() => setIncludedTimeOverrideOpen(true)} disabled={saving}>
                  {t('transportOrders.services.override')}
                </Button>
              </div>
            )}

            {includedTimeOverrideOpen && (
              <div className="tof-stop">
                <div className="tof-row">
                  <FormField label={t('transportOrders.services.overrideLoading')} htmlFor="to-included-loading-override">
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
                  <FormField label={t('transportOrders.services.overrideUnloading')} htmlFor="to-included-unloading-override">
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
                  <FormField label={t('transportOrders.services.overrideRate')} htmlFor="to-extra-rate-override">
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
                  <FormField label={t('transportOrders.services.overrideRounding')} htmlFor="to-extra-rounding-override">
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
                  <FormField label={t('transportOrders.services.overrideMinimum')} htmlFor="to-extra-minimum-override">
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
                      {t('transportOrders.services.resetContract')}
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
}
