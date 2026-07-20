import type { TransportOrderStatus } from '../transport-orders/types'

export type TripStatus = 'Draft' | 'Planned' | 'InProgress' | 'Completed' | 'Cancelled'

export const TRIP_STATUS_LABELS: Record<TripStatus, string> = {
  Draft: 'Concept',
  Planned: 'Gepland',
  InProgress: 'Onderweg',
  Completed: 'Afgerond',
  Cancelled: 'Geannuleerd',
}

export const TRIP_STATUSES: TripStatus[] = ['Draft', 'Planned', 'InProgress', 'Completed', 'Cancelled']

export const TRIP_STATUS_TONE: Record<TripStatus, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Draft: 'neutral',
  Planned: 'info',
  InProgress: 'warning',
  Completed: 'success',
  Cancelled: 'danger',
}

export const TRIP_TRANSITION_LABELS: Record<TripStatus, string> = {
  Draft: 'Terug naar concept',
  Planned: 'Inplannen',
  InProgress: 'Start rit',
  Completed: 'Afronden',
  Cancelled: 'Annuleren',
}

export type PlanningConflictCode =
  | 'DriverAbsent'
  | 'DriverBlocked'
  | 'DriverInactive'
  | 'DriverNotReady'
  | 'DriverDoubleBooked'
  | 'DriverShiftOverlap'
  | 'DriverTraining'
  | 'VehicleNotOperational'
  | 'VehicleInactive'
  | 'VehicleDoubleBooked'
  | 'TrailerNotOperational'
  | 'TrailerInactive'
  | 'TrailerDoubleBooked'
  | 'OrderRequiresCrane'
  | 'OrderRequiresAdr'
  | 'MissingDriver'
  | 'MissingVehicle'
  | 'NoOrders'
  | 'CapacityExceeded'
  | 'CapacityCheckIncomplete'

export type PlanningConflictSeverity = 'Information' | 'Warning' | 'Blocking'

export const CONFLICT_SEVERITY_META: Record<
  PlanningConflictSeverity,
  { label: string; tone: 'danger' | 'warning' | 'info' }
> = {
  Blocking: { label: 'Blokkerend', tone: 'danger' },
  Warning: { label: 'Waarschuwing', tone: 'warning' },
  Information: { label: 'Ter info', tone: 'info' },
}

export interface PlanningConflict {
  code: PlanningConflictCode
  blocking: boolean
  description: string
  severity: PlanningConflictSeverity
}

export interface TripOrderSummary {
  transportOrderId: string
  sequence: number
  orderNumber: string
  customerName: string
  orderStatus: TransportOrderStatus
  goodsDescription: string
  firstLoadingCity: string | null
  lastUnloadingCity: string | null
  adrRequired: boolean
  craneRequired: boolean
}

export interface TripListItem {
  id: string
  tripNumber: string
  tripDate: string
  status: TripStatus
  driverId: string | null
  driverName: string | null
  vehicleId: string | null
  vehicleNumber: string | null
  vehicleLicensePlate: string | null
  trailerId: string | null
  trailerNumber: string | null
  orderCount: number
  blockingConflictCount: number
}

export interface TripDetail {
  id: string
  tripNumber: string
  tripDate: string
  status: TripStatus
  driverId: string | null
  driverName: string | null
  vehicleId: string | null
  vehicleNumber: string | null
  vehicleLicensePlate: string | null
  trailerId: string | null
  trailerNumber: string | null
  plannedStart: string | null
  plannedEnd: string | null
  plannedDistanceKm: number | null
  plannedEmptyKm: number | null
  actualDistanceKm: number | null
  actualEmptyKm: number | null
  notes: string | null
  orders: TripOrderSummary[]
  conflicts: PlanningConflict[]
  allowedTransitions: TripStatus[]
}

export interface TripInput {
  tripDate: string
  driverId: string | null
  vehicleId: string | null
  trailerId: string | null
  plannedStart: string | null
  plannedEnd: string | null
  notes: string | null
  orderIds: string[]
  plannedDistanceKm: number | null
  plannedEmptyKm: number | null
}
