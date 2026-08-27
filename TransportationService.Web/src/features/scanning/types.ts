export type ScanType = 'Load' | 'Unload' | 'Return' | 'Depot' | 'Exception'

export type ScanResult =
  | 'Expected'
  | 'UnexpectedItem'
  | 'WrongItem'
  | 'DuplicateScan'
  | 'OverDelivery'
  | 'DamagedItem'
  | 'ManualCorrection'

export type ScanFeedbackLevel = 'Success' | 'Warning'

export type CargoScanState = 'Missing' | 'Partial' | 'Complete' | 'Over'

/** Vertaalsleutels (i18n-wave): render via t(SCAN_RESULT_LABELS[result]). */
export const SCAN_RESULT_LABELS: Record<ScanResult, string> = {
  Expected: 'scanning.scanResult.Expected',
  UnexpectedItem: 'scanning.scanResult.UnexpectedItem',
  WrongItem: 'scanning.scanResult.WrongItem',
  DuplicateScan: 'scanning.scanResult.DuplicateScan',
  OverDelivery: 'scanning.scanResult.OverDelivery',
  DamagedItem: 'scanning.scanResult.DamagedItem',
  ManualCorrection: 'scanning.scanResult.ManualCorrection',
}

export const SCAN_RESULT_ICONS: Record<ScanResult, string> = {
  Expected: '✓',
  UnexpectedItem: '?',
  WrongItem: '⇄',
  DuplicateScan: '≡',
  OverDelivery: '+',
  DamagedItem: '⚠',
  ManualCorrection: '✎',
}

export const SCAN_STATE_LABELS: Record<CargoScanState, string> = {
  Missing: 'scanning.scanState.Missing',
  Partial: 'scanning.scanState.Partial',
  Complete: 'scanning.scanState.Complete',
  Over: 'scanning.scanState.Over',
}

export const SCAN_STATE_TONE: Record<CargoScanState, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Missing: 'neutral',
  Partial: 'warning',
  Complete: 'success',
  Over: 'danger',
}

export const SCAN_STATE_ICONS: Record<CargoScanState, string> = {
  Missing: '○',
  Partial: '◐',
  Complete: '●',
  Over: '⬤+',
}

export interface CargoItemScanSummary {
  cargoItemId: string
  sequence: number
  description: string
  barcode: string | null
  expectedQuantity: number
  quantityUnit: string | null
  scannedQuantity: number
  damagedQuantity: number
  state: CargoScanState
}

export interface StopScanSummary {
  transportOrderStopId: string
  scanType: ScanType
  items: CargoItemScanSummary[]
  unexpectedScanCount: number
  totalScanCount: number
}

export type PackageScanOutcome =
  | 'Success'
  | 'Delivered'
  | 'ReplacedBarcode'
  | 'WrongTrip'
  | 'WrongOrder'
  | 'WrongLoadingStop'
  | 'WrongDeliveryStop'
  | 'AlreadyLoaded'
  | 'AlreadyDelivered'
  | 'NotLoaded'
  | 'CancelledPackage'
  | 'MissingPackage'
  | 'DamagedPackage'
  | 'Refused'
  | 'PartialDelivery'
  | 'NotScannable'
  | 'GroupProcessed'

export const PACKAGE_SCAN_OUTCOME_LABELS: Record<PackageScanOutcome, string> = {
  Success: 'scanning.packageOutcome.Success',
  Delivered: 'scanning.packageOutcome.Delivered',
  ReplacedBarcode: 'scanning.packageOutcome.ReplacedBarcode',
  WrongTrip: 'scanning.packageOutcome.WrongTrip',
  WrongOrder: 'scanning.packageOutcome.WrongOrder',
  WrongLoadingStop: 'scanning.packageOutcome.WrongLoadingStop',
  WrongDeliveryStop: 'scanning.packageOutcome.WrongDeliveryStop',
  AlreadyLoaded: 'scanning.packageOutcome.AlreadyLoaded',
  AlreadyDelivered: 'scanning.packageOutcome.AlreadyDelivered',
  NotLoaded: 'scanning.packageOutcome.NotLoaded',
  CancelledPackage: 'scanning.packageOutcome.CancelledPackage',
  MissingPackage: 'scanning.packageOutcome.MissingPackage',
  DamagedPackage: 'scanning.packageOutcome.DamagedPackage',
  Refused: 'scanning.packageOutcome.Refused',
  PartialDelivery: 'scanning.packageOutcome.PartialDelivery',
  NotScannable: 'scanning.packageOutcome.NotScannable',
  GroupProcessed: 'scanning.packageOutcome.GroupProcessed',
}

export interface PackageChildScanResult {
  packageId: string
  packageNumber: string
  description: string
  outcome: string
  succeeded: boolean
  message: string
}

export interface PackageScanFeedback {
  packageId: string
  packageNumber: string
  description: string
  outcome: string
  lifecycleStatus: string
  exceptionId: string | null
  children: PackageChildScanResult[]
}

export interface ScanFeedback {
  scanEventId: string
  result: ScanResult
  level: ScanFeedbackLevel
  message: string
  cargoItemId: string | null
  cargoDescription: string | null
  acceptedQuantity: number
  expectedQuantity: number
  summary: StopScanSummary
  package: PackageScanFeedback | null
  replayed: boolean
}

export interface ScanEventEntry {
  id: string
  transportOrderStopId: string
  cargoItemId: string | null
  cargoDescription: string | null
  scanType: ScanType
  result: ScanResult
  barcode: string | null
  quantity: number
  damaged: boolean
  damageNote: string | null
  correctionReason: string | null
  deviceInfo: string | null
  userName: string | null
  occurredAt: string
  packageId: string | null
  packageNumber: string | null
  packageOutcome: string | null
}
