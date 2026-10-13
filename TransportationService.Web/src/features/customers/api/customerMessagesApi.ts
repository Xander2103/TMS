import { apiClient } from '../../../api/apiClient'

/** Mirrors CustomerMessageDto (camelCase JSON) — shared shape for the portal and internal sides. */
export interface CustomerMessage {
  id: string
  transportOrderId: string | null
  orderNumber: string | null
  authorIsStaff: boolean
  authorName: string
  body: string
  createdAt: string
}

export function listCustomerMessages(customerId: string, orderId?: string): Promise<CustomerMessage[]> {
  const query = orderId ? `?orderId=${orderId}` : ''
  return apiClient.getJson<CustomerMessage[]>(`/api/customers/${customerId}/messages${query}`)
}

export function sendCustomerMessage(customerId: string, orderId: string | null, body: string): Promise<CustomerMessage> {
  return apiClient.postJson<CustomerMessage, { orderId: string | null; body: string }>(
    `/api/customers/${customerId}/messages`,
    { orderId, body },
  )
}

export function markCustomerMessagesRead(customerId: string, orderId: string | null): Promise<void> {
  return apiClient.postJson<void, { orderId: string | null }>(`/api/customers/${customerId}/messages/read`, { orderId })
}

export function getCustomerMessagesUnreadCount(customerId: string): Promise<{ count: number }> {
  return apiClient.getJson<{ count: number }>(`/api/customers/${customerId}/messages/unread-count`)
}
