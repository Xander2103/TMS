import { apiClient } from '../../../api/apiClient'

export interface Notification {
  id: string
  type: string
  title: string
  message: string
  linkPath: string | null
  isRead: boolean
  createdAt: string
}

export function listNotifications(unreadOnly = false, take = 50): Promise<Notification[]> {
  return apiClient.getJson<Notification[]>(`/api/notifications?unreadOnly=${unreadOnly}&take=${take}`)
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
