import { translate } from '../../i18n/translations'

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
  | 'Note'

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
  'Note',
]

/** Vertaalsleutels — renderen als t(SCHEDULE_STATE_KEYS[state]). */
export const SCHEDULE_STATE_KEYS: Record<ScheduleEntryState, string> = {
  Draft: 'employeePlanning.state.Draft',
  Planned: 'employeePlanning.state.Planned',
  Confirmed: 'employeePlanning.state.Confirmed',
  Standby: 'employeePlanning.state.Standby',
  Training: 'employeePlanning.state.Training',
  LeaveRequested: 'employeePlanning.state.LeaveRequested',
  LeaveApproved: 'employeePlanning.state.LeaveApproved',
  LeaveRejected: 'employeePlanning.state.LeaveRejected',
  Sick: 'employeePlanning.state.Sick',
  Unavailable: 'employeePlanning.state.Unavailable',
  Trip: 'employeePlanning.state.Trip',
  TripCancelled: 'employeePlanning.state.TripCancelled',
  Note: 'employeePlanning.state.Note',
}

/**
 * @deprecated Nederlandse literals zonder productie-consumers; blijft enkel als contract voor
 * de scheduleMeta-test. Schermen gebruiken SCHEDULE_STATE_KEYS + t().
 */
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
  Note: 'Persoonlijke notitie',
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
  Note: '📝',
}

/** Vertaalsleutels — renderen als t(SHIFT_TYPE_LABELS[type]). */
export const SHIFT_TYPE_LABELS: Record<ShiftType, string> = {
  Work: 'employeePlanning.shiftType.Work',
  Standby: 'employeePlanning.shiftType.Standby',
  Training: 'employeePlanning.shiftType.Training',
}

/** Vertaalsleutels — renderen als t(SHIFT_STATUS_LABELS[status]). */
export const SHIFT_STATUS_LABELS: Record<ShiftStatus, string> = {
  Draft: 'employeePlanning.shiftStatus.Draft',
  Planned: 'employeePlanning.shiftStatus.Planned',
  Confirmed: 'employeePlanning.shiftStatus.Confirmed',
}

export type ScheduleSourceType = 'Shift' | 'Absence' | 'Trip' | 'Note'
export type ConflictSeverity = 'Information' | 'Warning' | 'Blocking'

/** Vertaalsleutels — renderen als t(CONFLICT_SEVERITY_KEYS[severity]). */
export const CONFLICT_SEVERITY_KEYS: Record<ConflictSeverity, string> = {
  Information: 'employeePlanning.conflictSeverity.Information',
  Warning: 'employeePlanning.conflictSeverity.Warning',
  Blocking: 'employeePlanning.conflictSeverity.Blocking',
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
  /** Leave-type colour of absences or the chosen colour of a personal note. */
  colour: string | null
  noteId: string | null
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

type TranslateLike = (key: string, params?: Record<string, string | number>) => string

/**
 * Full accessible description: type, status, times, linked context, conflicts — for title and
 * aria-label. Taalneutraal: geef de actieve `t` uit useLocale() mee.
 */
export function describeChip(entry: ScheduleEntry, t: TranslateLike): string {
  const conflict = entry.conflictSeverity
    ? t('employeePlanning.chip.conflict', {
        severity: t(CONFLICT_SEVERITY_KEYS[entry.conflictSeverity]),
        notes: (entry.conflictNotes ?? []).join('; '),
      })
    : null
  return [
    entry.sourceType === 'Trip' ? t('employeePlanning.chip.trip', { number: entry.label }) : t(SCHEDULE_STATE_KEYS[entry.state]),
    entry.statusLabel,
    timeRangeOf(entry),
    entry.workLocation,
    entry.vehicleSummary,
    conflict,
  ]
    .filter((part): part is string => Boolean(part))
    .join(' · ')
}

/**
 * @deprecated Nederlandstalige variant zonder productie-consumers; blijft enkel als contract
 * voor de byte-identiteitstest (scheduleMeta). Geconverteerde code gebruikt describeChip(entry, t).
 */
export function chipDescription(entry: ScheduleEntry): string {
  return describeChip(entry, (key, params) => translate('nl', key, params))
}

export function formatMinutes(minutes: number): string {
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  return rest === 0 ? `${hours}u` : `${hours}u${String(rest).padStart(2, '0')}`
}

// `mondayOf`/`toIsoDate` live in the shared calendar date helpers (single implementation,
// reused by the shared calendar grid components as well as this feature). Re-exported here
// so existing imports from this module keep working unchanged.
export { mondayOf, toIsoDate } from '../../components/calendar/dateUtils'
