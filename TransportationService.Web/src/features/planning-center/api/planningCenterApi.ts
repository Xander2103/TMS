import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import type { TripDetail, TripStatus } from '../../planning/types'
import type { OrderPriority, TransportOrderStatus } from '../../transport-orders/types'
import type { PlanningBoard, PlanningResources, UnplannedOrder } from '../types'

export function getBoard(from: string, to: string): Promise<PlanningBoard> {
  return apiClient.getJson<PlanningBoard>(`/api/planning-board?from=${from}&to=${to}`)
}

export interface UnplannedOrdersParams {
  search?: string
  customerId?: string
  status?: TransportOrderStatus
  priority?: OrderPriority
  fromDate?: string
  toDate?: string
  onlyWithAttention?: boolean
  page?: number
  pageSize?: number
}

export function getUnplannedOrders(params: UnplannedOrdersParams): Promise<PagedResult<UnplannedOrder>> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.customerId) query.set('customerId', params.customerId)
  if (params.status) query.set('status', params.status)
  if (params.priority) query.set('priority', params.priority)
  if (params.fromDate) query.set('fromDate', params.fromDate)
  if (params.toDate) query.set('toDate', params.toDate)
  if (params.onlyWithAttention) query.set('onlyWithAttention', 'true')
  query.set('page', String(params.page ?? 1))
  query.set('pageSize', String(params.pageSize ?? 50))
  return apiClient.getJson<PagedResult<UnplannedOrder>>(`/api/planning-board/unplanned-orders?${query}`)
}

export function getResources(from: string, to: string): Promise<PlanningResources> {
  return apiClient.getJson<PlanningResources>(`/api/planning-board/resources?from=${from}&to=${to}`)
}

// --- Targeted commands: every drag-and-drop action persists through one of these ---

export function assignOrders(tripId: string, orderIds: string[], version: string): Promise<TripDetail> {
  return apiClient.postJson<TripDetail, { orderIds: string[]; version: string }>(
    `/api/trips/${tripId}/orders`, { orderIds, version })
}

export function removeOrder(tripId: string, orderId: string, version: string): Promise<TripDetail> {
  return apiClient.deleteJson<TripDetail>(`/api/trips/${tripId}/orders/${orderId}?version=${version}`)
}

export function reorderOrders(tripId: string, orderIds: string[], version: string): Promise<TripDetail> {
  return apiClient.postJson<TripDetail, { orderIds: string[]; version: string }>(
    `/api/trips/${tripId}/orders/reorder`, { orderIds, version })
}

interface AssignResourceBody {
  resourceId: string | null
  version: string
  override: boolean
  overrideReason: string | null
}

function assignResource(
  tripId: string,
  slot: 'driver' | 'vehicle' | 'trailer',
  resourceId: string | null,
  version: string,
  override = false,
  overrideReason: string | null = null,
): Promise<TripDetail> {
  return apiClient.putJson<TripDetail, AssignResourceBody>(
    `/api/trips/${tripId}/${slot}`, { resourceId, version, override, overrideReason })
}

export const assignDriver = (tripId: string, id: string | null, version: string, override = false, reason: string | null = null) =>
  assignResource(tripId, 'driver', id, version, override, reason)
export const assignVehicle = (tripId: string, id: string | null, version: string, override = false, reason: string | null = null) =>
  assignResource(tripId, 'vehicle', id, version, override, reason)
export const assignTrailer = (tripId: string, id: string | null, version: string, override = false, reason: string | null = null) =>
  assignResource(tripId, 'trailer', id, version, override, reason)

export function rescheduleTrip(
  tripId: string,
  tripDate: string,
  plannedStart: string | null,
  plannedEnd: string | null,
  version: string,
  override = false,
  overrideReason: string | null = null,
): Promise<TripDetail> {
  return apiClient.postJson<TripDetail, {
    tripDate: string
    plannedStart: string | null
    plannedEnd: string | null
    version: string
    override: boolean
    overrideReason: string | null
  }>(`/api/trips/${tripId}/reschedule`, { tripDate, plannedStart, plannedEnd, version, override, overrideReason })
}

export function createTripFromOrders(tripDate: string, orderIds: string[]): Promise<TripDetail> {
  return apiClient.postJson<TripDetail, {
    tripDate: string
    driverId: null
    vehicleId: null
    trailerId: null
    plannedStart: null
    plannedEnd: null
    notes: null
    orderIds: string[]
  }>('/api/trips', {
    tripDate, driverId: null, vehicleId: null, trailerId: null,
    plannedStart: null, plannedEnd: null, notes: null, orderIds,
  })
}

export function changeTripStatusVersioned(
  tripId: string,
  status: TripStatus,
  version: string,
  override = false,
  overrideReason: string | null = null,
): Promise<TripDetail> {
  return apiClient.postJson<TripDetail, {
    status: TripStatus
    override: boolean
    releaseOverride: boolean
    overrideReason: string | null
    version: string
  }>(`/api/trips/${tripId}/status`, { status, override, releaseOverride: false, overrideReason, version })
}
