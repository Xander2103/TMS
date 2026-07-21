export type LocationType =
  | 'CompanySite'
  | 'Depot'
  | 'Warehouse'
  | 'CustomerLocation'
  | 'Terminal'
  | 'LoadingLocation'
  | 'UnloadingLocation'
  | 'ParkingLocation'
  | 'Office'
  | 'RegisteredOffice'
  | 'AdministrativeAddress'
  | 'BillingAddress'
  | 'ReturnsAddress'

export const LOCATION_TYPE_LABELS: Record<LocationType, string> = {
  CompanySite: 'Bedrijfssite',
  Depot: 'Depot',
  Warehouse: 'Magazijn',
  CustomerLocation: 'Klantlocatie',
  Terminal: 'Terminal',
  LoadingLocation: 'Laadlocatie',
  UnloadingLocation: 'Loslocatie',
  ParkingLocation: 'Parking',
  Office: 'Kantoor',
  RegisteredOffice: 'Maatschappelijke zetel',
  AdministrativeAddress: 'Administratief adres',
  BillingAddress: 'Facturatieadres',
  ReturnsAddress: 'Retouradres',
}

export const LOCATION_TYPES = Object.keys(LOCATION_TYPE_LABELS) as LocationType[]

export interface LocationListItem {
  id: string
  code: string
  name: string
  type: LocationType
  city: string | null
  countryCode: string | null
  customerName: string | null
  isActive: boolean
  isDefaultLoadingLocation: boolean
  isDefaultUnloadingLocation: boolean
  isDefaultBillingLocation: boolean
}

export interface LocationOption {
  id: string
  code: string
  name: string
  type: LocationType
  city: string | null
  isDefaultLoadingLocation: boolean
  isDefaultUnloadingLocation: boolean
  isDefaultBillingLocation: boolean
}

export interface LocationDetail {
  id: string
  code: string
  name: string
  type: LocationType
  street: string | null
  houseNumber: string | null
  postalCode: string | null
  city: string | null
  countryCode: string | null
  latitude: number | null
  longitude: number | null
  contactName: string | null
  contactPhone: string | null
  contactEmail: string | null
  openingHours: string | null
  loadingInstructions: string | null
  unloadingInstructions: string | null
  accessInstructions: string | null
  accessRestrictions: string | null
  vehicleRestrictions: string | null
  trailerRestrictions: string | null
  alfapassRequired: boolean
  appointmentRequired: boolean
  isActive: boolean
  customerId: string | null
  customerName: string | null
  notes: string | null
  isDefaultLoadingLocation: boolean
  isDefaultUnloadingLocation: boolean
  isDefaultBillingLocation: boolean
}

export type LocationInput = Omit<LocationDetail, 'id' | 'customerName'>
