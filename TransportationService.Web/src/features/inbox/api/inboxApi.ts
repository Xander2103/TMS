import { apiClient } from '../../../api/apiClient'
import { createLookupApi } from '../../master-data/api/lookupApi'
import type { LookupOption } from '../../master-data/types'

/** Mirrors MessagePriority (serialized as string). */
export type MessagePriority = 'Normal' | 'High' | 'Urgent'

/** Mirrors InternalMessageDto (camelCase JSON). */
export interface InboxMessage {
  id: string
  subject: string
  body: string
  senderName: string
  sentAt: string
  readAt: string | null
  recipientCount: number
  priority: MessagePriority
  requiresAcknowledgement: boolean
  acknowledgedAt: string | null
  visibleFrom: string | null
  expiresAt: string | null
  /** Only meaningful in the sender's "Verzonden" list. */
  cancelledAt: string | null
}

export interface MessageRecipientOption {
  userId: string
  name: string
}

/** Mirrors SendInternalMessageRequest. Bulk targeting (role/department/all) needs messages.send_bulk. */
export interface SendInternalMessageInput {
  subject: string
  body: string
  userIds: string[]
  roleId: string | null
  departmentId: string | null
  allEmployees: boolean
  priority: MessagePriority
  requiresAcknowledgement: boolean
  visibleFrom: string | null
  expiresAt: string | null
  sendEmail: boolean
}

export type EmailDeliveryStatus = 'Geen' | 'Pending' | 'Sent' | 'Failed' | 'Suppressed' | 'Duplicate'

export interface MessageDeliveryRecipient {
  userId: string
  name: string
  readAt: string | null
  acknowledgedAt: string | null
  emailStatus: EmailDeliveryStatus
  emailFailureReason: string | null
}

export interface MessageDeliveryStatus {
  messageId: string
  subject: string
  sentAt: string
  cancelledAt: string | null
  requiresAcknowledgement: boolean
  recipients: MessageDeliveryRecipient[]
}

export function listInboxMessages(): Promise<InboxMessage[]> {
  return apiClient.getJson<InboxMessage[]>('/api/internal-messages/inbox')
}

export function listSentMessages(): Promise<InboxMessage[]> {
  return apiClient.getJson<InboxMessage[]>('/api/internal-messages/sent')
}

export function listMessageRecipients(): Promise<MessageRecipientOption[]> {
  return apiClient.getJson<MessageRecipientOption[]>('/api/internal-messages/recipients')
}

/** Department options for bulk targeting (same lookup endpoint as the master-data screens). */
export function listDepartmentOptions(): Promise<LookupOption[]> {
  return createLookupApi('/api/departments').options()
}

export function sendInternalMessage(input: SendInternalMessageInput): Promise<{ recipients: number }> {
  return apiClient.postJson<{ recipients: number }, SendInternalMessageInput>('/api/internal-messages', input)
}

export function markMessageRead(id: string): Promise<void> {
  return apiClient.postJson<void, Record<string, never>>(`/api/internal-messages/${id}/read`, {})
}

/** Explicit confirmation by the recipient; 400 when the message does not require acknowledgement. */
export function acknowledgeMessage(id: string): Promise<void> {
  return apiClient.postJson<void, Record<string, never>>(`/api/internal-messages/${id}/acknowledge`, {})
}

export function getMessageDeliveryStatus(id: string): Promise<MessageDeliveryStatus> {
  return apiClient.getJson<MessageDeliveryStatus>(`/api/internal-messages/${id}/delivery-status`)
}

export function cancelInternalMessage(id: string): Promise<void> {
  return apiClient.postJson<void, Record<string, never>>(`/api/internal-messages/${id}/cancel`, {})
}
