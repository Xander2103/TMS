export type PackageUnitType =
  | 'Package'
  | 'Parcel'
  | 'Colli'
  | 'Pallet'
  | 'Crate'
  | 'RollContainer'
  | 'Container'
  | 'Document'
  | 'Other'

export type PackageLifecycleStatus =
  | 'Created'
  | 'Labelled'
  | 'AwaitingLoading'
  | 'Loaded'
  | 'InTransit'
  | 'AtStop'
  | 'Delivered'
  | 'PartiallyDelivered'
  | 'DeliveryFailed'
  | 'Refused'
  | 'Missing'
  | 'Damaged'
  | 'Cancelled'
  | 'ReturnPending'
  | 'ReturnLoaded'
  | 'ReturnedToDepot'
  | 'ReturnedToSender'
  | 'RedeliveryPlanned'
  | 'Quarantined'

export type PackageExceptionState = 'None' | 'Open' | 'Resolved'

export const UNIT_TYPE_LABELS: Record<PackageUnitType, string> = {
  Package: 'Pakket',
  Parcel: 'Pakje',
  Colli: 'Colli',
  Pallet: 'Pallet',
  Crate: 'Krat',
  RollContainer: 'Rolcontainer',
  Container: 'Container',
  Document: 'Document',
  Other: 'Anders',
}

export const PACKAGE_STATUS_LABELS: Record<PackageLifecycleStatus, string> = {
  Created: 'Aangemaakt',
  Labelled: 'Geëtiketteerd',
  AwaitingLoading: 'Wacht op laden',
  Loaded: 'Geladen',
  InTransit: 'Onderweg',
  AtStop: 'Op stop',
  Delivered: 'Geleverd',
  PartiallyDelivered: 'Deels geleverd',
  DeliveryFailed: 'Levering mislukt',
  Refused: 'Geweigerd',
  Missing: 'Vermist',
  Damaged: 'Beschadigd',
  Cancelled: 'Geannuleerd',
  ReturnPending: 'Retour gepland',
  ReturnLoaded: 'Retour geladen',
  ReturnedToDepot: 'Terug in depot',
  ReturnedToSender: 'Terug naar afzender',
  RedeliveryPlanned: 'Herlevering gepland',
  Quarantined: 'In quarantaine',
}

export const PACKAGE_STATUS_TONE: Record<PackageLifecycleStatus, 'neutral' | 'info' | 'success' | 'warning' | 'danger'> = {
  Created: 'neutral',
  Labelled: 'neutral',
  AwaitingLoading: 'info',
  Loaded: 'info',
  InTransit: 'info',
  AtStop: 'info',
  Delivered: 'success',
  PartiallyDelivered: 'warning',
  DeliveryFailed: 'danger',
  Refused: 'danger',
  Missing: 'danger',
  Damaged: 'danger',
  Cancelled: 'neutral',
  ReturnPending: 'warning',
  ReturnLoaded: 'warning',
  ReturnedToDepot: 'info',
  ReturnedToSender: 'neutral',
  RedeliveryPlanned: 'warning',
  Quarantined: 'warning',
}

export interface Package {
  id: string
  transportOrderId: string
  packageNumber: string
  barcodeValue: string
  barcodeType: string
  externalBarcode: string | null
  externalPackageReference: string | null
  customerReference: string | null
  description: string
  quantity: number
  unitType: PackageUnitType
  unitTypeLabel: string | null
  weightKg: number | null
  volumeM3: number | null
  lengthCm: number | null
  widthCm: number | null
  heightCm: number | null
  parentPackageId: string | null
  loadingStopId: string | null
  deliveryStopId: string | null
  isMandatory: boolean
  isFragile: boolean
  requiresTemperatureControl: boolean
  requiresSignature: boolean
  status: PackageLifecycleStatus
  exceptionState: PackageExceptionState
  notes: string | null
  createdAt: string
}

export interface CreatePackageInput {
  description: string
  quantity: number
  unitType: PackageUnitType
  externalBarcode: string | null
  customerReference: string | null
  weightKg: number | null
  deliveryStopId: string | null
  isMandatory: boolean
  isFragile: boolean
  requiresTemperatureControl: boolean
  requiresSignature: boolean
}

export interface BulkCreateInput {
  count: number
  description: string
  unitType: PackageUnitType
  weightKg: number | null
  referencePrefix: string | null
  groupOnPallet: boolean
  deliveryStopId: string | null
}

export interface ImportRowResult {
  rowNumber: number
  action: 'Create' | 'Update' | 'Error'
  packageNumber: string | null
  description: string
  messages: string[]
}

export interface ImportPreview {
  totalRows: number
  creates: number
  updates: number
  errors: number
  rows: ImportRowResult[]
}

export interface ImportCommit {
  created: number
  updated: number
  failed: number
  committed: boolean
  rows: ImportRowResult[]
  errorWorkbookBase64: string | null
}

export type PackageDepartureRule = 'AllowWithWarning' | 'RequireOverride' | 'Block'

export interface TripPackageChecklistItem {
  packageId: string
  packageNumber: string
  description: string
  quantity: number
  unitType: string
  unitTypeLabel: string | null
  status: PackageLifecycleStatus
  exceptionState: PackageExceptionState
  isMandatory: boolean
  isGroup: boolean
  parentPackageId: string | null
  barcodeValue: string
  transportOrderId: string
  orderNumber: string
}

export interface TripPackageStopChecklist {
  stopId: string
  transportOrderId: string
  stopType: string
  sequence: number
  locationName: string | null
  city: string | null
  packages: TripPackageChecklistItem[]
}

export interface TripPackageChecklist {
  tripId: string
  stops: TripPackageStopChecklist[]
}

export interface TripPackageReadiness {
  tripId: string
  rule: PackageDepartureRule
  totalPackages: number
  mandatoryPackages: number
  loadedCount: number
  notLoadedCount: number
  missingCount: number
  damagedCount: number
  openExceptionCount: number
  isComplete: boolean
  requiresOverride: boolean
  isBlocked: boolean
  outstandingPackages: TripPackageChecklistItem[]
}

export type PackageIncidentAction =
  | 'Found'
  | 'ReleaseToLoad'
  | 'Return'
  | 'Quarantine'
  | 'Cancel'
  | 'Redeliver'
  | 'ReturnToSender'

export const PACKAGE_INCIDENT_ACTION_LABELS: Record<PackageIncidentAction, string> = {
  Found: 'Gevonden',
  ReleaseToLoad: 'Vrijgeven om te laden',
  Return: 'Retour',
  Quarantine: 'Quarantaine',
  Cancel: 'Annuleren',
  Redeliver: 'Herlevering plannen',
  ReturnToSender: 'Terug naar afzender',
}
