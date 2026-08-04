import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import type {
  OrderPricingStatus,
  OrderPriority,
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

/** Inline priority edit; backend validates status and audits the change. */
export function changeOrderPriority(id: string, priority: OrderPriority): Promise<TransportOrderDetail> {
  return apiClient.postJson<TransportOrderDetail, { priority: OrderPriority }>(
    `/api/transport-orders/${id}/priority`, { priority })
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

/** Internal accept/reject/request-info decision on a customer-submitted (Submitted-status) order. */
export type PortalReviewAction = 'Accept' | 'Reject' | 'RequestInfo'

export function reviewPortalOrder(
  id: string,
  action: PortalReviewAction,
  reason: string | null,
): Promise<TransportOrderDetail> {
  return apiClient.postJson<TransportOrderDetail, { action: PortalReviewAction; reason: string | null }>(
    `/api/transport-orders/${id}/portal-review`,
    { action, reason },
  )
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

/**
 * One line-level manual correction/removal/free addition (spec ch. 24-26). LineKey null targets a
 * new free Manual line; otherwise targets an existing Auto/AutoAdjusted/Manual line by its stable
 * merge key. Remove keeps the row for audit (Auto/AutoAdjusted → Amount 0) except a Manual line,
 * which is hard-deleted.
 */
export interface SaveOrderPriceLineInput {
  lineKey: string | null
  label: string
  quantity: number | null
  unitPrice: number | null
  amount: number | null
  adjustReason: string | null
  remove?: boolean
  /** Managed unit code for quantity (e.g. "COLLI"); normalized like quantityUnitCode. */
  unit?: string | null
}

export function saveOrderPriceLines(orderId: string, lines: SaveOrderPriceLineInput[]): Promise<TransportOrderDetail> {
  return apiClient.putJson<TransportOrderDetail, SaveOrderPriceLineInput[]>(
    `/api/transport-orders/${orderId}/pricing/lines`, lines)
}

/** Explicit re-run of the pricing engine (merge-on-recalc); refused while Locked/Invoiced. */
export function recalculateOrderPricing(orderId: string): Promise<TransportOrderDetail> {
  return apiClient.postJson<TransportOrderDetail, Record<string, never>>(
    `/api/transport-orders/${orderId}/pricing/recalculate`, {})
}

/** Pricing status transition (Draft/Reviewed/Locked); Invoiced is set only by invoicing. */
export function setOrderPricingStatus(orderId: string, status: OrderPricingStatus): Promise<TransportOrderDetail> {
  return apiClient.postJson<TransportOrderDetail, { status: OrderPricingStatus }>(
    `/api/transport-orders/${orderId}/pricing/status`, { status })
}

/** Confirms an unconfirmed (VOORSTEL) extra-time line so it counts towards LinesTotal/AgreedPrice. */
export function confirmOrderPriceLine(orderId: string, lineId: string): Promise<TransportOrderDetail> {
  return apiClient.postJson<TransportOrderDetail, Record<string, never>>(
    `/api/transport-orders/${orderId}/pricing/lines/${lineId}/confirm`, {})
}

/**
 * Wave 2026-08-04 §8/§10: "Prijs bevestigen". The reason is only consulted — and then required —
 * when the coverage shows unpriced goods and the caller holds the override permission.
 */
export function confirmOrderPricing(orderId: string, unpricedGoodsReason?: string | null): Promise<TransportOrderDetail> {
  return apiClient.postJson<TransportOrderDetail, { unpricedGoodsReason: string | null }>(
    `/api/transport-orders/${orderId}/pricing/confirm`, { unpricedGoodsReason: unpricedGoodsReason ?? null })
}

/** Wave 2026-08-04 §8: "Prijs aanpassen" — reopens a confirmed price (reason required). */
export function reopenOrderPricing(orderId: string, reason: string): Promise<TransportOrderDetail> {
  return apiClient.postJson<TransportOrderDetail, { reason: string }>(
    `/api/transport-orders/${orderId}/pricing/reopen`, { reason })
}
