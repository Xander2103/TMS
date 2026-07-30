import { apiClient } from '../../../api/apiClient'

/** Mirrors PortalAnnouncementDto (camelCase JSON). */
export interface PortalAnnouncement {
  id: string
  title: string
  body: string
  activeFrom: string | null
  activeUntil: string | null
  isActive: boolean
}

export interface SavePortalAnnouncementInput {
  title: string
  body: string
  activeFrom: string | null
  activeUntil: string | null
  isActive: boolean
}

export function listPortalAnnouncementsAdmin(): Promise<PortalAnnouncement[]> {
  return apiClient.getJson<PortalAnnouncement[]>('/api/portal-announcements')
}

export function createPortalAnnouncement(input: SavePortalAnnouncementInput): Promise<PortalAnnouncement> {
  return apiClient.postJson<PortalAnnouncement, SavePortalAnnouncementInput>('/api/portal-announcements', input)
}

export function updatePortalAnnouncement(id: string, input: SavePortalAnnouncementInput): Promise<PortalAnnouncement> {
  return apiClient.putJson<PortalAnnouncement, SavePortalAnnouncementInput>(`/api/portal-announcements/${id}`, input)
}

export function deletePortalAnnouncement(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/portal-announcements/${id}`)
}
