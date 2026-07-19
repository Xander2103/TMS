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

export const SCAN_RESULT_LABELS: Record<ScanResult, string> = {
  Expected: 'Verwacht item',
  UnexpectedItem: 'Onbekend item',
  WrongItem: 'Verkeerd item',
  DuplicateScan: 'Duplicaat',
  OverDelivery: 'Meer dan verwacht',
  DamagedItem: 'Schade',
  ManualCorrection: 'Correctie',
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
  Missing: 'Niet gescand',
  Partial: 'Gedeeltelijk',
  Complete: 'Volledig',
  Over: 'Te veel',
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
  Success: 'Geladen',
  Delivered: 'Afgeleverd',
  ReplacedBarcode: 'Vervangen barcode',
  WrongTrip: 'Verkeerde rit',
  WrongOrder: 'Andere opdracht',
  WrongLoadingStop: 'Verkeerde laadstop',
  WrongDeliveryStop: 'Verkeerde losstop',
  AlreadyLoaded: 'Al geladen',
  AlreadyDelivered: 'Al afgeleverd',
  NotLoaded: 'Niet geladen',
  CancelledPackage: 'Geannuleerd colli',
  MissingPackage: 'Vermist colli',
  DamagedPackage: 'Schade',
  Refused: 'Geweigerd',
  PartialDelivery: 'Gedeeltelijk geleverd',
  NotScannable: 'Niet scanbaar',
  GroupProcessed: 'Groep verwerkt',
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
