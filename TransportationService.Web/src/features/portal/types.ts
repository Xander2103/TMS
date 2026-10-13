export interface MyProfile {
  employeeId: string
  employeeNumber: string
  firstName: string
  lastName: string
  email: string
  phoneNumber: string
  mobilePhone: string | null
  street: string
  houseNumber: string
  postalCode: string
  city: string
  countryCode: string | null
  emergencyContactName: string | null
  emergencyContactPhone: string | null
  employmentStartDate: string
  employmentStatus: string
  departmentName: string | null
  jobFunctions: string[]
  isDriver: boolean
  driverNumber: string | null
}

export interface MyDashboard {
  firstName: string
  unreadNotifications: number
  openAbsenceRequests: number
  upcomingApprovedAbsences: number
  expiringQualifications: number
  isDriver: boolean
  tripsToday: number
  tripsThisWeek: number
}

export type MyQualificationStatus = 'Pending' | 'Valid' | 'ExpiringSoon' | 'Expired' | 'Rejected' | 'Suspended'

export interface MyQualification {
  id: string
  qualificationTypeCode: string
  qualificationTypeName: string
  documentNumber: string | null
  obtainedDate: string
  expiryDate: string | null
  effectiveStatus: MyQualificationStatus
  hasDocument: boolean
}

/** Translation keys per status; render sites resolve them via t(). */
export const MY_QUALIFICATION_STATUS_LABELS: Record<MyQualificationStatus, string> = {
  Pending: 'portalHome.qualificationStatus.Pending',
  Valid: 'portalHome.qualificationStatus.Valid',
  ExpiringSoon: 'portalHome.qualificationStatus.ExpiringSoon',
  Expired: 'portalHome.qualificationStatus.Expired',
  Rejected: 'portalHome.qualificationStatus.Rejected',
  Suspended: 'portalHome.qualificationStatus.Suspended',
}

export const MY_QUALIFICATION_STATUS_TONE: Record<
  MyQualificationStatus,
  'neutral' | 'success' | 'warning' | 'danger' | 'info'
> = {
  Pending: 'neutral',
  Valid: 'success',
  ExpiringSoon: 'warning',
  Expired: 'danger',
  Rejected: 'danger',
  Suspended: 'danger',
}
