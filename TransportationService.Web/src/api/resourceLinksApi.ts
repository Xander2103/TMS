import { apiClient } from './apiClient'

export type ResourceLinkKind = 'Favorite' | 'Recent' | 'Pinned'

export interface ResourceLink {
  id: string
  kind: ResourceLinkKind
  entityType: string
  entityId: string
  label: string
  subtitle: string | null
  route: string
  sortOrder: number
  touchedAt: string
}

export interface TouchResourceLinkInput {
  kind: ResourceLinkKind
  entityType: string
  entityId: string
  label: string
  subtitle?: string | null
  route: string
}

export function listResourceLinks(kind?: ResourceLinkKind): Promise<ResourceLink[]> {
  return apiClient.getJson<ResourceLink[]>(`/api/me/resource-links${kind ? `?kind=${kind}` : ''}`)
}

/** Upsert: creates the link or refreshes its label/route and touch time. */
export function touchResourceLink(input: TouchResourceLinkInput): Promise<ResourceLink> {
  return apiClient.putJson<ResourceLink, TouchResourceLinkInput>('/api/me/resource-links', {
    subtitle: null,
    ...input,
  })
}

export function deleteResourceLink(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/me/resource-links/${id}`)
}

export function reorderResourceLinks(ids: string[]): Promise<void> {
  return apiClient.postJson<void, { ids: string[] }>('/api/me/resource-links/order', { ids })
}

/** Fire-and-forget recent-item tracking from detail pages; never blocks navigation. */
export function recordRecentItem(input: Omit<TouchResourceLinkInput, 'kind'>): void {
  void touchResourceLink({ ...input, kind: 'Recent' }).catch(() => undefined)
}
