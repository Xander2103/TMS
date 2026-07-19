import type { TripStatus } from '../planning/types'
import type { StopType, TransportOrderStatus } from '../transport-orders/types'

export type StopExecutionStatus =
  | 'Planned'
  | 'EnRoute'
  | 'Arrived'
  | 'Loading'
  | 'Loaded'
  | 'Unloading'
  | 'Completed'
  | 'PartiallyCompleted'
  | 'Failed'
  | 'Skipped'

export const STOP_EXECUTION_LABELS: Record<StopExecutionStatus, string> = {
  Planned: 'Gepland',
  EnRoute: 'Onderweg',
  Arrived: 'Aangekomen',
  Loading: 'Aan het laden',
  Loaded: 'Geladen',
  Unloading: 'Aan het lossen',
  Completed: 'Afgerond',
  PartiallyCompleted: 'Deels afgerond',
  Failed: 'Mislukt',
  Skipped: 'Overgeslagen',
}

export const STOP_EXECUTION_TONE: Record<StopExecutionStatus, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Planned: 'neutral',
  EnRoute: 'info',
  Arrived: 'info',
  Loading: 'warning',
  Loaded: 'info',
  Unloading: 'warning',
  Completed: 'success',
  PartiallyCompleted: 'warning',
  Failed: 'danger',
  Skipped: 'warning',
}

/** Non-colour signal per status (accessibility: colour is never the only indicator). */
export const STOP_EXECUTION_ICONS: Record<StopExecutionStatus, string> = {
  Planned: '○',
  EnRoute: '»',
  Arrived: '◉',
  Loading: '▲',
  Loaded: '■',
  Unloading: '▼',
  Completed: '✓',
  PartiallyCompleted: '◐',
  Failed: '✕',
  Skipped: '↷',
}

/** Action verb on the transition buttons in the driver workflow. */
export const STOP_TRANSITION_ACTION_LABELS: Record<StopExecutionStatus, string> = {
  Planned: 'Gepland',
  EnRoute: 'Vertrokken naar stop',
  Arrived: 'Aangekomen',
  Loading: 'Start laden',
  Loaded: 'Laden klaar',
  Unloading: 'Start lossen',
  Completed: 'Afronden',
  PartiallyCompleted: 'Deels afgerond',
  Failed: 'Mislukt',
  Skipped: 'Overslaan',
}

/** The natural forward chain; the first allowed transition in this order is the primary action. */
export const STOP_PRIMARY_ACTION_ORDER: StopExecutionStatus[] = [
  'EnRoute',
  'Arrived',
  'Loading',
  'Unloading',
  'Loaded',
  'Completed',
]

/** Transitions that always demand a reason. */
export const STOP_REASON_REQUIRED: StopExecutionStatus[] = ['Skipped', 'Failed', 'PartiallyCompleted']

export interface MyTrip {
  id: string
  tripNumber: string
  tripDate: string
  status: TripStatus
  vehicleNumber: string | null
  vehicleLicensePlate: string | null
  trailerNumber: string | null
  orderCount: number
  stopCount: number
  completedStopCount: number
}

export interface ExecutionStop {
  transportOrderStopId: string
  transportOrderId: string
  orderNumber: string
  customerName: string
  orderSequence: number
  stopSequence: number
  stopType: StopType
  locationName: string
  address: string | null
  postalCode: string | null
  city: string | null
  plannedFrom: string | null
  plannedTo: string | null
  requestedFrom: string | null
  requestedTo: string | null
  confirmedFrom: string | null
  confirmedTo: string | null
  earliestAllowed: string | null
  latestAllowed: string | null
  appointmentRequired: boolean
  appointmentReference: string | null
  instructions: string | null
  accessInstructions: string | null
  loadingInstructions: string | null
  unloadingInstructions: string | null
  status: StopExecutionStatus
  arrivedAt: string | null
  departedAt: string | null
  completedAt: string | null
  waitingMinutes: number | null
  lateArrivalReason: string | null
  statusReason: string | null
  allowedTransitions: StopExecutionStatus[]
  hasPod: boolean
  podSignedBy: string | null
  remarks: string | null
}

export interface TripExecution {
  tripId: string
  tripNumber: string
  tripDate: string
  tripStatus: TripStatus
  driverName: string | null
  vehicleNumber: string | null
  vehicleLicensePlate: string | null
  stops: ExecutionStop[]
  completedCount: number
  totalCount: number
}

export interface StopStatusHistoryEntry {
  fromStatus: StopExecutionStatus
  toStatus: StopExecutionStatus
  occurredAt: string
  userName: string | null
  reason: string | null
}

/** The effective latest arrival bound the backend enforces (latestAllowed ?? confirmedTo ?? plannedTo). */
export function effectiveLatestBound(stop: ExecutionStop): string | null {
  return stop.latestAllowed ?? stop.confirmedTo ?? stop.plannedTo
}

export type { TransportOrderStatus }
