import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import type {
  CustomerNotificationOverride,
  MessageChannel,
  MessageTemplate,
  NotificationRule,
  OutboxRow,
  OutboxStatus,
  UpsertNotificationRuleInput,
} from '../types'

export function listNotificationRules(): Promise<NotificationRule[]> {
  return apiClient.getJson<NotificationRule[]>('/api/notification-rules')
}

export function updateNotificationRule(eventKey: string, input: UpsertNotificationRuleInput): Promise<void> {
  return apiClient.putJson<void, UpsertNotificationRuleInput>(`/api/notification-rules/${eventKey}`, input)
}

export function listCustomerNotificationOverrides(customerId: string): Promise<CustomerNotificationOverride[]> {
  return apiClient.getJson<CustomerNotificationOverride[]>(`/api/customers/${customerId}/notification-overrides`)
}

export function setCustomerNotificationOverride(
  customerId: string,
  eventKey: string,
  enabled: boolean | null,
): Promise<void> {
  return apiClient.putJson<void, { enabled: boolean | null }>(
    `/api/customers/${customerId}/notification-overrides/${eventKey}`,
    { enabled },
  )
}

export function getMessageTemplateKinds(): Promise<string[]> {
  return apiClient.getJson<string[]>('/api/message-templates/kinds')
}

export function getPlaceholders(eventKey: string | null): Promise<string[]> {
  const query = eventKey ? `?eventKey=${encodeURIComponent(eventKey)}` : ''
  return apiClient.getJson<string[]>(`/api/message-templates/placeholders${query}`)
}

export function listMessageTemplates(): Promise<MessageTemplate[]> {
  return apiClient.getJson<MessageTemplate[]>('/api/message-templates')
}

export interface SaveTemplateInput {
  kind: string
  channel: MessageChannel
  language: string
  subject: string | null
  body: string
  bodyHtml: string | null
  isActive: boolean
  customerId?: string | null
}

export function saveMessageTemplate(input: SaveTemplateInput): Promise<MessageTemplate> {
  return apiClient.postJson<MessageTemplate, SaveTemplateInput>('/api/message-templates', input)
}

export function deleteMessageTemplate(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/message-templates/${id}`)
}

export interface PreviewInput {
  kind: string
  channel: MessageChannel
  language: string
  tokens?: Record<string, string> | null
}

export interface PreviewResult {
  subject: string | null
  body: string
}

export function previewTemplate(input: PreviewInput): Promise<PreviewResult> {
  return apiClient.postJson<PreviewResult, PreviewInput>('/api/message-templates/preview', input)
}

export interface OutboxQuery {
  status?: OutboxStatus
  kind?: string
  channel?: MessageChannel
  search?: string
  page: number
  pageSize: number
}

export function listOutbox(query: OutboxQuery): Promise<PagedResult<OutboxRow>> {
  const params = new URLSearchParams()
  if (query.status) params.set('status', query.status)
  if (query.kind) params.set('kind', query.kind)
  if (query.channel) params.set('channel', query.channel)
  if (query.search) params.set('search', query.search)
  params.set('page', String(query.page))
  params.set('pageSize', String(query.pageSize))
  return apiClient.getJson<PagedResult<OutboxRow>>(`/api/messaging/outbox?${params.toString()}`)
}

export function retryOutboxMessage(id: string): Promise<void> {
  return apiClient.postJson<void, Record<string, never>>(`/api/messaging/outbox/${id}/retry`, {})
}
