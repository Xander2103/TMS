import { apiClient } from './apiClient'
import type { CreateTransportOrderInput, TransportOrder } from '../features/transport-orders/types/transportOrder'

export function getTransportOrders(): Promise<TransportOrder[]> {
  return apiClient.getJson<TransportOrder[]>('/api/transportorders')
}

export function createTransportOrder(input: CreateTransportOrderInput): Promise<TransportOrder> {
  return apiClient.postJson<TransportOrder, CreateTransportOrderInput>('/api/transportorders', input)
}
