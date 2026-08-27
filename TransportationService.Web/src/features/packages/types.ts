export type PackageUnitType =
  | 'Package'
  | 'Parcel'
  | 'Colli'
  | 'Pallet'
  | 'EuroPallet'
  | 'BlockPallet'
  | 'Box'
  | 'Crate'
  | 'RollContainer'
  | 'Drum'
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

/** Vertaalsleutels — renderen als t(UNIT_TYPE_LABELS[type]). */
export const UNIT_TYPE_LABELS: Record<PackageUnitType, string> = {
  Package: 'packages.unitType.Package',
  Parcel: 'packages.unitType.Parcel',
  Colli: 'packages.unitType.Colli',
  Pallet: 'packages.unitType.Pallet',
  EuroPallet: 'packages.unitType.EuroPallet',
  BlockPallet: 'packages.unitType.BlockPallet',
  Box: 'packages.unitType.Box',
  Crate: 'packages.unitType.Crate',
  RollContainer: 'packages.unitType.RollContainer',
  Drum: 'packages.unitType.Drum',
  Container: 'packages.unitType.Container',
  Document: 'packages.unitType.Document',
  Other: 'packages.unitType.Other',
}

/** Vertaalsleutels — renderen als t(PACKAGE_STATUS_LABELS[status]). */
export const PACKAGE_STATUS_LABELS: Record<PackageLifecycleStatus, string> = {
  Created: 'packages.status.Created',
  Labelled: 'packages.status.Labelled',
  AwaitingLoading: 'packages.status.AwaitingLoading',
  Loaded: 'packages.status.Loaded',
  InTransit: 'packages.status.InTransit',
  AtStop: 'packages.status.AtStop',
  Delivered: 'packages.status.Delivered',
  PartiallyDelivered: 'packages.status.PartiallyDelivered',
  DeliveryFailed: 'packages.status.DeliveryFailed',
  Refused: 'packages.status.Refused',
  Missing: 'packages.status.Missing',
  Damaged: 'packages.status.Damaged',
  Cancelled: 'packages.status.Cancelled',
  ReturnPending: 'packages.status.ReturnPending',
  ReturnLoaded: 'packages.status.ReturnLoaded',
  ReturnedToDepot: 'packages.status.ReturnedToDepot',
  ReturnedToSender: 'packages.status.ReturnedToSender',
  RedeliveryPlanned: 'packages.status.RedeliveryPlanned',
  Quarantined: 'packages.status.Quarantined',
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

/** Vertaalsleutels — renderen als t(PACKAGE_EVENT_LABELS[eventType] ?? …) met de code als fallback. */
export const PACKAGE_EVENT_LABELS: Record<string, string> = {
  Created: 'packages.event.Created',
  Labelled: 'packages.event.Labelled',
  LabelReprinted: 'packages.event.LabelReprinted',
  Relabelled: 'packages.event.Relabelled',
  StatusChanged: 'packages.event.StatusChanged',
  LoadScan: 'packages.event.LoadScan',
  LoadMissing: 'packages.event.LoadMissing',
  WrongPackageScan: 'packages.event.WrongPackageScan',
  UnloadScan: 'packages.event.UnloadScan',
  Delivered: 'packages.event.Delivered',
  Refused: 'packages.event.Refused',
  PartialDelivery: 'packages.event.PartialDelivery',
  DamageReported: 'packages.event.DamageReported',
  MarkedMissing: 'packages.event.MarkedMissing',
  ExceptionResolved: 'packages.event.ExceptionResolved',
  GroupAssigned: 'packages.event.GroupAssigned',
  GroupRemoved: 'packages.event.GroupRemoved',
  GroupBroken: 'packages.event.GroupBroken',
  MovedToTrip: 'packages.event.MovedToTrip',
  Cancelled: 'packages.event.Cancelled',
  DispositionSet: 'packages.event.DispositionSet',
  ReturnLoaded: 'packages.event.ReturnLoaded',
  ReturnedToDepot: 'packages.event.ReturnedToDepot',
  ReturnedToSender: 'packages.event.ReturnedToSender',
  RedeliveryLoaded: 'packages.event.RedeliveryLoaded',
  Quarantined: 'packages.event.Quarantined',
  DepartureOverride: 'packages.event.DepartureOverride',
  CompletionOverride: 'packages.event.CompletionOverride',
  PodFinalized: 'packages.event.PodFinalized',
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

/** Vertaalsleutels — renderen als t(PACKAGE_INCIDENT_ACTION_LABELS[action]). */
export const PACKAGE_INCIDENT_ACTION_LABELS: Record<PackageIncidentAction, string> = {
  Found: 'packages.incidentAction.Found',
  ReleaseToLoad: 'packages.incidentAction.ReleaseToLoad',
  Return: 'packages.incidentAction.Return',
  Quarantine: 'packages.incidentAction.Quarantine',
  Cancel: 'packages.incidentAction.Cancel',
  Redeliver: 'packages.incidentAction.Redeliver',
  ReturnToSender: 'packages.incidentAction.ReturnToSender',
}
