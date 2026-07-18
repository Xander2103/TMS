export type DriverAvailabilityStatus = 'Available' | 'Unavailable' | 'OnLeave' | 'OnTrip'

export interface DriverListItem {
  id: string
  driverNumber: string
  fullName: string
  employeeNumber: string
  categoryName: string | null
  availabilityStatus: DriverAvailabilityStatus
  isActive: boolean
  isBlocked: boolean
}

export interface DriverReadiness {
  status: 'Ready' | 'Warning' | 'NotReady' | 'Blocked'
  blockingReasons: string[]
  warnings: string[]
}

export interface DriverQualification {
  typeCode: string
  typeName: string
  status: string
  expiryDate: string | null
}

export interface DriverDetail {
  id: string
  driverNumber: string
  employeeId: string
  fullName: string
  employeeNumber: string
  categoryId: string | null
  categoryName: string | null
  availabilityStatus: DriverAvailabilityStatus
  isActive: boolean
  isBlocked: boolean
  blockReason: string | null
  fixedVehiclePreference: boolean
  defaultVehicleId: string | null
  preferredVehicleId: string | null
  defaultTrailerId: string | null
  notes: string | null
  readiness: DriverReadiness
  qualifications: DriverQualification[]
}

export interface CreateDriverInput {
  employeeId: string
  driverCategoryId: string | null
  availabilityStatus: DriverAvailabilityStatus
  fixedVehiclePreference: boolean
  defaultVehicleId: string | null
  preferredVehicleId: string | null
  defaultTrailerId: string | null
  notes: string | null
}

export interface UpdateDriverInput {
  driverCategoryId: string | null
  availabilityStatus: DriverAvailabilityStatus
  isActive: boolean
  fixedVehiclePreference: boolean
  defaultVehicleId: string | null
  preferredVehicleId: string | null
  defaultTrailerId: string | null
  notes: string | null
}

export const AVAILABILITY_LABELS: Record<DriverAvailabilityStatus, string> = {
  Available: 'Beschikbaar',
  Unavailable: 'Niet beschikbaar',
  OnLeave: 'Afwezig',
  OnTrip: 'Onderweg',
}
