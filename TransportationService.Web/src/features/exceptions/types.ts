/** Every server type (scan- and package-level ones included) — none may render blank or raw. */
export type ExecutionExceptionType =
  | 'MissingPackage'
  | 'WrongPackage'
  | 'DamagedPackage'
  | 'UnknownPackage'
  | 'WrongRoutePackage'
  | 'WrongStopPackage'
  | 'IncompleteQuantity'
  | 'UnreadableBarcode'
  | 'LabelMissing'
  | 'RejectedBeforeLoading'
  | 'RejectedDelivery'
  | 'PartialDelivery'
  | 'AddressIssue'
  | 'CustomerUnavailable'
  | 'AccessDenied'
  | 'VehicleIssue'
  | 'Delay'
  | 'Other'

export type ExceptionSeverity = 'Low' | 'Medium' | 'High' | 'Critical'

export type ExecutionExceptionStatus = 'Open' | 'Investigating' | 'Resolved' | 'Rejected' | 'CustomerActionRequired'

/** Vertaalsleutels (i18n-wave): render via t(EXCEPTION_TYPE_LABELS[type]). */
export const EXCEPTION_TYPE_LABELS: Record<ExecutionExceptionType, string> = {
  MissingPackage: 'exceptions.type.MissingPackage',
  WrongPackage: 'exceptions.type.WrongPackage',
  DamagedPackage: 'exceptions.type.DamagedPackage',
  UnknownPackage: 'exceptions.type.UnknownPackage',
  WrongRoutePackage: 'exceptions.type.WrongRoutePackage',
  WrongStopPackage: 'exceptions.type.WrongStopPackage',
  IncompleteQuantity: 'exceptions.type.IncompleteQuantity',
  UnreadableBarcode: 'exceptions.type.UnreadableBarcode',
  LabelMissing: 'exceptions.type.LabelMissing',
  RejectedBeforeLoading: 'exceptions.type.RejectedBeforeLoading',
  RejectedDelivery: 'exceptions.type.RejectedDelivery',
  PartialDelivery: 'exceptions.type.PartialDelivery',
  AddressIssue: 'exceptions.type.AddressIssue',
  CustomerUnavailable: 'exceptions.type.CustomerUnavailable',
  AccessDenied: 'exceptions.type.AccessDenied',
  VehicleIssue: 'exceptions.type.VehicleIssue',
  Delay: 'exceptions.type.Delay',
  Other: 'exceptions.type.Other',
}

export const EXCEPTION_TYPES: ExecutionExceptionType[] = [
  'MissingPackage',
  'WrongPackage',
  'DamagedPackage',
  'RejectedDelivery',
  'PartialDelivery',
  'AddressIssue',
  'CustomerUnavailable',
  'AccessDenied',
  'VehicleIssue',
  'Delay',
  'Other',
]

/** Filter lists: every type, also the scan/package ones the driver dialog does not offer. */
export const EXCEPTION_ALL_TYPES = Object.keys(EXCEPTION_TYPE_LABELS) as ExecutionExceptionType[]

export const EXCEPTION_SEVERITY_LABELS: Record<ExceptionSeverity, string> = {
  Low: 'exceptions.severity.Low',
  Medium: 'exceptions.severity.Medium',
  High: 'exceptions.severity.High',
  Critical: 'exceptions.severity.Critical',
}

export const EXCEPTION_SEVERITY_TONE: Record<ExceptionSeverity, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Low: 'neutral',
  Medium: 'info',
  High: 'warning',
  Critical: 'danger',
}

export const EXCEPTION_SEVERITY_ICONS: Record<ExceptionSeverity, string> = {
  Low: '·',
  Medium: '•',
  High: '▲',
  Critical: '⚠',
}

export const EXCEPTION_SEVERITIES: ExceptionSeverity[] = ['Low', 'Medium', 'High', 'Critical']

export const EXCEPTION_STATUS_LABELS: Record<ExecutionExceptionStatus, string> = {
  Open: 'exceptions.status.Open',
  Investigating: 'exceptions.status.Investigating',
  Resolved: 'exceptions.status.Resolved',
  Rejected: 'exceptions.status.Rejected',
  CustomerActionRequired: 'exceptions.status.CustomerActionRequired',
}

export const EXCEPTION_STATUS_TONE: Record<ExecutionExceptionStatus, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Open: 'warning',
  Investigating: 'info',
  Resolved: 'success',
  Rejected: 'danger',
  CustomerActionRequired: 'warning',
}

export const EXCEPTION_STATUS_ICONS: Record<ExecutionExceptionStatus, string> = {
  Open: '◌',
  Investigating: '␦',
  Resolved: '✓',
  Rejected: '✕',
  CustomerActionRequired: '⚑',
}

export const EXCEPTION_STATUSES: ExecutionExceptionStatus[] = [
  'Open',
  'Investigating',
  'CustomerActionRequired',
  'Resolved',
  'Rejected',
]

export interface ExceptionPhoto {
  id: string
  fileName: string
  contentType: string
  createdAt: string
}

export interface ExceptionListItem {
  id: string
  occurredAt: string
  type: ExecutionExceptionType
  severity: ExceptionSeverity
  status: ExecutionExceptionStatus
  description: string
  tripNumber: string
  orderNumber: string | null
  stopLabel: string | null
  reportedByName: string | null
  photoCount: number
  customerVisible: boolean
  packageId: string | null
  packageNumber: string | null
  assignedToUserId: string | null
  assignedToName: string | null
}

export interface ExceptionDetail {
  id: string
  type: ExecutionExceptionType
  severity: ExceptionSeverity
  status: ExecutionExceptionStatus
  description: string
  quantity: number | null
  tripId: string
  tripNumber: string
  transportOrderId: string | null
  orderNumber: string | null
  transportOrderStopId: string | null
  stopLabel: string | null
  cargoItemId: string | null
  cargoDescription: string | null
  packageId: string | null
  packageNumber: string | null
  packageStatus: string | null
  assignedToUserId: string | null
  assignedToName: string | null
  reportedByName: string | null
  driverName: string | null
  occurredAt: string
  latitude: number | null
  longitude: number | null
  dispatcherNotes: string | null
  customerVisible: boolean
  resolutionNote: string | null
  resolvedByName: string | null
  resolvedAt: string | null
  photos: ExceptionPhoto[]
  allowedTransitions: ExecutionExceptionStatus[]
  /** B24: the dossier/customer behind the order (absent on older API builds). */
  dossierId?: string | null
  dossierNumber?: string | null
  customerId?: string | null
  customerName?: string | null
  /** Klantgerichte tekst; de omschrijving van de chauffeur blijft intern. */
  customerMessage?: string | null
  /** Afwijking → Incident. */
  escalatedIncidentId?: string | null
  escalatedIncidentTitle?: string | null
  vehicleId?: string | null
  vehicleLabel?: string | null
  damageReportId?: string | null
  canCreateDamageReport?: boolean
  vehicleSafetyIssue?: boolean
  /** Foto's van een afgehandelde/geëscaleerde afwijking zijn bewijsmateriaal. */
  photosLocked?: boolean
}

export interface ReportExceptionInput {
  type: ExecutionExceptionType
  severity: ExceptionSeverity
  description: string
  transportOrderStopId: string | null
  cargoItemId: string | null
  quantity: number | null
  latitude: number | null
  longitude: number | null
}
