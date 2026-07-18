export type InspectionType = 'VehicleInspection' | 'TrailerInspection' | 'CraneInspection' | 'Other'
export type InspectionResult = 'Passed' | 'PassedWithRemarks' | 'Failed'
export type InspectionUrgency = 'Ok' | 'DueSoon' | 'Overdue' | 'Completed'

export const INSPECTION_TYPE_LABELS: Record<InspectionType, string> = {
  VehicleInspection: 'Technische keuring (voertuig)',
  TrailerInspection: 'Keuring oplegger',
  CraneInspection: 'Kraankeuring',
  Other: 'Overig',
}

export const INSPECTION_TYPES = Object.keys(INSPECTION_TYPE_LABELS) as InspectionType[]

export const INSPECTION_RESULT_LABELS: Record<InspectionResult, string> = {
  Passed: 'Goedgekeurd',
  PassedWithRemarks: 'Goedgekeurd met opmerkingen',
  Failed: 'Afgekeurd',
}

export const INSPECTION_URGENCY_LABELS: Record<InspectionUrgency, string> = {
  Ok: 'Gepland',
  DueSoon: 'Binnenkort',
  Overdue: 'Te laat',
  Completed: 'Uitgevoerd',
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

export function inspectionDisplayName(inspection: Pick<Inspection, 'inspectionType' | 'customTypeName'>): string {
  return inspection.inspectionType === 'Other' && inspection.customTypeName
    ? inspection.customTypeName
    : INSPECTION_TYPE_LABELS[inspection.inspectionType]
}
