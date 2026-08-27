import { formatDecimal, formatPercent } from '../../utils/numbers'

export interface KpiCustomerRow {
  customerId: string
  customerName: string
  revenue: number
  allocatedCost: number
  profit: number
  marginPct: number | null
}

export interface KpiDriverKmRow {
  driverId: string
  driverName: string
  km: number
  hours: number
}

export interface KpiDashboard {
  revenueToday: number
  revenuePeriod: number
  profitToday: number
  profitPeriod: number
  averageMarginPct: number | null
  profitPerTrip: number | null
  tripCount: number
  vehicleUtilisationPct: number | null
  totalKm: number
  emptyKm: number
  emptyKmPct: number | null
  fuelLitres: number
  fuelCost: number
  co2Kg: number
  openDamageCount: number
  deliveryReliabilityPct: number | null
  onTimeArrivalPct: number | null
  avgEtaDeviationMinutes: number | null
  failedDeliveries: number
  partialDeliveries: number
  openExceptions: number
  costOverrunTripCount: number
  avgCostOverrunPct: number | null
  topCustomers: KpiCustomerRow[]
  kmPerDriver: KpiDriverKmRow[]
}

export interface TripProfitabilityRow {
  tripId: string
  tripNumber: string
  tripDate: string
  driverName: string | null
  vehicleNumber: string | null
  customerNames: string
  revenue: number
  estimatedCost: number
  projectedCost: number
  finalCost: number | null
  profit: number
  marginPct: number | null
  totalKm: number
  emptyKm: number
  status: string
  isFinalized: boolean
}

/** P11: one row per activity type (a dossier with crane + plateau feeds both rows). */
export interface ActivityKpiRow {
  activityTypeId: string
  code: string
  name: string
  kpiCategory: string | null
  activityCount: number
  linkedOrderCount: number
  revenue: number
  redeliveryCount: number
}

export interface ActivityKpiCategoryRow {
  kpiCategory: string | null
  activityCount: number
  linkedOrderCount: number
  revenue: number
  redeliveryCount: number
}

export interface ActivityKpiTotals {
  activityCount: number
  linkedOrderCount: number
  revenue: number
  redeliveryCount: number
}

export interface ActivityKpiReport {
  from: string
  to: string
  rows: ActivityKpiRow[]
  totals: ActivityKpiTotals
  /** Started pallet-days of storage stays overlapping the period; null when none. */
  palletDays: number | null
  perCategory: ActivityKpiCategoryRow[]
}

export interface KpiFilterState {
  from: string
  to: string
  customerId: string | null
  driverId: string | null
  vehicleId: string | null
}

/** "—" when the ratio is undefined (zero denominator); one decimal + % otherwise (tenant separators). */
export function pct(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '—'
  return formatPercent(value, 1)
}

export function num(value: number, decimals = 0): string {
  return formatDecimal(value, decimals)
}

export function isoDate(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

/** Period presets for the KPI filter bar; all end today. */
export function presetRange(preset: 'today' | 'week' | 'month'): { from: string; to: string } {
  const today = new Date()
  const to = isoDate(today)
  if (preset === 'today') return { from: to, to }
  if (preset === 'week') {
    const monday = new Date(today)
    monday.setDate(today.getDate() - ((today.getDay() + 6) % 7))
    return { from: isoDate(monday), to }
  }
  return { from: isoDate(new Date(today.getFullYear(), today.getMonth(), 1)), to }
}
