import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import type {
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

export function deleteTransportOrder(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/transport-orders/${id}`)
}
