export type RevenueSource = 'Invoiced' | 'Agreed' | 'None'

export const REVENUE_SOURCE_LABELS: Record<RevenueSource, string> = {
  Invoiced: 'Gefactureerd',
  Agreed: 'Afgesproken',
  None: 'Geen omzet',
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

export const DIMENSION_LABELS: Record<ProfitabilityDimension, string> = {
  Customer: 'Klanten',
  Driver: 'Chauffeurs',
  Vehicle: 'Voertuigen',
  Week: 'Per week',
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

export const COST_TYPE_LABELS: Record<string, string> = {
  Fuel: 'Brandstof',
  Toll: 'Tol/Maut',
  DriverLabour: 'Chauffeur',
  Overtime: 'Overuren',
  WaitingTime: 'Wachttijd',
  VehicleDistance: 'Voertuig (km)',
  VehicleTime: 'Voertuig (tijd)',
  Maintenance: 'Onderhoud',
  Depreciation: 'Afschrijving',
  Trailer: 'Oplegger',
  Equipment: 'Uitrusting',
  Subcontractor: 'Onderaannemer',
  FerryTunnelParking: 'Ferry/tunnel/parking',
  Manual: 'Handmatig',
  Correction: 'Correctie',
}

export function formatEuro(value: number): string {
  return value.toLocaleString('nl-BE', { style: 'currency', currency: 'EUR', maximumFractionDigits: 0 })
}

/** Margin tone: red below zero, amber under 10%, green otherwise. */
export function marginTone(marginPct: number | null): 'danger' | 'warning' | 'success' | 'neutral' {
  if (marginPct === null) return 'neutral'
  if (marginPct < 0) return 'danger'
  if (marginPct < 10) return 'warning'
  return 'success'
}
