export type ShiftType = 'Work' | 'Standby' | 'Training'
export type ShiftStatus = 'Draft' | 'Planned' | 'Confirmed'

export type ScheduleEntryState =
  | 'Draft'
  | 'Planned'
  | 'Confirmed'
  | 'Standby'
  | 'Training'
  | 'LeaveRequested'
  | 'LeaveApproved'
  | 'LeaveRejected'
  | 'Sick'
  | 'Unavailable'
  | 'Trip'
  | 'TripCancelled'

export const SCHEDULE_STATES: ScheduleEntryState[] = [
  'Draft',
  'Planned',
  'Confirmed',
  'Standby',
  'Training',
  'LeaveRequested',
  'LeaveApproved',
  'LeaveRejected',
  'Sick',
  'Unavailable',
  'Trip',
  'TripCancelled',
]

export const SCHEDULE_STATE_LABELS: Record<ScheduleEntryState, string> = {
  Draft: 'Concept',
  Planned: 'Gepland',
  Confirmed: 'Bevestigd',
  Standby: 'Stand-by',
  Training: 'Opleiding',
  LeaveRequested: 'Verlof aangevraagd',
  LeaveApproved: 'Verlof goedgekeurd',
  LeaveRejected: 'Verlof afgewezen',
  Sick: 'Ziek',
  Unavailable: 'Onbeschikbaar',
  Trip: 'Rit',
  TripCancelled: 'Rit geannuleerd',
}

/**
 * Colour is never the only signal: every state also carries an icon and its label.
 * The CSS classes (schedule-chip-<state>) implement the colour convention with
 * accessible contrast in light and dark mode.
 */
export const SCHEDULE_STATE_ICONS: Record<ScheduleEntryState, string> = {
  Draft: '▢',
  Planned: '◔',
  Confirmed: '●',
  Standby: '☏',
  Training: '✎',
  LeaveRequested: '⧖',
  LeaveApproved: '🏖',
  LeaveRejected: '✕',
  Sick: '＋',
  Unavailable: '⊘',
  Trip: '🚚',
  TripCancelled: '🚫',
}

export const SHIFT_TYPE_LABELS: Record<ShiftType, string> = {
  Work: 'Werk',
  Standby: 'Stand-by',
  Training: 'Opleiding',
}

export const SHIFT_STATUS_LABELS: Record<ShiftStatus, string> = {
  Draft: 'Concept',
  Planned: 'Gepland',
  Confirmed: 'Bevestigd',
}

export type ScheduleSourceType = 'Shift' | 'Absence' | 'Trip'
export type ConflictSeverity = 'Information' | 'Warning' | 'Blocking'

export const CONFLICT_SEVERITY_LABELS: Record<ConflictSeverity, string> = {
  Information: 'Ter info',
  Warning: 'Waarschuwing',
  Blocking: 'Blokkerend',
}

export interface ScheduleEntry {
  state: ScheduleEntryState
  shiftId: string | null
  absenceId: string | null
  tripId: string | null
  sourceType: ScheduleSourceType
  label: string
  startTime: string | null
  endTime: string | null
  shiftType: ShiftType | null
  workLocation: string | null
  vehicleSummary: string | null
  statusLabel: string | null
  conflictSeverity: ConflictSeverity | null
  conflictNotes: string[] | null
}

export interface ScheduleDay {
  date: string
  entries: ScheduleEntry[]
}

export interface ScheduleRow {
  employeeId: string
  employeeName: string
  employeeNumber: string
  departmentName: string | null
  plannedMinutes: number
  days: ScheduleDay[]
}

export interface ScheduleGrid {
  from: string
  to: string
  rows: ScheduleRow[]
}

export interface Shift {
  id: string
  employeeId: string
  employeeName: string
  date: string
  startTime: string
  endTime: string
  breakMinutes: number
  plannedMinutes: number
  type: ShiftType
  status: ShiftStatus
  workLocation: string | null
  roleLabel: string | null
  notes: string | null
}

export interface ShiftInput {
  employeeId: string
  date: string
  startTime: string
  endTime: string
  breakMinutes: number
  type: ShiftType
  workLocation: string | null
  roleLabel: string | null
  notes: string | null
  /** Set after a 409: asks the server to override blocking conflicts (permission-gated). */
  override?: boolean
}

function timeRangeOf(entry: ScheduleEntry): string | null {
  if (!entry.startTime || !entry.endTime) return null
  return `${entry.startTime.slice(0, 5)}–${entry.endTime.slice(0, 5)}`
}

/** Full accessible description: type, status, times, linked context, conflicts — for title and aria-label. */
export function chipDescription(entry: ScheduleEntry): string {
  const conflict = entry.conflictSeverity
    ? `Conflict (${CONFLICT_SEVERITY_LABELS[entry.conflictSeverity]}): ${(entry.conflictNotes ?? []).join('; ')}`
    : null
  return [
    entry.sourceType === 'Trip' ? `Rit ${entry.label}` : SCHEDULE_STATE_LABELS[entry.state],
    entry.statusLabel,
    timeRangeOf(entry),
    entry.workLocation,
    entry.vehicleSummary,
    conflict,
  ]
    .filter((part): part is string => Boolean(part))
    .join(' · ')
}

export function formatMinutes(minutes: number): string {
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  return rest === 0 ? `${hours}u` : `${hours}u${String(rest).padStart(2, '0')}`
}

/** Monday of the week containing the given date (ISO week start). */
export function mondayOf(date: Date): Date {
  const result = new Date(date)
  const day = (result.getDay() + 6) % 7
  result.setDate(result.getDate() - day)
  result.setHours(0, 0, 0, 0)
  return result
}

export function toIsoDate(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}
