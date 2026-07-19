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

export interface WarehouseTripStop {
  stopId: string
  locationName: string | null
  city: string | null
  expectedPackages: number
}

export interface WarehouseTrip {
  tripId: string
  tripNumber: string
  tripDate: string
  status: string
  driverName: string | null
  vehicleNumber: string | null
  orderCount: number
  totalPackages: number
  mandatoryPackages: number
  loadedCount: number
  notLoadedCount: number
  missingCount: number
  damagedCount: number
  openExceptionCount: number
  isComplete: boolean
  rule: PackageDepartureRule
  loadingStops: WarehouseTripStop[]
}

export interface WarehousePackageRow {
  packageId: string
  packageNumber: string
  description: string
  status: PackageLifecycleStatus
  exceptionState: PackageExceptionState
  transportOrderId: string
  orderNumber: string
  tripId: string | null
  tripNumber: string | null
}

export interface PackageTimelineEvent {
  id: string
  eventType: string
  oldStatus: PackageLifecycleStatus | null
  newStatus: PackageLifecycleStatus | null
  occurredAt: string
  userName: string | null
  tripNumber: string | null
  stopLabel: string | null
  barcodeUsed: string | null
  result: string | null
  notes: string | null
  isOverride: boolean
  exceptionId: string | null
  scanEventId: string | null
}

export const PACKAGE_EVENT_LABELS: Record<string, string> = {
  Created: 'Aangemaakt',
  Labelled: 'Geëtiketteerd',
  LabelReprinted: 'Etiket opnieuw afgedrukt',
  Relabelled: 'Heretiketteerd',
  StatusChanged: 'Status gewijzigd',
  LoadScan: 'Laadscan',
  LoadMissing: 'Ontbrak bij laden',
  WrongPackageScan: 'Verkeerde scan',
  UnloadScan: 'Losscan',
  Delivered: 'Afgeleverd',
  Refused: 'Geweigerd',
  PartialDelivery: 'Gedeeltelijke levering',
  DamageReported: 'Schade gemeld',
  MarkedMissing: 'Vermist gemeld',
  ExceptionResolved: 'Melding opgelost',
  GroupAssigned: 'Aan groep toegevoegd',
  GroupRemoved: 'Uit groep gehaald',
  GroupBroken: 'Groep opgeheven',
  MovedToTrip: 'Naar andere rit',
  Cancelled: 'Geannuleerd',
  DispositionSet: 'Vervolgactie gekozen',
  ReturnLoaded: 'Retour geladen',
  ReturnedToDepot: 'Terug in depot',
  ReturnedToSender: 'Terug naar afzender',
  RedeliveryLoaded: 'Geladen voor herlevering',
  Quarantined: 'In quarantaine',
  DepartureOverride: 'Vertrek vrijgegeven',
  CompletionOverride: 'Stop afgerond met vrijgave',
  PodFinalized: 'POD vastgelegd',
}

export interface PackageBarcodeRow {
  id: string
  value: string
  type: string
  isActive: boolean
  createdAt: string
  retiredAt: string | null
  retireReason: string | null
}

export interface PackageLabelVersion {
  id: string
  version: number
  format: string
  printedAt: string
  reprintReason: string | null
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
