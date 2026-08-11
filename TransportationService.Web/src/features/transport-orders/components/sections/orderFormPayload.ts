import { computeVolumeM3 } from '../../../../utils/volume'
import type { TransportOrderInput } from '../../types'
import { numberOrNullFrom, type OrderFormValues } from './orderFormState'

/**
 * Submit-payload mapping of the transport-order form (Wave 1 phase 6). Split from
 * orderFormState.ts to respect the per-file size budget; consumes the same OrderFormValues
 * snapshot the validation reads.
 */

/** Service selections (id + quantities + note) as submitted and previewed. */
export function buildServiceSelections(values: OrderFormValues): NonNullable<TransportOrderInput['services']> {
  const { selectedServiceOptionIds, serviceOptions, serviceQuantities, servicePallets, serviceDays, serviceNotes } = values
  return selectedServiceOptionIds.map((id) => {
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
}

export interface OneOffPreviewInput {
  fixedAmount: number
  includedLoadingMinutes: number | null
  includedUnloadingMinutes: number | null
  includedCombinedMinutes: number | null
  extraHourlyRate: number | null
  notes: string | null
}

/** One-off pricing input built from the form state; null when Contract mode (nothing to send). */
export function buildOneOffPreviewInput(values: OrderFormValues): OneOffPreviewInput | null {
  if (values.pricingSource !== 'OneOff' || values.oneOffFixedAmount.trim() === '') return null
  return {
    fixedAmount: Number(values.oneOffFixedAmount),
    includedLoadingMinutes: values.oneOffTimeMode === 'separate' && values.oneOffIncludedLoadingMinutes.trim()
      ? Number(values.oneOffIncludedLoadingMinutes)
      : null,
    includedUnloadingMinutes: values.oneOffTimeMode === 'separate' && values.oneOffIncludedUnloadingMinutes.trim()
      ? Number(values.oneOffIncludedUnloadingMinutes)
      : null,
    includedCombinedMinutes: values.oneOffTimeMode === 'combined' && values.oneOffIncludedCombinedMinutes.trim()
      ? Number(values.oneOffIncludedCombinedMinutes)
      : null,
    extraHourlyRate: values.oneOffExtraHourlyRate.trim() ? Number(values.oneOffExtraHourlyRate) : null,
    notes: values.oneOffNotes.trim() || null,
  }
}


// --- Submit payload ---

/** Maps the validated form values onto the API input (create + update share this shape). */
export function buildSubmitPayload(values: OrderFormValues): TransportOrderInput {
  return {
    customerId: values.customerId,
    customerReference: values.customerReference.trim() || null,
    orderDate: values.orderDate || null,
    goodsDescription: values.goodsDescription.trim() || null,
    quantity: values.quantity === '' ? null : Number(values.quantity),
    quantityUnit: values.quantityUnit.trim() || null,
    quantityUnitCode: values.quantityUnitCode,
    weightKg: values.weightKg === '' ? null : Number(values.weightKg),
    volumeM3: values.volumeM3 === '' ? null : Number(values.volumeM3),
    palletCount: values.palletCount === '' ? null : Number(values.palletCount),
    adrRequired: values.adrRequired,
    craneRequired: values.craneRequired,
    agreedPrice: values.agreedPrice === '' ? null : Number(values.agreedPrice),
    notes: values.notes.trim() || null,
    legalEntityId: values.legalEntityId || null,
    dieselSurchargeOverride: values.dieselSurchargeOverride,
    dieselSurchargePercentOverride: values.dieselSurchargeOverride
      ? numberOrNullFrom(values.dieselSurchargePercentOverride)
      : null,
    dieselSurchargeOverrideReason: values.dieselSurchargeOverride
      ? values.dieselSurchargeOverrideReason.trim() || null
      : null,
    serviceOptionIds: values.selectedServiceOptionIds,
    services: buildServiceSelections(values),
    priceIsManual: values.priceIsManual,
    priceOverrideReason: values.priceIsManual ? values.priceOverrideReason.trim() || null : null,
    pricingSource: values.pricingSource,
    oneOffFixedAmount: values.pricingSource === 'OneOff' && values.oneOffFixedAmount.trim()
      ? Number(values.oneOffFixedAmount)
      : null,
    oneOffIncludedLoadingMinutes:
      values.pricingSource === 'OneOff' && values.oneOffTimeMode === 'separate' && values.oneOffIncludedLoadingMinutes.trim()
        ? Number(values.oneOffIncludedLoadingMinutes)
        : null,
    oneOffIncludedUnloadingMinutes:
      values.pricingSource === 'OneOff' && values.oneOffTimeMode === 'separate' && values.oneOffIncludedUnloadingMinutes.trim()
        ? Number(values.oneOffIncludedUnloadingMinutes)
        : null,
    oneOffIncludedCombinedMinutes:
      values.pricingSource === 'OneOff' && values.oneOffTimeMode === 'combined' && values.oneOffIncludedCombinedMinutes.trim()
        ? Number(values.oneOffIncludedCombinedMinutes)
        : null,
    oneOffExtraHourlyRate: values.pricingSource === 'OneOff' && values.oneOffExtraHourlyRate.trim()
      ? Number(values.oneOffExtraHourlyRate)
      : null,
    oneOffNotes: values.pricingSource === 'OneOff' ? values.oneOffNotes.trim() || null : null,
    includedLoadingMinutesOverride:
      values.pricingSource === 'OneOff' ? null : numberOrNullFrom(values.includedLoadingMinutesOverride),
    includedUnloadingMinutesOverride:
      values.pricingSource === 'OneOff' ? null : numberOrNullFrom(values.includedUnloadingMinutesOverride),
    extraTimeHourlyRateOverride:
      values.pricingSource === 'OneOff' ? null : numberOrNullFrom(values.extraTimeHourlyRateOverride),
    extraTimeRoundingStepMinutes:
      values.pricingSource === 'OneOff' ? null : numberOrNullFrom(values.extraTimeRoundingStepMinutes),
    extraTimeMinimumBillableMinutes:
      values.pricingSource === 'OneOff' ? null : numberOrNullFrom(values.extraTimeMinimumBillableMinutes),
    // Wave 1 §10: concurrency token round-trip; absent on create (JSON drops undefined).
    version: values.version,
    stops: values.stops.map((stop) => ({
      // Phase 7: echo the existing stop id (snapshot carry-over) + the pending re-copy flag.
      id: stop.id,
      refreshSnapshot: stop.refreshSnapshot,
      stopType: stop.stopType,
      locationId: stop.locationId || null,
      locationName: stop.locationId ? null : stop.locationName.trim() || null,
      address: stop.address.trim() || null,
      postalCode: stop.postalCode.trim() || null,
      city: stop.city.trim() || null,
      countryCode: stop.countryCode.trim() || null,
      // §14: one date + optional from/to time; a date without times keeps the day itself.
      plannedFrom: stop.date ? `${stop.date}T${stop.fromTime || '00:00'}:00Z` : null,
      plannedTo: stop.date && stop.toTime ? `${stop.date}T${stop.toTime}:00Z` : null,
      timeRequirement: stop.timeRequirement || 'None',
      timeRequirementFrom:
        (stop.timeRequirement === 'After' || stop.timeRequirement === 'Window') && stop.timeReqFrom
          ? stop.timeReqFrom
          : null,
      timeRequirementTo:
        (stop.timeRequirement === 'Before' || stop.timeRequirement === 'Window') && stop.timeReqTo
          ? stop.timeReqTo
          : null,
      includedTimeMinutesOverride: numberOrNullFrom(stop.includedTimeMinutesOverride),
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
    cargoItems: values.cargoItems.map((cargo) => ({
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
      palletCount: numberOrNullFrom(cargo.palletCount),
    })),
  }
}
