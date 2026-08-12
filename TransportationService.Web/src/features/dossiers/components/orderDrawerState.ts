import type { ServiceOption, UnitTypeMaster } from '../../tarification/api/pricingApi'
import type { TransportOrderDetail } from '../../transport-orders/types'
import {
  cargoFromOrder,
  serviceDaysFromOrder,
  serviceIdsFromOrder,
  serviceNotesFromOrder,
  servicePalletsFromOrder,
  serviceQuantitiesFromOrder,
  stopsFromOrder,
  type CargoFormRow,
  type OrderFormValues,
} from '../../transport-orders/components/sections/orderFormState'

/**
 * Whole-form value snapshot rebuilt from a loaded order. The dossier section drawers edit
 * ONE slice (stops or goods) and submit the untouched remainder verbatim, so a drawer save
 * round-trips exactly like the full order form (§17: same payload builder, same version gate).
 */
export function orderValuesFromDetail(order: TransportOrderDetail, serviceOptions: ServiceOption[]): OrderFormValues {
  const str = (value: number | null | undefined) => (value == null ? '' : String(value))
  return {
    customerId: order.customerId,
    customerReference: order.customerReference ?? '',
    orderDate: order.orderDate,
    goodsDescription: order.goodsDescription ?? '',
    quantity: str(order.quantity),
    quantityUnit: order.quantityUnit ?? '',
    quantityUnitCode: order.quantityUnitCode,
    weightKg: str(order.weightKg),
    volumeM3: str(order.volumeM3),
    palletCount: str(order.palletCount),
    distanceKm: str(order.distanceKm),
    loadingMeters: str(order.loadingMeters),
    adrRequired: order.adrRequired,
    craneRequired: order.craneRequired,
    plateauRequired: order.plateauRequired ?? false,
    moffettRequired: order.moffettRequired ?? false,
    isReturnMovement: order.isReturnMovement ?? false,
    agreedPrice: str(order.agreedPrice),
    notes: order.notes ?? '',
    legalEntityId: order.legalEntityId ?? '',
    dieselSurchargeOverride: order.dieselSurchargeOverride,
    dieselSurchargePercentOverride: str(order.dieselSurchargePercentOverride),
    dieselSurchargeOverrideReason: order.dieselSurchargeOverrideReason ?? '',
    stops: stopsFromOrder(order),
    cargoItems: cargoFromOrder(order),
    serviceOptions,
    selectedServiceOptionIds: serviceIdsFromOrder(order),
    serviceQuantities: serviceQuantitiesFromOrder(order),
    servicePallets: servicePalletsFromOrder(order),
    serviceDays: serviceDaysFromOrder(order),
    serviceNotes: serviceNotesFromOrder(order),
    priceIsManual: order.priceIsManual,
    priceOverrideReason: order.priceOverrideReason ?? '',
    pricingSource: order.pricingSource,
    oneOffFixedAmount: str(order.oneOffFixedAmount),
    oneOffTimeMode:
      order.oneOffIncludedCombinedMinutes != null
        ? 'combined'
        : order.oneOffIncludedLoadingMinutes != null || order.oneOffIncludedUnloadingMinutes != null
          ? 'separate'
          : 'none',
    oneOffIncludedLoadingMinutes: str(order.oneOffIncludedLoadingMinutes),
    oneOffIncludedUnloadingMinutes: str(order.oneOffIncludedUnloadingMinutes),
    oneOffIncludedCombinedMinutes: str(order.oneOffIncludedCombinedMinutes),
    oneOffExtraHourlyRate: str(order.oneOffExtraHourlyRate),
    oneOffNotes: order.oneOffNotes ?? '',
    includedLoadingMinutesOverride: str(order.includedLoadingMinutesOverride),
    includedUnloadingMinutesOverride: str(order.includedUnloadingMinutesOverride),
    extraTimeHourlyRateOverride: str(order.extraTimeHourlyRateOverride),
    extraTimeRoundingStepMinutes: str(order.extraTimeRoundingStepMinutes),
    extraTimeMinimumBillableMinutes: str(order.extraTimeMinimumBillableMinutes),
    version: order.version,
  }
}

/**
 * Selecting a unit auto-fills physical defaults from the unit master data (mirrors the
 * order form's applyCargoUnit): Fixed always sets them, DefaultButOverridable only fills
 * what is still empty, Variable leaves everything to the planner.
 */
export function applyUnitToCargoRow(row: CargoFormRow, code: string | null, master: UnitTypeMaster | null): CargoFormRow {
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
}
