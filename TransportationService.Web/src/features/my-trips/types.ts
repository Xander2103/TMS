import type { TripStatus } from '../planning/types'
import type { StopType, TransportOrderStatus } from '../transport-orders/types'

export type StopExecutionStatus = 'Pending' | 'Arrived' | 'Completed' | 'Skipped'

export const STOP_EXECUTION_LABELS: Record<StopExecutionStatus, string> = {
  Pending: 'Te doen',
  Arrived: 'Aangekomen',
  Completed: 'Afgerond',
  Skipped: 'Overgeslagen',
}

export const STOP_EXECUTION_TONE: Record<StopExecutionStatus, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Pending: 'neutral',
  Arrived: 'info',
  Completed: 'success',
  Skipped: 'warning',
}

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
  instructions: string | null
  status: StopExecutionStatus
  arrivedAt: string | null
  completedAt: string | null
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

export type { TransportOrderStatus }
