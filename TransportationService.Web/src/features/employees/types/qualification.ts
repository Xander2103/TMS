import type { BadgeTone } from '../../../components/ui/Badge'

export type QualificationStatus = 'Pending' | 'Valid' | 'ExpiringSoon' | 'Expired' | 'Rejected' | 'Suspended'

/** Vertaalsleutels — renderen als t(QUALIFICATION_STATUS_LABELS[status]). */
export const QUALIFICATION_STATUS_LABELS: Record<QualificationStatus, string> = {
  Pending: 'employees.qualificationStatus.Pending',
  Valid: 'employees.qualificationStatus.Valid',
  ExpiringSoon: 'employees.qualificationStatus.ExpiringSoon',
  Expired: 'employees.qualificationStatus.Expired',
  Rejected: 'employees.qualificationStatus.Rejected',
  Suspended: 'employees.qualificationStatus.Suspended',
}

export const QUALIFICATION_STATUS_TONES: Record<QualificationStatus, BadgeTone> = {
  Pending: 'info',
  Valid: 'success',
  ExpiringSoon: 'warning',
  Expired: 'danger',
  Rejected: 'danger',
  Suspended: 'neutral',
}

export interface QualificationType {
  id: string
  code: string
  name: string
  description: string | null
  category: string
  requiresExpiryDate: boolean
  isActive: boolean
}

export interface EmployeeQualification {
  id: string
  employeeId: string
  qualificationTypeId: string
  qualificationTypeCode: string
  qualificationTypeName: string
  documentNumber: string | null
  obtainedDate: string
  expiryDate: string | null
  issuingCountryCode: string | null
  storedStatus: QualificationStatus
  effectiveStatus: QualificationStatus
  hasDocument: boolean
  notes: string | null
  verifiedAt: string | null
  verifiedByUserId: string | null
}

export interface ExpiringQualification {
  id: string
  employeeId: string
  employeeName: string
  employeeNumber: string
  qualificationTypeCode: string
  qualificationTypeName: string
  expiryDate: string | null
  effectiveStatus: QualificationStatus
  employeeIsDriver: boolean
}

export interface CreateEmployeeQualificationInput {
  qualificationTypeId: string
  documentNumber: string | null
  obtainedDate: string
  expiryDate: string | null
  notes: string | null
  issuingCountryCode: string | null
}

export interface UpdateEmployeeQualificationInput {
  documentNumber: string | null
  obtainedDate: string
  expiryDate: string | null
  notes: string | null
  issuingCountryCode: string | null
}
