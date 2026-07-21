export type TrailerOwnershipType = 'Owned' | 'Rented' | 'Leased'
export type TrailerOperationalStatus = 'Available' | 'InUse' | 'InMaintenance' | 'OutOfService'

export const TRAILER_OWNERSHIP_LABELS: Record<TrailerOwnershipType, string> = {
  Owned: 'Eigendom',
  Rented: 'Huur',
  Leased: 'Lease',
}

// Deliberately no label "Actief" here — see vehicles/types.ts.
export const TRAILER_STATUS_LABELS: Record<TrailerOperationalStatus, string> = {
  Available: 'Beschikbaar',
  InUse: 'In gebruik',
  InMaintenance: 'In onderhoud',
  OutOfService: 'Buiten dienst',
}

export const TRAILER_STATUS_TONES: Record<TrailerOperationalStatus, 'success' | 'info' | 'warning' | 'danger'> = {
  Available: 'success',
  InUse: 'info',
  InMaintenance: 'warning',
  OutOfService: 'danger',
}

export interface TrailerListItem {
  id: string
  internalNumber: string
  licensePlate: string
  brand: string | null
  model: string | null
  categoryName: string | null
  operationalStatus: TrailerOperationalStatus
  isActive: boolean
}

export interface TrailerOption {
  id: string
  internalNumber: string
  licensePlate: string
  brand: string | null
  model: string | null
}

export interface TrailerDetail {
  id: string
  internalNumber: string
  licensePlate: string
  vin: string | null
  categoryId: string | null
  categoryName: string | null
  brand: string | null
  model: string | null
  year: number | null
  firstRegistrationDate: string | null
  capacityKg: number | null
  lengthMeters: number | null
  widthMeters: number | null
  heightMeters: number | null
  volumeM3: number | null
  volumeIsManual: boolean
  axleCount: number
  loadingMeters: number
  hasRefrigeration: boolean
  adrSuitable: boolean
  ownershipType: TrailerOwnershipType
  operationalStatus: TrailerOperationalStatus
  statusReason: string | null
  isActive: boolean
  notes: string | null
}

export type TrailerInput = Omit<TrailerDetail, 'id' | 'internalNumber' | 'categoryName'>
