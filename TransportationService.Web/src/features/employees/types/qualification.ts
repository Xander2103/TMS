export type QualificationStatus = 'Pending' | 'Valid' | 'ExpiringSoon' | 'Expired' | 'Rejected' | 'Suspended'

export const QUALIFICATION_STATUS_LABELS: Record<QualificationStatus, string> = {
  Pending: 'In behandeling',
  Valid: 'Geldig',
  ExpiringSoon: 'Verloopt binnenkort',
  Expired: 'Verlopen',
  Rejected: 'Afgewezen',
  Suspended: 'Geschorst',
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
  storedStatus: QualificationStatus
  effectiveStatus: QualificationStatus
  documentPath: string | null
  notes: string | null
  verifiedAt: string | null
  verifiedByUserId: string | null
}

export interface CreateEmployeeQualificationInput {
  qualificationTypeId: string
  documentNumber: string | null
  obtainedDate: string
  expiryDate: string | null
  notes: string | null
}

export interface UpdateEmployeeQualificationInput {
  documentNumber: string | null
  obtainedDate: string
  expiryDate: string | null
  notes: string | null
}
