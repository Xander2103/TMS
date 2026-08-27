import { getActiveLocale } from '../../i18n/activeLocale'
import { formatDecimal } from '../../utils/numbers'

export type RevenueSource = 'Invoiced' | 'Agreed' | 'None'

/** Vertaalsleutels — renderen als t(REVENUE_SOURCE_LABELS[source]). */
export const REVENUE_SOURCE_LABELS: Record<RevenueSource, string> = {
  Invoiced: 'profitability.revenueSource.Invoiced',
  Agreed: 'profitability.revenueSource.Agreed',
  None: 'profitability.revenueSource.None',
}

export interface TripProfitability {
  tripId: string
  tripNumber: string
  tripDate: string
  status: string
  driverName: string | null
  vehicleNumber: string | null
  customerSummary: string | null
  orderCount: number
  stopCount: number
  packageCount: number
  distanceKm: number | null
  distanceIsActual: boolean
  agreedRevenue: number
  invoicedRevenue: number
  paidRevenue: number
  revenueUsed: number
  revenueSourceUsed: RevenueSource
  actualCost: number
  estimatedCost: number
  projectedCost: number
  missingCostTypes: string[]
  margin: number
  marginPct: number | null
  revenuePerKm: number | null
  costPerKm: number | null
  costPerStop: number | null
  costPerPackage: number | null
  isFinalized: boolean
}

export interface ProfitabilitySummary {
  tripCount: number
  revenueUsed: number
  actualCost: number
  estimatedCost: number
  projectedCost: number
  margin: number
  marginPct: number | null
  tripsWithMissingData: number
  unprofitableTrips: number
}

export interface ProfitabilityOverview {
  from: string
  to: string
  summary: ProfitabilitySummary
  trips: TripProfitability[]
}

export type ProfitabilityDimension = 'Customer' | 'Driver' | 'Vehicle' | 'Week'

/** Vertaalsleutels — renderen als t(DIMENSION_LABELS[dimension]). */
export const DIMENSION_LABELS: Record<ProfitabilityDimension, string> = {
  Customer: 'profitability.dimension.Customer',
  Driver: 'profitability.dimension.Driver',
  Vehicle: 'profitability.dimension.Vehicle',
  Week: 'profitability.dimension.Week',
}

export interface ProfitabilityGroup {
  key: string
  label: string
  tripCount: number
  revenue: number
  projectedCost: number
  margin: number
  marginPct: number | null
  containsAllocatedCosts: boolean
}

export interface ExplanationLine {
  kind: string
  description: string
  amount: number
  phase: string | null
  source: string | null
  isManualOverride: boolean
  overrideReason: string | null
}

export interface TripExplanation {
  tripId: string
  tripNumber: string
  revenueLines: ExplanationLine[]
  costLines: ExplanationLine[]
  missingCostTypes: string[]
  calculationNote: string
}

/** Vertaalsleutels — renderen als t(COST_TYPE_LABELS[type] ?? …) met de code als fallback. */
export const COST_TYPE_LABELS: Record<string, string> = {
  Fuel: 'profitability.costType.Fuel',
  Toll: 'profitability.costType.Toll',
  DriverLabour: 'profitability.costType.DriverLabour',
  Overtime: 'profitability.costType.Overtime',
  WaitingTime: 'profitability.costType.WaitingTime',
  VehicleDistance: 'profitability.costType.VehicleDistance',
  VehicleTime: 'profitability.costType.VehicleTime',
  Maintenance: 'profitability.costType.Maintenance',
  Depreciation: 'profitability.costType.Depreciation',
  Trailer: 'profitability.costType.Trailer',
  Equipment: 'profitability.costType.Equipment',
  Subcontractor: 'profitability.costType.Subcontractor',
  FerryTunnelParking: 'profitability.costType.FerryTunnelParking',
  Manual: 'profitability.costType.Manual',
  Correction: 'profitability.costType.Correction',
}

/** Hele euro's met tenant-cijferconventie en symboolpositie per UI-taal (0 decimalen). */
export function formatEuro(value: number): string {
  const body = formatDecimal(value, 0)
  switch (getActiveLocale()) {
    case 'fr':
      return `${body} €`
    case 'en':
      return `€${body}`
    default:
      return `€ ${body}`
  }
}

/** Margin tone: red below zero, amber under 10%, green otherwise. */
export function marginTone(marginPct: number | null): 'danger' | 'warning' | 'success' | 'neutral' {
  if (marginPct === null) return 'neutral'
  if (marginPct < 0) return 'danger'
  if (marginPct < 10) return 'warning'
  return 'success'
}
