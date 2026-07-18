export type DamageSeverity = 'Minor' | 'Moderate' | 'Severe' | 'TotalLoss'
export type DamageStatus = 'Reported' | 'UnderAssessment' | 'InRepair' | 'Repaired' | 'Closed'

export const DAMAGE_SEVERITY_LABELS: Record<DamageSeverity, string> = {
  Minor: 'Licht',
  Moderate: 'Matig',
  Severe: 'Zwaar',
  TotalLoss: 'Totaal verlies',
}

export const DAMAGE_STATUS_LABELS: Record<DamageStatus, string> = {
  Reported: 'Gemeld',
  UnderAssessment: 'In beoordeling',
  InRepair: 'In herstelling',
  Repaired: 'Hersteld',
  Closed: 'Afgesloten',
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
}

export interface CreateDamageInput {
  driverId: string | null
  incidentDate: string
  location: string | null
  description: string
  severity: DamageSeverity
  insuranceReference: string | null
  notes: string | null
}

export interface UpdateDamageInput extends CreateDamageInput {
  status: DamageStatus
  repairCost: number | null
  downtimeDays: number | null
}
