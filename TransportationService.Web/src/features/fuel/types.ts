export type FuelWarningCode = 'OdometerLowerThanPrevious' | 'ConsumptionAboveAverage' | 'ConsumptionBelowAverage'

/** i18n-keys (fuel.warning.*) — render via t(FUEL_WARNING_LABELS[code]). */
export const FUEL_WARNING_LABELS: Record<FuelWarningCode, string> = {
  OdometerLowerThanPrevious: 'fuel.warning.OdometerLowerThanPrevious',
  ConsumptionAboveAverage: 'fuel.warning.ConsumptionAboveAverage',
  ConsumptionBelowAverage: 'fuel.warning.ConsumptionBelowAverage',
}

export interface FuelTransaction {
  id: string
  vehicleId: string
  driverId: string | null
  driverName: string | null
  tankCardId: string | null
  tankCardNumber: string | null
  transactionDate: string
  litres: number
  totalAmount: number
  pricePerLitre: number | null
  odometerKm: number | null
  station: string | null
  fullTank: boolean
  consumptionLPer100Km: number | null
  warnings: FuelWarningCode[]
  notes: string | null
}

export interface FuelOverview {
  items: FuelTransaction[]
  averageConsumptionLPer100Km: number | null
  totalLitres: number
  totalAmount: number
}

export interface FuelWarning {
  transactionId: string
  vehicleId: string
  vehicleInternalNumber: string
  vehicleLicensePlate: string
  transactionDate: string
  litres: number
  consumptionLPer100Km: number | null
  warnings: FuelWarningCode[]
}

export interface FuelTransactionInput {
  driverId: string | null
  tankCardId: string | null
  transactionDate: string
  litres: number
  totalAmount: number
  odometerKm: number | null
  station: string | null
  fullTank: boolean
  notes: string | null
}
