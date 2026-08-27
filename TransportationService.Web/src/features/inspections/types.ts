export type InspectionType = 'VehicleInspection' | 'TrailerInspection' | 'CraneInspection' | 'Other'
export type InspectionResult = 'Passed' | 'PassedWithRemarks' | 'Failed'
export type InspectionUrgency = 'Ok' | 'DueSoon' | 'Overdue' | 'Completed'

/** i18n-keys (maintenance.insp.type.*) — render via t(INSPECTION_TYPE_LABELS[x]). */
export const INSPECTION_TYPE_LABELS: Record<InspectionType, string> = {
  VehicleInspection: 'maintenance.insp.type.VehicleInspection',
  TrailerInspection: 'maintenance.insp.type.TrailerInspection',
  CraneInspection: 'maintenance.insp.type.CraneInspection',
  Other: 'maintenance.insp.type.Other',
}

export const INSPECTION_TYPES = Object.keys(INSPECTION_TYPE_LABELS) as InspectionType[]

/** i18n-keys (maintenance.insp.result.*) — render via t(INSPECTION_RESULT_LABELS[x]). */
export const INSPECTION_RESULT_LABELS: Record<InspectionResult, string> = {
  Passed: 'maintenance.insp.result.Passed',
  PassedWithRemarks: 'maintenance.insp.result.PassedWithRemarks',
  Failed: 'maintenance.insp.result.Failed',
}

/** i18n-keys (maintenance.insp.urgency.*) — render via t(INSPECTION_URGENCY_LABELS[x]). */
export const INSPECTION_URGENCY_LABELS: Record<InspectionUrgency, string> = {
  Ok: 'maintenance.insp.urgency.Ok',
  DueSoon: 'maintenance.insp.urgency.DueSoon',
  Overdue: 'maintenance.insp.urgency.Overdue',
  Completed: 'maintenance.insp.urgency.Completed',
}

export interface Inspection {
  id: string
  vehicleId: string | null
  trailerId: string | null
  inspectionType: InspectionType
  customTypeName: string | null
  dueDate: string
  completedDate: string | null
  result: InspectionResult | null
  intervalMonths: number | null
  warningDays: number | null
  urgency: InspectionUrgency
  hasAttachment: boolean
  notes: string | null
}

export interface InspectionInput {
  inspectionType: InspectionType
  customTypeName: string | null
  dueDate: string
  intervalMonths: number | null
  warningDays: number | null
  notes: string | null
}

export interface CompleteInspectionInput {
  completedDate: string
  result: InspectionResult
  notes: string | null
}

/**
 * Display name: either the custom name (data) or a translation KEY. Callers render via
 * t(inspectionDisplayName(inspection)) — t() echoes unknown keys, so custom names pass through.
 */
export function inspectionDisplayName(inspection: Pick<Inspection, 'inspectionType' | 'customTypeName'>): string {
  return inspection.inspectionType === 'Other' && inspection.customTypeName
    ? inspection.customTypeName
    : INSPECTION_TYPE_LABELS[inspection.inspectionType]
}
