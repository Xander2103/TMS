import { apiClient } from '../../../api/apiClient'
import type { PackageUnitType } from '../../packages/types'
import type { StopType, TransportOrderStatus } from '../../transport-orders/types'

export interface PortalContext {
  customerId: string
  customerName: string
}

export interface PortalOrderListItem {
  id: string
  orderNumber: string
  orderDate: string
  status: TransportOrderStatus
  customerReference: string | null
  goodsDescription: string | null
  firstLoadingCity: string | null
  lastUnloadingCity: string | null
}

export interface PortalStop {
  sequence: number
  stopType: StopType
  locationName: string
  city: string | null
  requestedFrom: string | null
  requestedTo: string | null
  reference: string | null
  instructions: string | null
}

export interface PortalCargo {
  sequence: number
  description: string
  expectedQuantity: number
  quantityUnit: string | null
  unitType: PackageUnitType | null
  adrRequired: boolean
}

export interface PortalOrderDetail {
  id: string
  orderNumber: string
  orderDate: string
  status: TransportOrderStatus
  customerReference: string | null
  goodsDescription: string | null
  notes: string | null
  cancellationReason: string | null
  stops: PortalStop[]
  cargoItems: PortalCargo[]
}

export interface PortalStopInput {
  stopType: StopType
  locationId: string | null
  locationName: string | null
  address: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  requestedFrom: string | null
  requestedTo: string | null
  reference: string | null
  instructions: string | null
}

export interface PortalCargoInput {
  description: string
  expectedQuantity: number
  quantityUnit: string | null
  unitType: PackageUnitType | null
  totalWeightKg: number | null
  adrRequired: boolean
  adrDetails: string | null
}

export interface PortalCreateOrderInput {
  customerReference: string | null
  orderDate: string | null
  goodsDescription: string | null
  remarks: string | null
  stops: PortalStopInput[]
  cargoItems: PortalCargoInput[]
}

export interface PortalLocation {
  id: string
  name: string
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  isDefaultLoadingLocation: boolean
  isDefaultUnloadingLocation: boolean
}

export function getPortalContext(): Promise<PortalContext> {
  return apiClient.getJson<PortalContext>('/api/customer-portal/context')
}

export function listPortalOrders(): Promise<PortalOrderListItem[]> {
  return apiClient.getJson<PortalOrderListItem[]>('/api/customer-portal/orders')
}

export function getPortalOrder(id: string): Promise<PortalOrderDetail> {
  return apiClient.getJson<PortalOrderDetail>(`/api/customer-portal/orders/${id}`)
}

export function submitPortalOrder(input: PortalCreateOrderInput): Promise<PortalOrderDetail> {
  return apiClient.postJson<PortalOrderDetail, PortalCreateOrderInput>('/api/customer-portal/orders', input)
}

export function listPortalLocations(): Promise<PortalLocation[]> {
  return apiClient.getJson<PortalLocation[]>('/api/customer-portal/locations')
}

export function createPortalLocation(input: {
  name: string
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
}): Promise<PortalLocation> {
  return apiClient.postJson<PortalLocation, typeof input>('/api/customer-portal/locations', input)
}
