import type { BadgeTone } from '../../components/ui/Badge'

// ── Status & punches ─────────────────────────────────────────────────────────────

export type AttendanceLiveStatus = 'NotClockedIn' | 'Working' | 'OnBreak' | 'ClockedOut'

export interface AttendanceStatus {
  status: AttendanceLiveStatus
  sessionId: string | null
  clockInAt: string | null
  breakStartedAt: string | null
  lastClockOutAt: string | null
  workedMinutesToday: number
  breakMinutesToday: number
  canClockIn: boolean
  canClockOut: boolean
  canStartBreak: boolean
  canEndBreak: boolean
}

/** Vertaalsleutels — renderen als t(ATTENDANCE_LIVE_STATUS_LABELS[status]). */
export const ATTENDANCE_LIVE_STATUS_LABELS: Record<AttendanceLiveStatus, string> = {
  NotClockedIn: 'attendance.status.NotClockedIn',
  Working: 'attendance.status.Working',
  OnBreak: 'attendance.status.OnBreak',
  ClockedOut: 'attendance.status.ClockedOut',
}

export const ATTENDANCE_LIVE_STATUS_TONE: Record<AttendanceLiveStatus, BadgeTone> = {
  NotClockedIn: 'neutral',
  Working: 'success',
  OnBreak: 'info',
  ClockedOut: 'neutral',
}

// ── Historie ─────────────────────────────────────────────────────────────────────

export type AttendanceSessionStatus = 'Working' | 'OnBreak' | 'Completed' | 'AutoClosed' | 'Cancelled'

export type AttendanceSource = 'Web' | 'Kiosk' | 'Mobile' | 'Api' | 'Import' | 'Manual'

/** Vertaalsleutels — renderen als t(ATTENDANCE_SOURCE_LABELS[source]). */
export const ATTENDANCE_SOURCE_LABELS: Record<AttendanceSource, string> = {
  Web: 'attendance.source.Web',
  Kiosk: 'attendance.source.Kiosk',
  Mobile: 'attendance.source.Mobile',
  Api: 'attendance.source.Api',
  Import: 'attendance.source.Import',
  Manual: 'attendance.source.Manual',
}

export type AttendanceCorrectionKind =
  | 'ClockIn' | 'ClockOut' | 'BreakStart' | 'BreakEnd' | 'SessionCancelled' | 'ManualSession'

export interface AttendanceBreak {
  id: string
  startedAt: string
  endedAt: string | null
  minutes: number
}

export interface AttendanceCorrection {
  id: string
  kind: AttendanceCorrectionKind
  breakId: string | null
  oldValue: string | null
  newValue: string | null
  reason: string
  correctedByName: string | null
  correctedAt: string
}

export interface AttendanceSession {
  id: string
  employeeId: string
  clockInAt: string
  clockOutAt: string | null
  status: AttendanceSessionStatus
  clockInSource: AttendanceSource
  locationId: string | null
  locationName: string | null
  grossMinutes: number
  breakMinutes: number
  netMinutes: number
  hasCorrections: boolean
  version: string
  breaks: AttendanceBreak[]
  corrections: AttendanceCorrection[]
}

export interface AttendanceDay {
  date: string
  grossMinutes: number
  breakMinutes: number
  netMinutes: number
  plannedMinutes: number | null
  deviationMinutes: number | null
  sessions: AttendanceSession[]
}

export interface AttendanceHistory {
  from: string
  to: string
  totalGrossMinutes: number
  totalBreakMinutes: number
  totalNetMinutes: number
  totalPlannedMinutes: number | null
  days: AttendanceDay[]
}

// ── HR-overzicht ─────────────────────────────────────────────────────────────────

export type AttendanceOverviewStatus =
  | 'NotClockedIn' | 'Working' | 'OnBreak' | 'ClockedOut' | 'ForgottenClockOut' | 'Absent'

/** Vertaalsleutels — renderen als t(ATTENDANCE_OVERVIEW_STATUS_LABELS[status]). */
export const ATTENDANCE_OVERVIEW_STATUS_LABELS: Record<AttendanceOverviewStatus, string> = {
  NotClockedIn: 'attendance.overviewStatus.NotClockedIn',
  Working: 'attendance.overviewStatus.Working',
  OnBreak: 'attendance.overviewStatus.OnBreak',
  ClockedOut: 'attendance.overviewStatus.ClockedOut',
  ForgottenClockOut: 'attendance.overviewStatus.ForgottenClockOut',
  Absent: 'attendance.overviewStatus.Absent',
}

export const ATTENDANCE_OVERVIEW_STATUS_TONE: Record<AttendanceOverviewStatus, BadgeTone> = {
  NotClockedIn: 'neutral',
  Working: 'success',
  OnBreak: 'info',
  ClockedOut: 'neutral',
  ForgottenClockOut: 'warning',
  Absent: 'info',
}

export const ATTENDANCE_OVERVIEW_STATUSES: readonly AttendanceOverviewStatus[] = [
  'Working', 'OnBreak', 'NotClockedIn', 'ClockedOut', 'ForgottenClockOut', 'Absent',
]

export interface AttendanceOverviewRow {
  employeeId: string
  name: string
  employeeNumber: string
  departmentName: string | null
  status: AttendanceOverviewStatus
  since: string | null
  workedMinutes: number
  breakMinutes: number
  plannedMinutes: number | null
  plannedStart: string | null
  plannedEnd: string | null
  plannedNotClockedIn: boolean
  hasCorrections: boolean
  absenceLabel: string | null
}

export interface AttendanceOverviewSummary {
  working: number
  onBreak: number
  clockedOut: number
  notClockedIn: number
  plannedNotClockedIn: number
  forgottenClockOut: number
  absent: number
}

export interface AttendanceOverview {
  date: string
  summary: AttendanceOverviewSummary
  rows: AttendanceOverviewRow[]
}

// ── Rapportering ─────────────────────────────────────────────────────────────────

export interface AttendanceReportRow {
  employeeId: string
  employeeName: string
  employeeNumber: string
  departmentName: string | null
  date: string
  grossMinutes: number
  breakMinutes: number
  netMinutes: number
  plannedMinutes: number | null
  deviationMinutes: number | null
  missingClockOut: boolean
  correctionCount: number
}

export interface AttendanceReport {
  from: string
  to: string
  totalGrossMinutes: number
  totalBreakMinutes: number
  totalNetMinutes: number
  totalPlannedMinutes: number
  rows: AttendanceReportRow[]
}

// ── Driver Activity Card ─────────────────────────────────────────────────────────

export interface DriverDayPlan {
  startTime: string
  endTime: string
  breakMinutes: number
  roleLabel: string | null
}

export interface DriverDaySummary {
  attendance: AttendanceStatus
  plannedToday: DriverDayPlan[]
  /** v1: altijd false — de UI toont dan "Tachograafdata niet gekoppeld". Nooit rijtijd uit attendance afleiden. */
  tachographConnected: boolean
}

// ── Instellingen & beheer ────────────────────────────────────────────────────────

export interface AttendanceSettings {
  selfPunchEnabled: boolean
  kioskEnabled: boolean
  pinLength: number
  forgottenClockOutAfterHours: number
  autoCloseEnabled: boolean
  autoCloseAfterHours: number
  plannedNotClockedInGraceMinutes: number
  kioskConfigured: boolean
}

export interface KioskDevice {
  id: string
  name: string
  locationId: string | null
  locationName: string | null
  isActive: boolean
  lastSeenAt: string | null
  lastPunchAt: string | null
  createdAt: string
  defaultLanguage: 'nl' | 'fr' | 'en'
}

export interface KioskProvisionResult {
  device: KioskDevice
  /** Wordt exact één keer getoond; daarna bestaat server-side alleen nog de hash. */
  deviceKey: string
}

export interface AttendanceCredentialStatus {
  hasCredential: boolean
  isActive: boolean
  lastUsedAt: string | null
  lockedUntil: string | null
}

export interface AttendanceCredentialResult {
  outcome: 'Success' | 'EmployeeNotFound' | 'InvalidPin' | 'PinInUse' | 'NotConfigured' | 'NoCredential'
  status: AttendanceCredentialStatus | null
  generatedPin: string | null
  error: string | null
}

// ── Kiosk ────────────────────────────────────────────────────────────────────────

export type KioskOutcome =
  | 'Success' | 'InvalidDevice' | 'InvalidCode' | 'KioskDisabled'
  | 'NotConfigured' | 'TokenExpired' | 'InvalidAction' | 'PunchRejected'

export type KioskPunchAction = 'ClockIn' | 'ClockOut' | 'StartBreak' | 'EndBreak'

export interface KioskPing {
  outcome: KioskOutcome
  deviceName: string | null
  locationName: string | null
  error: string | null
  /** Standaardtaal van het device — het beginscherm rendert hierin. */
  defaultLanguage: 'nl' | 'fr' | 'en'
}

export interface KioskIdentifyResult {
  outcome: KioskOutcome
  firstName: string | null
  status: AttendanceStatus | null
  interactionToken: string | null
  error: string | null
  /** Persoonlijke taal van de medewerker — alleen aanwezig ná geldige identificatie. */
  preferredLanguage: 'nl' | 'fr' | 'en' | null
}

export interface KioskPunchResult {
  outcome: KioskOutcome
  status: AttendanceStatus | null
  occurredAt: string | null
  error: string | null
}
