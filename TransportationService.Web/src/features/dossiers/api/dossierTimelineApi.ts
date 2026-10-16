import { apiClient } from '../../../api/apiClient'

/** Filter chips of the dossier history; mirrors DossierTimelineEventDto.Category. */
export const DOSSIER_TIMELINE_CATEGORIES = ['dossier', 'orders', 'planning', 'pricing', 'documents', 'incidents'] as const
export type DossierTimelineCategory = (typeof DOSSIER_TIMELINE_CATEGORIES)[number]

/** A Dutch-labelled detail; `before` is only set for a real change (old → new). */
export interface DossierTimelineField {
  label: string
  value: string | null
  before: string | null
}

/** Mirrors DossierTimelineEventDto (T-H6): one step of the dossier's story, oldest first. */
export interface DossierTimelineEvent {
  id: string
  timestamp: string
  category: DossierTimelineCategory | string
  /** Stable machine code ("trip.planned", "dossier.confirmed_automatically", …). */
  type: string
  /** The Dutch sentence the user reads. */
  title: string
  detail: string | null
  /** System = automatic event (never the person who happened to trigger it). */
  actorKind: 'User' | 'System' | 'Unknown'
  actorName: string | null
  fields: DossierTimelineField[]
}

/** Readable with dossiers.view — the global audit right is not needed. */
export function getDossierTimeline(dossierId: string, signal?: AbortSignal): Promise<DossierTimelineEvent[]> {
  return apiClient.getJson<DossierTimelineEvent[]>(`/api/dossiers/${dossierId}/timeline`, { signal })
}
