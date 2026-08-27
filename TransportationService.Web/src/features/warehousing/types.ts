import type { OrderPriority } from '../transport-orders/types'

export interface Dock {
  id: string
  code: string
  name: string | null
  allowsLoading: boolean
  allowsUnloading: boolean
  allowsAdr: boolean
  refrigerated: boolean
  maxVehicleLengthM: number | null
  maxVehicleHeightM: number | null
  isActive: boolean
  notes: string | null
}

export interface Warehouse {
  id: string
  name: string
  locationId: string
  locationLabel: string
  isActive: boolean
  opensAt: string | null
  closesAt: string | null
  contactName: string | null
  contactPhone: string | null
  contactEmail: string | null
  notes: string | null
  docks: Dock[]
}

export interface WarehouseInput {
  name: string
  locationId: string
  isActive: boolean
  opensAt: string | null
  closesAt: string | null
  contactName: string | null
  contactPhone: string | null
  contactEmail: string | null
  notes: string | null
}

export interface DockInput {
  code: string
  name: string | null
  allowsLoading: boolean
  allowsUnloading: boolean
  allowsAdr: boolean
  refrigerated: boolean
  maxVehicleLengthM: number | null
  maxVehicleHeightM: number | null
  isActive: boolean
  notes: string | null
}

export type DockOperationType = 'Loading' | 'Unloading'

export type DockAppointmentStatus =
  | 'Planned'
  | 'Expected'
  | 'Arrived'
  | 'Waiting'
  | 'AssignedToDock'
  | 'InProgress'
  | 'Completed'
  | 'Cancelled'
  | 'NoShow'

/** Vertaalsleutels — renderen als t(DOCK_STATUS_LABELS[status]). */
export const DOCK_STATUS_LABELS: Record<DockAppointmentStatus, string> = {
  Planned: 'warehousing.dockStatus.Planned',
  Expected: 'warehousing.dockStatus.Expected',
  Arrived: 'warehousing.dockStatus.Arrived',
  Waiting: 'warehousing.dockStatus.Waiting',
  AssignedToDock: 'warehousing.dockStatus.AssignedToDock',
  InProgress: 'warehousing.dockStatus.InProgress',
  Completed: 'warehousing.dockStatus.Completed',
  Cancelled: 'warehousing.dockStatus.Cancelled',
  NoShow: 'warehousing.dockStatus.NoShow',
}

export const DOCK_STATUS_TONE: Record<DockAppointmentStatus, 'neutral' | 'info' | 'warning' | 'success' | 'danger'> = {
  Planned: 'neutral',
  Expected: 'info',
  Arrived: 'info',
  Waiting: 'warning',
  AssignedToDock: 'info',
  InProgress: 'warning',
  Completed: 'success',
  Cancelled: 'danger',
  NoShow: 'danger',
}

/** Vertaalsleutels — renderen als t(OPERATION_LABELS[type]). */
export const OPERATION_LABELS: Record<DockOperationType, string> = {
  Loading: 'warehousing.operation.Loading',
  Unloading: 'warehousing.operation.Unloading',
}

export interface DockConflict {
  code: string
  severity: 'Information' | 'Warning' | 'Blocking'
  description: string
  overrideAllowed: boolean
}

export interface DockAppointment {
  id: string
  warehouseId: string
  dockId: string | null
  dockCode: string | null
  operationType: DockOperationType
  status: DockAppointmentStatus
  plannedStart: string
  plannedEnd: string
  arrivedAt: string | null
  startedAt: string | null
  completedAt: string | null
  priority: OrderPriority
  tripId: string | null
  tripNumber: string | null
  transportOrderId: string | null
  orderNumber: string | null
  customerName: string | null
  vehicleId: string | null
  vehicleNumber: string | null
  trailerId: string | null
  trailerNumber: string | null
  driverId: string | null
  driverName: string | null
  reference: string | null
  remarks: string | null
  packageCount: number
  packagesHandled: number
  hasOpenDiscrepancies: boolean
  allowedTransitions: DockAppointmentStatus[]
  version: string
}

export interface DockAppointmentInput {
  warehouseId: string
  dockId: string | null
  operationType: DockOperationType
  plannedStart: string
  plannedEnd: string
  tripId?: string | null
  transportOrderId?: string | null
  vehicleId?: string | null
  trailerId?: string | null
  driverId?: string | null
  priority?: OrderPriority
  reference?: string | null
  remarks?: string | null
  version?: string | null
  override?: boolean
  overrideReason?: string | null
}

export interface DockBoard {
  warehouseId: string
  date: string
  opensAt: string | null
  closesAt: string | null
  docks: Dock[]
  appointments: DockAppointment[]
  queue: DockAppointment[]
}

export interface DockUtilization {
  dockId: string
  dockCode: string
  bookedMinutes: number
  openMinutes: number
  utilizationPct: number
}

export interface WarehouseDashboard {
  warehouseId: string
  date: string
  expectedToday: number
  waiting: number
  inProgress: number
  completed: number
  delayed: number
  noShows: number
  utilization: DockUtilization[]
}

/** Horizontal block position on the dock timeline, in % of the operating window. */
export function dockBlockPosition(
  plannedStart: string,
  plannedEnd: string,
  opensAt: string | null,
  closesAt: string | null,
): { leftPct: number; widthPct: number } {
  const open = parseMinutes(opensAt) ?? 6 * 60
  const close = parseMinutes(closesAt) ?? 20 * 60
  const window = Math.max(close - open, 60)
  const start = new Date(plannedStart)
  const end = new Date(plannedEnd)
  const startMinutes = clamp(start.getHours() * 60 + start.getMinutes() - open, 0, window)
  const endMinutes = clamp(end.getHours() * 60 + end.getMinutes() - open, startMinutes + 15, window)
  return {
    leftPct: (startMinutes / window) * 100,
    widthPct: Math.max(((endMinutes - startMinutes) / window) * 100, 2),
  }
}

function parseMinutes(time: string | null): number | null {
  if (!time) return null
  const [hours, minutes] = time.split(':').map(Number)
  return Number.isFinite(hours) && Number.isFinite(minutes) ? hours * 60 + minutes : null
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max)
}
