import { apiClient } from '../../api/apiClient'

export type KpiQuality = 'Actual' | 'Estimated' | 'Unavailable'

export interface FleetKpiValue {
  key: string
  label: string
  value: number | null
  unit: string
  quality: KpiQuality
  detail: string | null
}

export interface FleetKpi {
  assetId: string
  from: string
  to: string
  values: FleetKpiValue[]
}

export type FleetKpiOwnerType = 'vehicle' | 'trailer'

export function getFleetKpi(
  ownerType: FleetKpiOwnerType,
  ownerId: string,
  from: string,
  to: string,
): Promise<FleetKpi> {
  const base = ownerType === 'vehicle' ? `/api/vehicles/${ownerId}/kpi` : `/api/trailers/${ownerId}/kpi`
  const query = new URLSearchParams({ from, to })
  return apiClient.getJson<FleetKpi>(`${base}?${query.toString()}`)
}
