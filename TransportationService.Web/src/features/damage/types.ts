export type DamageSeverity = 'Minor' | 'Moderate' | 'Severe' | 'TotalLoss'
export type DamageStatus = 'Reported' | 'UnderAssessment' | 'InRepair' | 'Repaired' | 'Closed'

/** Vertaalsleutels (i18n-wave): render via t(DAMAGE_..._LABELS[code]). */
export const DAMAGE_SEVERITY_LABELS: Record<DamageSeverity, string> = {
  Minor: 'damage.severity.Minor',
  Moderate: 'damage.severity.Moderate',
  Severe: 'damage.severity.Severe',
  TotalLoss: 'damage.severity.TotalLoss',
}

export const DAMAGE_STATUS_LABELS: Record<DamageStatus, string> = {
  Reported: 'damage.status.Reported',
  UnderAssessment: 'damage.status.UnderAssessment',
  InRepair: 'damage.status.InRepair',
  Repaired: 'damage.status.Repaired',
  Closed: 'damage.status.Closed',
}

export interface DamageReport {
  id: string
  vehicleId: string | null
  trailerId: string | null
  driverId: string | null
  driverName: string | null
  incidentDate: string
  location: string | null
  description: string
  severity: DamageSeverity
  status: DamageStatus
  insuranceReference: string | null
  repairCost: number | null
  downtimeDays: number | null
  hasAttachment: boolean
  notes: string | null
  /** FL-M7: the trip during which the damage occurred, when known. */
  tripId?: string | null
  tripNumber?: string | null
  /** EX-03: the incident/afwijking this report was created from, described for the fleet. */
  sourceIncidentId?: string | null
  sourceExceptionId?: string | null
  sourceLabel?: string | null
}

export interface CreateDamageInput {
  driverId: string | null
  incidentDate: string
  location: string | null
  description: string
  severity: DamageSeverity
  insuranceReference: string | null
  notes: string | null
  /** FL-M7: optional — parking-yard damage has no trip. */
  tripId: string | null
  repairCost: number | null
  downtimeDays: number | null
}

export interface UpdateDamageInput extends CreateDamageInput {
  status: DamageStatus
}
