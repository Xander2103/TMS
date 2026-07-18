export type FuelWarningCode = 'OdometerLowerThanPrevious' | 'ConsumptionAboveAverage' | 'ConsumptionBelowAverage'

export const FUEL_WARNING_LABELS: Record<FuelWarningCode, string> = {
  OdometerLowerThanPrevious: 'Kilometerstand lager dan vorige tankbeurt',
  ConsumptionAboveAverage: 'Verbruik ver boven het gemiddelde',
  ConsumptionBelowAverage: 'Verbruik ver onder het gemiddelde',
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
