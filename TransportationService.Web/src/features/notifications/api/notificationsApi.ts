import { apiClient } from '../../../api/apiClient'

export type NotificationCategory =
  | 'General'
  | 'Orders'
  | 'Planning'
  | 'Execution'
  | 'Hr'
  | 'System'
  | 'Inventory'
  | 'Task'
  | 'CustomerPortal'
  | 'Fleet'
  | 'Document'
  | 'Approval'
export type NotificationSeverity = 'Info' | 'Warning' | 'Critical' | 'Success'

export const NOTIFICATION_CATEGORY_LABELS: Record<NotificationCategory, string> = {
  General: 'Algemeen',
  Orders: 'Opdrachten',
  Planning: 'Planning',
  Execution: 'Uitvoering',
  Hr: 'HR',
  System: 'Systeem',
  Inventory: 'Voorraad',
  Task: 'Taken',
  CustomerPortal: 'Klantportaal',
  Fleet: 'Wagenpark',
  Document: 'Documenten',
  Approval: 'Goedkeuringen',
}

export const NOTIFICATION_CATEGORIES: NotificationCategory[] = [
  'General',
  'Orders',
  'Planning',
  'Execution',
  'Hr',
  'System',
  'Inventory',
  'Task',
  'CustomerPortal',
  'Fleet',
  'Document',
  'Approval',
]

export const NOTIFICATION_SEVERITY_ICONS: Record<NotificationSeverity, string> = {
  Info: 'ℹ',
  Warning: '⚠',
  Critical: '⛔',
  Success: '✅',
}

export interface Notification {
  id: string
  type: string
  category: NotificationCategory
  severity: NotificationSeverity
  title: string
  message: string
  linkPath: string | null
  isRead: boolean
  isArchived: boolean
  createdAt: string
  requiresAcknowledgement: boolean
  acknowledgedAt: string | null
  resolvedAt: string | null
  expiresAt: string | null
}

export interface NotificationPreference {
  category: NotificationCategory
  enabled: boolean
}

export function listNotifications(
  options: { unreadOnly?: boolean; category?: NotificationCategory; includeArchived?: boolean; take?: number } = {},
): Promise<Notification[]> {
  const query = new URLSearchParams()
  if (options.unreadOnly) query.set('unreadOnly', 'true')
  if (options.category) query.set('category', options.category)
  if (options.includeArchived) query.set('includeArchived', 'true')
  query.set('take', String(options.take ?? 50))
  return apiClient.getJson<Notification[]>(`/api/notifications?${query.toString()}`)
}

export function getUnreadCount(): Promise<{ count: number }> {
  return apiClient.getJson<{ count: number }>('/api/notifications/unread-count')
}

export function markNotificationRead(id: string): Promise<void> {
  return apiClient.postJson<void, Record<string, never>>(`/api/notifications/${id}/read`, {})
}

export function markAllNotificationsRead(): Promise<void> {
  return apiClient.postJson<void, Record<string, never>>('/api/notifications/read-all', {})
}

export function acknowledgeNotification(id: string): Promise<void> {
  return apiClient.postJson<void, Record<string, never>>(`/api/notifications/${id}/acknowledge`, {})
}

export function archiveNotification(id: string): Promise<void> {
  return apiClient.postJson<void, Record<string, never>>(`/api/notifications/${id}/archive`, {})
}

export function getNotificationPreferences(): Promise<NotificationPreference[]> {
  return apiClient.getJson<NotificationPreference[]>('/api/notifications/preferences')
}

export function setNotificationPreference(category: NotificationCategory, enabled: boolean): Promise<void> {
  return apiClient.putJson<void, { category: NotificationCategory; enabled: boolean }>(
    '/api/notifications/preferences',
    { category, enabled },
  )
}
