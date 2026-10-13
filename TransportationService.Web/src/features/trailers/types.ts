export type TrailerOwnershipType = 'Owned' | 'Rented' | 'Leased'
export type TrailerOperationalStatus = 'Available' | 'InUse' | 'InMaintenance' | 'OutOfService'

/** i18n-keys (trailers.ownership.*) — render via t(TRAILER_OWNERSHIP_LABELS[x]). */
export const TRAILER_OWNERSHIP_LABELS: Record<TrailerOwnershipType, string> = {
  Owned: 'trailers.ownership.Owned',
  Rented: 'trailers.ownership.Rented',
  Leased: 'trailers.ownership.Leased',
}

// Deliberately no label "Actief" here — see vehicles/types.ts.
/** i18n-keys (trailers.status.*) — render via t(TRAILER_STATUS_LABELS[x]). */
export const TRAILER_STATUS_LABELS: Record<TrailerOperationalStatus, string> = {
  Available: 'trailers.status.Available',
  InUse: 'trailers.status.InUse',
  InMaintenance: 'trailers.status.InMaintenance',
  OutOfService: 'trailers.status.OutOfService',
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
