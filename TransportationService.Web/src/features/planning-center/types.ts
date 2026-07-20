import type { OrderPriority, TransportOrderStatus } from '../transport-orders/types'
import type { PlanningConflict, TripStatus } from '../planning/types'

export interface PlanningBoardTrip {
  id: string
  tripNumber: string
  tripDate: string
  status: TripStatus
  plannedStart: string | null
  plannedEnd: string | null
  driverId: string | null
  driverName: string | null
  vehicleId: string | null
  vehicleNumber: string | null
  trailerId: string | null
  trailerNumber: string | null
  orderCount: number
  stopCount: number
  routeSummary: string | null
  totalWeightKg: number | null
  totalVolumeM3: number | null
  capacityWeightKg: number | null
  capacityVolumeM3: number | null
  blockingConflictCount: number
  warningConflictCount: number
  version: string
}

export interface PlanningBoard {
  from: string
  to: string
  trips: PlanningBoardTrip[]
}

export interface UnplannedOrder {
  id: string
  orderNumber: string
  orderDate: string
  customerId: string
  customerName: string
  status: TransportOrderStatus
  priority: OrderPriority
  firstLoadingCity: string | null
  lastUnloadingCity: string | null
  requestedFrom: string | null
  requestedTo: string | null
  goodsDescription: string | null
  stopCount: number
  cargoLineCount: number
  packageCount: number
  totalWeightKg: number | null
  totalVolumeM3: number | null
  adrRequired: boolean
  craneRequired: boolean
  attentionBadges: string[]
}

export const ATTENTION_BADGE_LABELS: Record<string, string> = {
  SubmittedReview: 'Te beoordelen',
  NoStops: 'Geen stops',
  MissingWeight: 'Gewicht ontbreekt',
  MissingVolume: 'Volume ontbreekt',
  AppointmentRequired: 'Afspraak vereist',
}

export interface ResourceAssignment {
  date: string
  tripId: string
  tripNumber: string
  status: TripStatus
}

export interface ResourceAbsence {
  startDate: string
  endDate: string
  type: string
}

export interface PlanningDriver {
  id: string
  driverNumber: string
  name: string
  availabilityStatus: string
  isActive: boolean
  isBlocked: boolean
  blockReason: string | null
  fixedVehicleId: string | null
  fixedVehicleNumber: string | null
  assignments: ResourceAssignment[]
  absences: ResourceAbsence[]
  qualificationBlocks: string[]
  qualificationWarnings: string[]
}

export interface PlanningVehicle {
  id: string
  internalNumber: string
  licensePlate: string
  operationalStatus: string
  isActive: boolean
  payloadKg: number | null
  volumeM3: number | null
  hasCrane: boolean
  hasTailLift: boolean
  hasRefrigeration: boolean
  adrSuitable: boolean
  fixedDriverId: string | null
  fixedDriverName: string | null
  currentDriverId: string | null
  currentDriverName: string | null
  overdueMaintenanceCount: number
  overdueInspectionCount: number
  assignments: ResourceAssignment[]
}

export interface PlanningTrailer {
  id: string
  internalNumber: string
  licensePlate: string
  operationalStatus: string
  isActive: boolean
  capacityKg: number | null
  volumeM3: number | null
  hasRefrigeration: boolean
  adrSuitable: boolean
  overdueMaintenanceCount: number
  overdueInspectionCount: number
  assignments: ResourceAssignment[]
}

export interface PlanningResources {
  from: string
  to: string
  drivers: PlanningDriver[]
  vehicles: PlanningVehicle[]
  trailers: PlanningTrailer[]
}

/** Drag payload carried through HTML5 drag-and-drop (serialized as JSON). */
export type DragPayload =
  | { kind: 'order'; id: string; label: string }
  | { kind: 'driver'; id: string; label: string }
  | { kind: 'vehicle'; id: string; label: string }
  | { kind: 'trailer'; id: string; label: string }
  | { kind: 'trip'; id: string; label: string; version: string }

export const DRAG_MIME = 'application/x-ts-plan'

export function encodeDragPayload(payload: DragPayload): string {
  return JSON.stringify(payload)
}

export function decodeDragPayload(raw: string): DragPayload | null {
  try {
    const parsed = JSON.parse(raw) as DragPayload
    if (!parsed || typeof parsed !== 'object') return null
    if (!('kind' in parsed) || !('id' in parsed)) return null
    if (!['order', 'driver', 'vehicle', 'trailer', 'trip'].includes(parsed.kind)) return null
    return parsed
  } catch {
    return null
  }
}

/** 409 body of a rejected planning mutation. */
export interface PlanningRejection {
  message?: string
  conflicts?: PlanningConflict[] | null
  staleVersion?: boolean
}
