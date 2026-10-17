import { apiClient } from '../../api/apiClient'

export type FleetAssetKind = 'vehicle' | 'trailer'

/** Mirrors FleetHistoryEventDto (FL-H3): one line of the vehicle/trailer story, newest first. */
export interface FleetHistoryEvent {
  id: string
  at: string
  /** System = automatic readiness moment (never the person whose action triggered it). */
  actorKind: 'User' | 'System'
  actorName: string | null
  /** lifecycle · status · readiness · assignment · document · inspection · tachograph · maintenance · damage · fuel · leasing · planning */
  category: string
  /** The Dutch sentence the user reads. */
  title: string
  detail: string | null
  /** True when the line carries amounts — only served to fleet_finance.view holders. */
  financial: boolean
  linkPath: string | null
  /** Raw before/after for the "Technische details" expander. */
  technical: string | null
}

/** Readable with vehicles.view / trailers.view; amounts are shaped away server-side without fleet_finance.view. */
export function getFleetHistory(kind: FleetAssetKind, assetId: string, signal?: AbortSignal): Promise<FleetHistoryEvent[]> {
  const base = kind === 'vehicle' ? 'vehicles' : 'trailers'
  return apiClient.getJson<FleetHistoryEvent[]>(`/api/${base}/${assetId}/history`, { signal })
}

/** The filter chips: each groups server categories into one operational question. */
export const FLEET_HISTORY_FILTERS = ['all', 'status', 'documents', 'maintenance', 'planning', 'costs'] as const
export type FleetHistoryFilter = (typeof FLEET_HISTORY_FILTERS)[number]

const GROUPS: Record<Exclude<FleetHistoryFilter, 'all' | 'costs'>, string[]> = {
  status: ['lifecycle', 'status', 'readiness', 'assignment'],
  documents: ['document', 'inspection', 'tachograph'],
  maintenance: ['maintenance', 'damage'],
  planning: ['planning'],
}

export function matchesFleetHistoryFilter(event: FleetHistoryEvent, filter: FleetHistoryFilter): boolean {
  if (filter === 'all') return true
  if (filter === 'costs') return event.financial || event.category === 'fuel' || event.category === 'leasing'
  return GROUPS[filter].includes(event.category)
}
