import type { ServiceOption } from '../../tarification/api/pricingApi'
import type { TransportOrderDetail } from '../../transport-orders/types'
import {
  cargoFromOrder,
  serviceDaysFromOrder,
  serviceIdsFromOrder,
  serviceNotesFromOrder,
  servicePalletsFromOrder,
  serviceQuantitiesFromOrder,
  stopsFromOrder,
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
