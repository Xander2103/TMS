export type TrailerOwnershipType = 'Owned' | 'Rented' | 'Leased'
export type TrailerOperationalStatus = 'Active' | 'InMaintenance' | 'OutOfService' | 'Decommissioned'

export const TRAILER_OWNERSHIP_LABELS: Record<TrailerOwnershipType, string> = {
  Owned: 'Eigendom',
  Rented: 'Huur',
  Leased: 'Lease',
}

export const TRAILER_STATUS_LABELS: Record<TrailerOperationalStatus, string> = {
  Active: 'Actief',
  InMaintenance: 'In onderhoud',
  OutOfService: 'Buiten dienst',
  Decommissioned: 'Afgevoerd',
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
  hasRefrigeration: boolean
  adrSuitable: boolean
  ownershipType: TrailerOwnershipType
  operationalStatus: TrailerOperationalStatus
  isActive: boolean
  notes: string | null
}

export type TrailerInput = Omit<TrailerDetail, 'id' | 'internalNumber' | 'categoryName'>
