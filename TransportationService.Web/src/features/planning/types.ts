import type { TransportOrderStatus } from '../transport-orders/types'

export type TripStatus = 'Draft' | 'Planned' | 'InProgress' | 'Completed' | 'Cancelled'

/** Vertaalsleutels (i18n-wave): render altijd via t(TRIP_STATUS_LABELS[status]). */
export const TRIP_STATUS_LABELS: Record<TripStatus, string> = {
  Draft: 'planning.tripStatus.Draft',
  Planned: 'planning.tripStatus.Planned',
  InProgress: 'planning.tripStatus.InProgress',
  Completed: 'planning.tripStatus.Completed',
  Cancelled: 'planning.tripStatus.Cancelled',
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
  Draft: 'planning.tripTransition.Draft',
  Planned: 'planning.tripTransition.Planned',
  InProgress: 'planning.tripTransition.InProgress',
  Completed: 'planning.tripTransition.Completed',
  Cancelled: 'planning.tripTransition.Cancelled',
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
  Blocking: { label: 'planning.conflictSeverity.Blocking', tone: 'danger' },
  Warning: { label: 'planning.conflictSeverity.Warning', tone: 'warning' },
  Information: { label: 'planning.conflictSeverity.Information', tone: 'info' },
}

export type ConflictCategory =
  | 'Resource'
  | 'Availability'
  | 'Qualification'
  | 'Capacity'
  | 'Timing'
  | 'Equipment'
  | 'Document'
  | 'Data'

export const CONFLICT_CATEGORY_LABELS: Record<ConflictCategory, string> = {
  Resource: 'planning.conflictCategory.Resource',
  Availability: 'planning.conflictCategory.Availability',
  Qualification: 'planning.conflictCategory.Qualification',
  Capacity: 'planning.conflictCategory.Capacity',
  Timing: 'planning.conflictCategory.Timing',
  Equipment: 'planning.conflictCategory.Equipment',
  Document: 'planning.conflictCategory.Document',
  Data: 'planning.conflictCategory.Data',
}

export interface PlanningConflict {
  code: PlanningConflictCode
  blocking: boolean
  description: string
  severity: PlanningConflictSeverity
  category: ConflictCategory
  relatedEntityType: string | null
  relatedEntityId: string | null
  /** Only blocking conflicts can be overridden, with the named permission and a reason. */
  overrideAllowed: boolean
  requiredPermission: string | null
  suggestedAction: string | null
}

export interface ConflictOverrideEntry {
  id: string
  conflictCodes: string
  reason: string
  actorUserId: string | null
  actorName: string | null
  occurredAt: string
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
  /** Optimistic-concurrency token; echo it on mutations so parallel edits surface as 409. */
  version: string
  overrides: ConflictOverrideEntry[]
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
  /** Version loaded with the trip; omit only when creating. */
  version?: string
}
