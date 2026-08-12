import { apiClient } from '../../../api/apiClient'
import type {
  DockAppointment, DockAppointmentInput, DockAppointmentStatus, DockBoard, DockInput,
  Warehouse, WarehouseDashboard, WarehouseInput,
} from '../types'

export function listWarehouses(): Promise<Warehouse[]> {
  return apiClient.getJson<Warehouse[]>('/api/warehouses')
}

export function createWarehouse(input: WarehouseInput): Promise<Warehouse> {
  return apiClient.postJson<Warehouse, WarehouseInput>('/api/warehouses', input)
}

export function updateWarehouse(id: string, input: WarehouseInput): Promise<Warehouse> {
  return apiClient.putJson<Warehouse, WarehouseInput>(`/api/warehouses/${id}`, input)
}

export function createDock(warehouseId: string, input: DockInput): Promise<Warehouse> {
  return apiClient.postJson<Warehouse, DockInput>(`/api/warehouses/${warehouseId}/docks`, input)
}

export function updateDock(warehouseId: string, dockId: string, input: DockInput): Promise<Warehouse> {
  return apiClient.putJson<Warehouse, DockInput>(`/api/warehouses/${warehouseId}/docks/${dockId}`, input)
}

export function deleteDock(warehouseId: string, dockId: string): Promise<void> {
  return apiClient.deleteRequest(`/api/warehouses/${warehouseId}/docks/${dockId}`)
}

// --- Wave 4 §1: warehouse storage locations (zone → position) ---

export interface WarehouseLocation {
  id: string
  warehouseId: string
  parentId: string | null
  code: string
  name: string
  kind: 'Zone' | 'Position'
  isActive: boolean
  sortOrder: number
  /** Colli momenteel op deze locatie (projectie). */
  packageCount: number
}

export interface WarehouseLocationInput {
  code: string
  name: string
  parentId?: string | null
  kind?: 'Zone' | 'Position'
  isActive?: boolean
  sortOrder?: number
}

export function listWarehouseLocations(warehouseId: string): Promise<WarehouseLocation[]> {
  return apiClient.getJson(`/api/warehouses/${warehouseId}/locations`)
}

export function createWarehouseLocation(warehouseId: string, input: WarehouseLocationInput): Promise<WarehouseLocation> {
  return apiClient.postJson(`/api/warehouses/${warehouseId}/locations`, input)
}

export function updateWarehouseLocation(
  warehouseId: string, locationId: string, input: WarehouseLocationInput,
): Promise<WarehouseLocation> {
  return apiClient.putJson(`/api/warehouses/${warehouseId}/locations/${locationId}`, input)
}

export function deleteWarehouseLocation(warehouseId: string, locationId: string): Promise<void> {
  return apiClient.deleteRequest(`/api/warehouses/${warehouseId}/locations/${locationId}`)
}

// --- Wave 4 §3+§4: standalone scans + trace/overview ---

export type WarehouseScanType = 'Received' | 'Moved' | 'Staged' | 'Return'

export interface WarehouseScanFeedback {
  outcome: string
  level: 'Success' | 'Warning' | 'Error'
  message: string
  packageId: string | null
  packageNumber: string | null
  orderNumber: string | null
  lifecycleStatus: string | null
  warehouseLocationId: string | null
  locationCode: string | null
}

export function submitWarehouseScan(input: {
  barcode: string
  scanType: WarehouseScanType
  warehouseLocationId?: string | null
  clientEventId?: string
}): Promise<WarehouseScanFeedback> {
  return apiClient.postJson('/api/warehouse/scans', input)
}

export interface WarehouseTraceEvent {
  eventType: string
  occurredAt: string
  locationCode: string | null
  tripNumber: string | null
  result: string | null
}

export interface WarehouseTrace {
  packageId: string
  packageNumber: string
  description: string | null
  lifecycleStatus: string
  exceptionStatus: string
  orderNumber: string
  customerName: string
  warehouseLocationId: string | null
  warehouseName: string | null
  locationCode: string | null
  locationName: string | null
  lastEvents: WarehouseTraceEvent[]
}

export function traceWarehousePackage(barcode: string): Promise<WarehouseTrace> {
  return apiClient.getJson(`/api/warehouse/trace?barcode=${encodeURIComponent(barcode)}`)
}

export interface WarehouseOverviewPackage {
  packageId: string
  packageNumber: string
  orderNumber: string
  lifecycleStatus: string
  locationCode: string | null
}

export interface WarehouseOverview {
  warehouseId: string
  warehouseName: string
  locations: { locationId: string; code: string; name: string; kind: string; packages: WarehouseOverviewPackage[] }[]
  shouldHaveLeftToday: WarehouseOverviewPackage[]
  waitsForTomorrow: WarehouseOverviewPackage[]
}

export function getWarehouseOverview(warehouseId: string): Promise<WarehouseOverview> {
  return apiClient.getJson(`/api/warehouses/${warehouseId}/overview`)
}

export function getDockBoard(warehouseId: string, date: string): Promise<DockBoard> {
  return apiClient.getJson<DockBoard>(`/api/dock-appointments/board?warehouseId=${warehouseId}&date=${date}`)
}

export function getWarehouseDashboard(warehouseId: string, date: string): Promise<WarehouseDashboard> {
  return apiClient.getJson<WarehouseDashboard>(`/api/dock-appointments/dashboard?warehouseId=${warehouseId}&date=${date}`)
}

export function createDockAppointment(input: DockAppointmentInput): Promise<DockAppointment> {
  return apiClient.postJson<DockAppointment, DockAppointmentInput>('/api/dock-appointments', input)
}

export function updateDockAppointment(id: string, input: DockAppointmentInput): Promise<DockAppointment> {
  return apiClient.putJson<DockAppointment, DockAppointmentInput>(`/api/dock-appointments/${id}`, input)
}

export function changeDockAppointmentStatus(
  id: string, status: DockAppointmentStatus, version: string,
): Promise<DockAppointment> {
  return apiClient.postJson<DockAppointment, { status: DockAppointmentStatus; version: string }>(
    `/api/dock-appointments/${id}/status`, { status, version })
}

export function deleteDockAppointment(id: string, version: string): Promise<void> {
  return apiClient.deleteRequest(`/api/dock-appointments/${id}?version=${version}`)
}
