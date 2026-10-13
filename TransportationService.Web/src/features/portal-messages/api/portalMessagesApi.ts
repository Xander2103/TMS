import { apiClient } from '../../../api/apiClient'

export type PortalMessagePriority = 'Normal' | 'High' | 'Urgent'

export type PortalMessageDisplayMode = 'Notification' | 'DashboardBanner' | 'BlockingAcknowledgement'

/** Mirrors PortalMessageAdminDto (camelCase JSON). */
export interface PortalMessageAdminItem {
  id: string
  titleNl: string
  titleFr: string | null
  titleEn: string | null
  bodyNl: string
  bodyFr: string | null
  bodyEn: string | null
  priority: PortalMessagePriority
  displayMode: PortalMessageDisplayMode
  requiresAcknowledgement: boolean
  visibleFrom: string | null
  expiresAt: string | null
  relatedEntityType: 'order' | 'invoice' | null
  relatedEntityId: string | null
  emailRequested: boolean
  cancelledAt: string | null
  createdAt: string
  customerNames: string[]
}

/** Mirrors SendPortalMessageRequest. Multiple customers require portal_messages.send_bulk. */
export interface SendPortalMessageInput {
  titleNl: string
  bodyNl: string
  titleFr?: string | null
  bodyFr?: string | null
  titleEn?: string | null
  bodyEn?: string | null
  customerIds?: string[]
  portalUserIds?: string[]
  priority?: PortalMessagePriority
  displayMode?: PortalMessageDisplayMode
  requiresAcknowledgement?: boolean
  visibleFrom?: string | null
  expiresAt?: string | null
  relatedEntityType?: 'order' | 'invoice' | null
  relatedEntityId?: string | null
  sendEmail?: boolean
}

export interface PortalMessageDeliveryRow {
  userId: string
  name: string
  customerName: string
  readAt: string | null
  acknowledgedAt: string | null
  emailStatus: string
  emailFailureReason: string | null
}

export interface PortalMessageDeliveryStatus {
  messageId: string
  titleNl: string
  createdAt: string
  cancelledAt: string | null
  requiresAcknowledgement: boolean
  recipients: PortalMessageDeliveryRow[]
}

export function listPortalMessagesAdmin(): Promise<PortalMessageAdminItem[]> {
  return apiClient.getJson<PortalMessageAdminItem[]>('/api/portal-messages')
}

export function sendPortalMessage(input: SendPortalMessageInput): Promise<PortalMessageAdminItem> {
  return apiClient.postJson<PortalMessageAdminItem, SendPortalMessageInput>('/api/portal-messages', input)
}

export function getPortalMessageDeliveryStatus(id: string): Promise<PortalMessageDeliveryStatus> {
  return apiClient.getJson<PortalMessageDeliveryStatus>(`/api/portal-messages/${id}/delivery-status`)
}

export function cancelPortalMessage(id: string): Promise<void> {
  return apiClient.postJson<void, Record<string, never>>(`/api/portal-messages/${id}/cancel`, {})
}
