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
