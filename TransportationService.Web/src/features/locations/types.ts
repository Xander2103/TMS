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
  | 'ConstructionSite'
  | 'TemporaryLocation'
  | 'Other'

export const LOCATION_TYPE_LABELS: Record<LocationType, string> = {
  CompanySite: 'Bedrijfssite',
  Depot: 'Depot',
  Warehouse: 'Magazijn',
  CustomerLocation: 'Klantlocatie',
  Terminal: 'Terminal',
  LoadingLocation: 'Laadadres',
  UnloadingLocation: 'Losadres',
  ParkingLocation: 'Parking',
  Office: 'Kantoor',
  RegisteredOffice: 'Maatschappelijke zetel',
  AdministrativeAddress: 'Administratief adres',
  BillingAddress: 'Facturatieadres',
  ReturnsAddress: 'Retouradres',
  ConstructionSite: 'Werf',
  TemporaryLocation: 'Tijdelijke locatie',
  Other: 'Overig',
}

export const LOCATION_TYPES = Object.keys(LOCATION_TYPE_LABELS) as LocationType[]

/**
 * One structured opening window. Times travel as "HH:mm" strings — exactly what the weekly
 * editor and `<input type="time">` produce; dayOfWeek is ISO (1 = maandag .. 7 = zondag).
 */
export interface LocationOpeningInterval {
  dayOfWeek: number
  fromTime: string
  toTime: string
  note: string | null
}

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
  contactMobile: string | null
  contactEmail: string | null
  /** Optional link to a CustomerContact of the linked customer (snapshot fields stay editable). */
  customerContactId: string | null
  externalReference: string | null
  openingHours: string | null
  /** Structured weekly hours; a day without intervals is closed/unknown. */
  openingIntervals: LocationOpeningInterval[] | null
  loadingInstructions: string | null
  unloadingInstructions: string | null
  accessInstructions: string | null
  accessRestrictions: string | null
  vehicleRestrictions: string | null
  trailerRestrictions: string | null
  alfapassRequired: boolean
  appointmentRequired: boolean
  gate: string | null
  /** Absent/null unless the caller holds locations.view_sensitive. */
  accessCode?: string | null
  receptionPoint: string | null
  dock: string | null
  routeDescription: string | null
  deliveryByAppointmentOnly: boolean
  heightRestrictionMeters: number | null
  weightRestrictionTons: number | null
  /** Null = onbekend (never guessed to false). */
  adrAllowed: boolean | null
  craneRequired: boolean
  forkliftAvailable: boolean
  driverInstructions: string | null
  /** Internal-only memo, never shown on driver/customer surfaces. */
  internalMemo: string | null
  defaultLoadingMinutes: number | null
  defaultUnloadingMinutes: number | null
  preferredArrivalFrom: string | null
  preferredArrivalTo: string | null
  earliestArrival: string | null
  latestArrival: string | null
  isActive: boolean
  customerId: string | null
  customerName: string | null
  notes: string | null
  isDefaultLoadingLocation: boolean
  isDefaultUnloadingLocation: boolean
  isDefaultBillingLocation: boolean
}

/**
 * Create/update payload. Notes on backend semantics:
 * - `openingIntervals` must ALWAYS echo the full list on update — sending null clears every
 *   interval, so the type requires an array (send [] to intentionally clear).
 * - `accessCode` must be OMITTED entirely when the user lacks locations.view_sensitive
 *   (the backend then preserves the stored value).
 */
export type LocationInput = Omit<LocationDetail, 'id' | 'customerName' | 'accessCode' | 'openingIntervals'> & {
  accessCode?: string | null
  openingIntervals: LocationOpeningInterval[]
}

/** Blank location form value; spread and override to prefill (e.g. customerId from the URL). */
export const EMPTY_LOCATION_INPUT: LocationInput = {
  code: '',
  name: '',
  type: 'Depot',
  street: null,
  houseNumber: null,
  postalCode: null,
  city: null,
  countryCode: null,
  latitude: null,
  longitude: null,
  contactName: null,
  contactPhone: null,
  contactMobile: null,
  contactEmail: null,
  customerContactId: null,
  externalReference: null,
  openingHours: null,
  openingIntervals: [],
  loadingInstructions: null,
  unloadingInstructions: null,
  accessInstructions: null,
  accessRestrictions: null,
  vehicleRestrictions: null,
  trailerRestrictions: null,
  alfapassRequired: false,
  appointmentRequired: false,
  gate: null,
  accessCode: null,
  receptionPoint: null,
  dock: null,
  routeDescription: null,
  deliveryByAppointmentOnly: false,
  heightRestrictionMeters: null,
  weightRestrictionTons: null,
  adrAllowed: null,
  craneRequired: false,
  forkliftAvailable: false,
  driverInstructions: null,
  internalMemo: null,
  defaultLoadingMinutes: null,
  defaultUnloadingMinutes: null,
  preferredArrivalFrom: null,
  preferredArrivalTo: null,
  earliestArrival: null,
  latestArrival: null,
  isActive: true,
  customerId: null,
  notes: null,
  isDefaultLoadingLocation: false,
  isDefaultUnloadingLocation: false,
  isDefaultBillingLocation: false,
}
