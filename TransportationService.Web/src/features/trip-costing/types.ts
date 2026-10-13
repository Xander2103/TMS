export type TripCostPhase = 'Estimated' | 'Actual'

export type TripCostType =
  | 'Fuel'
  | 'Toll'
  | 'DriverLabour'
  | 'Overtime'
  | 'WaitingTime'
  | 'VehicleDistance'
  | 'VehicleTime'
  | 'Maintenance'
  | 'Depreciation'
  | 'Trailer'
  | 'Equipment'
  | 'Subcontractor'
  | 'FerryTunnelParking'
  | 'Manual'
  | 'Correction'

/** Vertaalsleutels — renderen als t(COST_TYPE_LABELS[type]). */
export const COST_TYPE_LABELS: Record<TripCostType, string> = {
  Fuel: 'tripCosting.costType.Fuel',
  Toll: 'tripCosting.costType.Toll',
  DriverLabour: 'tripCosting.costType.DriverLabour',
  Overtime: 'tripCosting.costType.Overtime',
  WaitingTime: 'tripCosting.costType.WaitingTime',
  VehicleDistance: 'tripCosting.costType.VehicleDistance',
  VehicleTime: 'tripCosting.costType.VehicleTime',
  Maintenance: 'tripCosting.costType.Maintenance',
  Depreciation: 'tripCosting.costType.Depreciation',
  Trailer: 'tripCosting.costType.Trailer',
  Equipment: 'tripCosting.costType.Equipment',
  Subcontractor: 'tripCosting.costType.Subcontractor',
  FerryTunnelParking: 'tripCosting.costType.FerryTunnelParking',
  Manual: 'tripCosting.costType.Manual',
  Correction: 'tripCosting.costType.Correction',
}

/** Vertaalsleutels — renderen als t(COST_PHASE_LABELS[phase]). */
export const COST_PHASE_LABELS: Record<TripCostPhase, string> = {
  Estimated: 'tripCosting.phase.Estimated',
  Actual: 'tripCosting.phase.Actual',
}

/** Manual line entry offers the operational/expense types, not the calculated ones. */
export const MANUAL_COST_TYPES: TripCostType[] = [
  'Toll',
  'FerryTunnelParking',
  'Subcontractor',
  'WaitingTime',
  'Manual',
  'Correction',
]

export interface TripCostLine {
  id: string
  phase: TripCostPhase
  costType: TripCostType
  description: string
  quantity: number
  unit: string
  unitRate: number
  amount: number
  source: string
  isManualOverride: boolean
  overrideReason: string | null
  calculatedAt: string
}

export interface TripOrderAllocation {
  transportOrderId: string
  orderNumber: string
  customerName: string
  customerId: string
  revenue: number
  allocatedCost: number
  profit: number
  marginPct: number | null
}

export interface TripProfitability {
  revenue: number
  cost: number
  grossProfit: number
  marginPct: number | null
  revenuePerKm: number | null
  costPerKm: number | null
  revenuePerHour: number | null
  costPerHour: number | null
  perOrder: TripOrderAllocation[]
}

export interface TripCosting {
  tripId: string
  tripNumber: string
  tripStatus: string
  isFinalized: boolean
  finalizedAt: string | null
  estimatedTotal: number
  actualTotal: number
  projectedTotal: number
  finalCost: number | null
  plannedDistanceKm: number | null
  plannedEmptyKm: number | null
  actualDistanceKm: number | null
  actualEmptyKm: number | null
  lines: TripCostLine[]
  profitability: TripProfitability | null
}

export interface CostRateSet {
  id: string
  effectiveFrom: string
  name: string | null
  fuelPricePerLitre: number
  defaultConsumptionLPer100Km: number
  vehicleCostPerKm: number
  vehicleCostPerHour: number
  driverCostPerHour: number
  employerCostMultiplier: number
  maintenanceCostPerKm: number
  depreciationPerDay: number
  trailerCostPerDay: number
  equipmentCostPerDay: number
  defaultTollPerTrip: number
  overtimeThresholdMinutesPerDay: number
  overtimeRateMultiplier: number
  waitingTimeCostPerHour: number
  co2KgPerLitreDiesel: number
  co2KgPerLitreOther: number
}

export type CostRateSetInput = Omit<CostRateSet, 'id'>

/** Zero-safe percentage formatter: "14,8%" or "—" when the margin is undefined. */
export function formatMarginPct(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '—'
  return `${value.toLocaleString('nl-BE', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%`
}
