import { apiClient } from '../../api/apiClient'
import type { AssetReadiness, AssetReferences, FleetAssetKind, TripReference } from './types'

const base = (kind: FleetAssetKind, id: string) => `/api/${kind === 'vehicle' ? 'vehicles' : 'trailers'}/${id}`

/** Fleet readiness for a day (default: today, server time). */
export function getAssetReadiness(kind: FleetAssetKind, id: string, date?: string): Promise<AssetReadiness> {
  return apiClient.getJson<AssetReadiness>(`${base(kind, id)}/readiness${date ? `?date=${date}` : ''}`)
}

export function getAssetReferences(kind: FleetAssetKind, id: string): Promise<AssetReferences> {
  return apiClient.getJson<AssetReferences>(`${base(kind, id)}/references`)
}

/** The asset's most recent trips (newest first) — e.g. the damage report's trip picker. */
export function getAssetTrips(kind: FleetAssetKind, id: string, limit = 30): Promise<TripReference[]> {
  return apiClient.getJson<TripReference[]>(`${base(kind, id)}/trips?limit=${limit}`)
}
