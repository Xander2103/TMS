import { apiClient } from '../../../api/apiClient'
import type { KpiDashboard, KpiFilterState, TripProfitabilityRow } from '../types'

function query(filter: KpiFilterState): string {
  const params = new URLSearchParams({ from: filter.from, to: filter.to })
  if (filter.customerId) params.set('customerId', filter.customerId)
  if (filter.driverId) params.set('driverId', filter.driverId)
  if (filter.vehicleId) params.set('vehicleId', filter.vehicleId)
  return params.toString()
}

export function getKpiDashboard(filter: KpiFilterState): Promise<KpiDashboard> {
  return apiClient.getJson<KpiDashboard>(`/api/kpi/dashboard?${query(filter)}`)
}

export function getTripProfitability(filter: KpiFilterState): Promise<TripProfitabilityRow[]> {
  return apiClient.getJson<TripProfitabilityRow[]>(`/api/kpi/trip-profitability?${query(filter)}`)
}

export function kpiExportUrl(report: string, filter: KpiFilterState): string {
  return `/api/kpi/export?report=${encodeURIComponent(report)}&${query(filter)}`
}
