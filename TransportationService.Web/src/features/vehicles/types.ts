export type FuelType = 'Diesel' | 'Petrol' | 'Electric' | 'Hybrid' | 'Lng' | 'Cng' | 'Hydrogen' | 'Other'
export type EmissionClass = 'Euro3' | 'Euro4' | 'Euro5' | 'Euro6' | 'Electric' | 'Other'
export type VehicleOwnershipType = 'Owned' | 'Rented' | 'Leased'
export type VehicleOperationalStatus = 'Available' | 'InUse' | 'InMaintenance' | 'OutOfService'

export const FUEL_TYPE_LABELS: Record<FuelType, string> = {
  Diesel: 'Diesel',
  Petrol: 'Benzine',
  Electric: 'Elektrisch',
  Hybrid: 'Hybride',
  Lng: 'LNG',
  Cng: 'CNG',
  Hydrogen: 'Waterstof',
  Other: 'Overig',
}

export const EMISSION_CLASS_LABELS: Record<EmissionClass, string> = {
  Euro3: 'Euro 3',
  Euro4: 'Euro 4',
  Euro5: 'Euro 5',
  Euro6: 'Euro 6',
  Electric: 'Elektrisch',
  Other: 'Overig',
}

export const OWNERSHIP_TYPE_LABELS: Record<VehicleOwnershipType, string> = {
  Owned: 'Eigendom',
  Rented: 'Huur',
  Leased: 'Lease',
}

// Deliberately no label "Actief" here: the administrative active flag owns that word,
// so the two states can never render as duplicate badges.
export const OPERATIONAL_STATUS_LABELS: Record<VehicleOperationalStatus, string> = {
  Available: 'Beschikbaar',
  InUse: 'In gebruik',
  InMaintenance: 'In onderhoud',
  OutOfService: 'Buiten dienst',
}

export const OPERATIONAL_STATUS_TONES: Record<VehicleOperationalStatus, 'success' | 'info' | 'warning' | 'danger'> = {
  Available: 'success',
  InUse: 'info',
  InMaintenance: 'warning',
  OutOfService: 'danger',
}

export interface VehicleListItem {
  id: string
  internalNumber: string
  licensePlate: string
  brand: string | null
  model: string | null
  categoryName: string | null
  operationalStatus: VehicleOperationalStatus
  isActive: boolean
}

export interface VehicleOption {
  id: string
  internalNumber: string
  licensePlate: string
  brand: string | null
  model: string | null
}

export interface VehicleDetail {
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
  fuelType: FuelType
  emissionClass: EmissionClass | null
  grossVehicleWeightKg: number | null
  payloadKg: number | null
  lengthMeters: number | null
  widthMeters: number | null
  heightMeters: number | null
  volumeM3: number | null
  odometerKm: number
  consumptionLPer100Km: number | null
  hasCrane: boolean
  hasRefrigeration: boolean
  hasTailLift: boolean
  adrSuitable: boolean
  ownershipType: VehicleOwnershipType
  operationalStatus: VehicleOperationalStatus
  statusReason: string | null
  isActive: boolean
  fixedDriverId: string | null
  fixedDriverName: string | null
  currentDriverId: string | null
  currentDriverName: string | null
  notes: string | null
}

/**
 * Editable vehicle fields. Driver assignment is NOT part of this shape: it is managed through
 * the dedicated assignment endpoints so both the driver and vehicle pages stay in sync.
 */
export type VehicleInput = Omit<
  VehicleDetail,
  'id' | 'internalNumber' | 'categoryName' | 'fixedDriverId' | 'fixedDriverName' | 'currentDriverId' | 'currentDriverName'
>

/** Create-only variant: initial driver assignment may be supplied when registering a vehicle. */
export interface CreateVehicleInput extends VehicleInput {
  fixedDriverId: string | null
  currentDriverId: string | null
}
