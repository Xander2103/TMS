export type AbsenceType = 'Vacation' | 'Sick' | 'Training' | 'PersonalLeave' | 'Unpaid' | 'Other'
export type AbsenceStatus = 'Requested' | 'UnderReview' | 'Approved' | 'Rejected' | 'Cancelled'
export type AbsencePartDay = 'FullDay' | 'Morning' | 'Afternoon'

export const ABSENCE_TYPE_LABELS: Record<AbsenceType, string> = {
  Vacation: 'Verlof',
  Sick: 'Ziekte',
  Training: 'Opleiding',
  PersonalLeave: 'Persoonlijk verlof',
  Unpaid: 'Onbetaald verlof',
  Other: 'Overig',
}

export const ABSENCE_TYPES: AbsenceType[] = ['Vacation', 'Sick', 'Training', 'PersonalLeave', 'Unpaid', 'Other']

export const ABSENCE_STATUS_LABELS: Record<AbsenceStatus, string> = {
  Requested: 'Aangevraagd',
  UnderReview: 'In behandeling',
  Approved: 'Goedgekeurd',
  Rejected: 'Afgewezen',
  Cancelled: 'Ingetrokken',
}

export const ABSENCE_STATUSES: AbsenceStatus[] = ['Requested', 'UnderReview', 'Approved', 'Rejected', 'Cancelled']

/** Badge tone per status; lives here (not in a component file) so pages can share it. */
export const ABSENCE_STATUS_TONE: Record<AbsenceStatus, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Requested: 'warning',
  UnderReview: 'info',
  Approved: 'success',
  Rejected: 'danger',
  Cancelled: 'neutral',
}

export const PART_DAY_LABELS: Record<AbsencePartDay, string> = {
  FullDay: 'Hele dag',
  Morning: 'Voormiddag',
  Afternoon: 'Namiddag',
}

export interface Absence {
  id: string
  employeeId: string
  employeeName: string
  employeeNumber: string
  isDriver: boolean
  type: AbsenceType
  startDate: string
  endDate: string
  status: AbsenceStatus
  reason: string | null
  decisionNote: string | null
  decidedAt: string | null
  partDay: AbsencePartDay
  internalNote: string | null
  hasAttachment: boolean
  attachmentFileName: string | null
  leaveTypeId: string | null
}

export interface AbsenceInput {
  type: AbsenceType
  startDate: string
  endDate: string
  reason: string | null
  partDay?: AbsencePartDay
  /** Configurable leave type (source of truth for new records); drives balance deduction. */
  leaveTypeId?: string | null
}

export interface OverlappingShift {
  date: string
  startTime: string
  endTime: string
  workLocation: string | null
}

export interface OverlappingTrip {
  tripId: string
  tripNumber: string
  tripDate: string
}

export interface OverlappingColleague {
  employeeName: string
  type: AbsenceType
  startDate: string
  endDate: string
  status: AbsenceStatus
}

export interface AbsenceReviewContext {
  overlappingShifts: OverlappingShift[]
  overlappingTrips: OverlappingTrip[]
  overlappingColleagues: OverlappingColleague[]
  usedVacationDaysThisYear: number
  hasAttachment: boolean
}
