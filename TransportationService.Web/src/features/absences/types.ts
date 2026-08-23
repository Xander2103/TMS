export type AbsenceType = 'Vacation' | 'Sick' | 'Training' | 'PersonalLeave' | 'Unpaid' | 'Other'
export type AbsenceStatus = 'Requested' | 'UnderReview' | 'Approved' | 'Rejected' | 'Cancelled'
export type AbsencePartDay = 'FullDay' | 'Morning' | 'Afternoon'

/** Vertaalsleutels — renderen als t(ABSENCE_TYPE_LABELS[type]). */
export const ABSENCE_TYPE_LABELS: Record<AbsenceType, string> = {
  Vacation: 'absences.type.Vacation',
  Sick: 'absences.type.Sick',
  Training: 'absences.type.Training',
  PersonalLeave: 'absences.type.PersonalLeave',
  Unpaid: 'absences.type.Unpaid',
  Other: 'absences.type.Other',
}

export const ABSENCE_TYPES: AbsenceType[] = ['Vacation', 'Sick', 'Training', 'PersonalLeave', 'Unpaid', 'Other']

/** Vertaalsleutels — renderen als t(ABSENCE_STATUS_LABELS[status]). */
export const ABSENCE_STATUS_LABELS: Record<AbsenceStatus, string> = {
  Requested: 'absences.status.Requested',
  UnderReview: 'absences.status.UnderReview',
  Approved: 'absences.status.Approved',
  Rejected: 'absences.status.Rejected',
  Cancelled: 'absences.status.Cancelled',
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

/** Vertaalsleutels — renderen als t(PART_DAY_LABELS[partDay]). */
export const PART_DAY_LABELS: Record<AbsencePartDay, string> = {
  FullDay: 'absences.partDay.FullDay',
  Morning: 'absences.partDay.Morning',
  Afternoon: 'absences.partDay.Afternoon',
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
