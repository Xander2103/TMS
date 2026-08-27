export type FuelType = 'Diesel' | 'Petrol' | 'Electric' | 'Hybrid' | 'Lng' | 'Cng' | 'Hydrogen' | 'Other'
export type EmissionClass =
  | 'Euro0'
  | 'Euro1'
  | 'Euro2'
  | 'Euro3'
  | 'Euro4'
  | 'Euro5'
  | 'Euro6'
  | 'Euro7'
  | 'Electric'
  | 'Other'
export type VehicleOwnershipType = 'Owned' | 'Rented' | 'Leased'
export type VehicleOperationalStatus = 'Available' | 'InUse' | 'InMaintenance' | 'OutOfService'

/** i18n-keys (vehicles.fuelType.*) — render via t(FUEL_TYPE_LABELS[x]). */
export const FUEL_TYPE_LABELS: Record<FuelType, string> = {
  Diesel: 'vehicles.fuelType.Diesel',
  Petrol: 'vehicles.fuelType.Petrol',
  Electric: 'vehicles.fuelType.Electric',
  Hybrid: 'vehicles.fuelType.Hybrid',
  Lng: 'vehicles.fuelType.Lng',
  Cng: 'vehicles.fuelType.Cng',
  Hydrogen: 'vehicles.fuelType.Hydrogen',
  Other: 'vehicles.fuelType.Other',
}

/** i18n-keys (vehicles.emissionClass.*) — render via t(EMISSION_CLASS_LABELS[x]). */
export const EMISSION_CLASS_LABELS: Record<EmissionClass, string> = {
  Euro0: 'vehicles.emissionClass.Euro0',
  Euro1: 'vehicles.emissionClass.Euro1',
  Euro2: 'vehicles.emissionClass.Euro2',
  Euro3: 'vehicles.emissionClass.Euro3',
  Euro4: 'vehicles.emissionClass.Euro4',
  Euro5: 'vehicles.emissionClass.Euro5',
  Euro6: 'vehicles.emissionClass.Euro6',
  Euro7: 'vehicles.emissionClass.Euro7',
  Electric: 'vehicles.emissionClass.Electric',
  Other: 'vehicles.emissionClass.Other',
}

/** Belgian driving-licence codes relevant for vehicle eligibility (B < C1 < C < CE). */
export const REQUIRED_LICENCE_CODES = ['B', 'C1', 'C', 'CE'] as const

/** i18n-keys (vehicles.ownership.*) — render via t(OWNERSHIP_TYPE_LABELS[x]). */
export const OWNERSHIP_TYPE_LABELS: Record<VehicleOwnershipType, string> = {
  Owned: 'vehicles.ownership.Owned',
  Rented: 'vehicles.ownership.Rented',
  Leased: 'vehicles.ownership.Leased',
}

// Deliberately no label "Actief" here: the administrative active flag owns that word,
// so the two states can never render as duplicate badges.
/** i18n-keys (vehicles.status.*) — render via t(OPERATIONAL_STATUS_LABELS[x]). */
export const OPERATIONAL_STATUS_LABELS: Record<VehicleOperationalStatus, string> = {
  Available: 'vehicles.status.Available',
  InUse: 'vehicles.status.InUse',
  InMaintenance: 'vehicles.status.InMaintenance',
  OutOfService: 'vehicles.status.OutOfService',
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
  volumeIsManual: boolean
  odometerKm: number
  consumptionLPer100Km: number | null
  axleCount: number
  loadingMeters: number
  requiredLicenceCode: string | null
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
