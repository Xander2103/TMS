import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import type {
  StopExecutionPlanInput,
  TransportOrderDetail,
  TransportOrderInput,
  TransportOrderListItem,
  TransportOrderStatus,
} from '../types'

export interface SearchTransportOrdersParams {
  search?: string
  status?: TransportOrderStatus
  customerId?: string
  fromDate?: string
  toDate?: string
  page: number
  pageSize: number
}

export function searchTransportOrders(
  params: SearchTransportOrdersParams,
): Promise<PagedResult<TransportOrderListItem>> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.status) query.set('status', params.status)
  if (params.customerId) query.set('customerId', params.customerId)
  if (params.fromDate) query.set('fromDate', params.fromDate)
  if (params.toDate) query.set('toDate', params.toDate)
  query.set('page', String(params.page))
  query.set('pageSize', String(params.pageSize))
  return apiClient.getJson<PagedResult<TransportOrderListItem>>(`/api/transport-orders?${query.toString()}`)
}

export interface BulkStatusItemResult {
  orderId: string
  success: boolean
  error: string | null
}

export interface BulkStatusResult {
  succeededCount: number
  failedCount: number
  results: BulkStatusItemResult[]
}

export function bulkChangeOrderStatus(orderIds: string[], status: string): Promise<BulkStatusResult> {
  return apiClient.postJson<BulkStatusResult, { orderIds: string[]; status: string }>(
    '/api/transport-orders/bulk-status',
    { orderIds, status },
  )
}

export function getTransportOrder(id: string): Promise<TransportOrderDetail> {
  return apiClient.getJson<TransportOrderDetail>(`/api/transport-orders/${id}`)
}

export function createTransportOrder(input: TransportOrderInput): Promise<TransportOrderDetail> {
  return apiClient.postJson<TransportOrderDetail, TransportOrderInput>('/api/transport-orders', input)
}

export function updateTransportOrder(id: string, input: TransportOrderInput): Promise<TransportOrderDetail> {
  return apiClient.putJson<TransportOrderDetail, TransportOrderInput>(`/api/transport-orders/${id}`, input)
}

export function changeTransportOrderStatus(
  id: string,
  status: TransportOrderStatus,
): Promise<TransportOrderDetail> {
  return apiClient.postJson<TransportOrderDetail, { status: TransportOrderStatus }>(
    `/api/transport-orders/${id}/status`,
    { status },
  )
}

export function updateStopExecutionPlan(
  orderId: string,
  stopId: string,
  input: StopExecutionPlanInput,
): Promise<TransportOrderDetail> {
  return apiClient.putJson<TransportOrderDetail, StopExecutionPlanInput>(
    `/api/transport-orders/${orderId}/stops/${stopId}/execution-plan`,
    input,
  )
}

export function cancelTransportOrder(id: string, reason: string): Promise<TransportOrderDetail> {
  return apiClient.postJson<TransportOrderDetail, { reason: string }>(`/api/transport-orders/${id}/cancel`, { reason })
}

export function correctTransportOrderStatus(
  id: string,
  targetStatus: string,
  reason: string,
): Promise<TransportOrderDetail> {
  return apiClient.postJson<TransportOrderDetail, { targetStatus: string; reason: string }>(
    `/api/transport-orders/${id}/correct-status`,
    { targetStatus, reason },
  )
}

export function deleteTransportOrder(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/transport-orders/${id}`)
}

export interface OrderTimelineEvent {
  timestamp: string
  category: 'order' | 'status' | 'package' | 'stop' | 'invoice' | string
  title: string
  detail: string | null
  userName: string | null
}

export function getTransportOrderTimeline(id: string): Promise<OrderTimelineEvent[]> {
  return apiClient.getJson<OrderTimelineEvent[]>(`/api/transport-orders/${id}/timeline`)
}
