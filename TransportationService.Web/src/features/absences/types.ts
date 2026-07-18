export type AbsenceType = 'Vacation' | 'Sick' | 'Training' | 'PersonalLeave' | 'Unpaid' | 'Other'
export type AbsenceStatus = 'Requested' | 'Approved' | 'Rejected' | 'Cancelled'

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
  Approved: 'Goedgekeurd',
  Rejected: 'Afgewezen',
  Cancelled: 'Geannuleerd',
}

export const ABSENCE_STATUSES: AbsenceStatus[] = ['Requested', 'Approved', 'Rejected', 'Cancelled']

/** Badge tone per status; lives here (not in a component file) so pages can share it. */
export const ABSENCE_STATUS_TONE: Record<AbsenceStatus, 'neutral' | 'success' | 'warning' | 'danger' | 'info'> = {
  Requested: 'warning',
  Approved: 'success',
  Rejected: 'danger',
  Cancelled: 'neutral',
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
}

export interface AbsenceInput {
  type: AbsenceType
  startDate: string
  endDate: string
  reason: string | null
}
