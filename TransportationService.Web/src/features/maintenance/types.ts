export type MaintenanceType = 'PeriodicService' | 'Repair' | 'TireService' | 'BrakeService' | 'Revision' | 'Other'
export type MaintenanceStatus = 'Planned' | 'InProgress' | 'Completed' | 'Cancelled'

/** i18n-keys (maintenance.type.*) — render via t(MAINTENANCE_TYPE_LABELS[x]). */
export const MAINTENANCE_TYPE_LABELS: Record<MaintenanceType, string> = {
  PeriodicService: 'maintenance.type.PeriodicService',
  Repair: 'maintenance.type.Repair',
  TireService: 'maintenance.type.TireService',
  BrakeService: 'maintenance.type.BrakeService',
  Revision: 'maintenance.type.Revision',
  Other: 'maintenance.type.Other',
}

export const MAINTENANCE_TYPES = Object.keys(MAINTENANCE_TYPE_LABELS) as MaintenanceType[]

/** i18n-keys (maintenance.status.*) — render via t(MAINTENANCE_STATUS_LABELS[x]). */
export const MAINTENANCE_STATUS_LABELS: Record<MaintenanceStatus, string> = {
  Planned: 'maintenance.status.Planned',
  InProgress: 'maintenance.status.InProgress',
  Completed: 'maintenance.status.Completed',
  Cancelled: 'maintenance.status.Cancelled',
}

export interface MaintenanceRecord {
  id: string
  vehicleId: string | null
  trailerId: string | null
  maintenanceType: MaintenanceType
  customTypeName: string | null
  status: MaintenanceStatus
  isOverdue: boolean
  description: string
  scheduledDate: string | null
  odometerTriggerKm: number | null
  completedDate: string | null
  completedOdometerKm: number | null
  workPerformed: string | null
  provider: string | null
  cost: number | null
  nextServiceDate: string | null
  nextServiceOdometerKm: number | null
  intervalMonths: number | null
  intervalKm: number | null
  hasAttachment: boolean
  notes: string | null
}

export interface MaintenanceInput {
  maintenanceType: MaintenanceType
  customTypeName: string | null
  description: string
  scheduledDate: string | null
  odometerTriggerKm: number | null
  provider: string | null
  intervalMonths: number | null
  intervalKm: number | null
  notes: string | null
}

export interface CompleteMaintenanceInput {
  completedDate: string
  completedOdometerKm: number | null
  workPerformed: string | null
  provider: string | null
  cost: number | null
  notes: string | null
}

/**
 * Display name: either the custom name (data) or a translation KEY. Callers render via
 * t(maintenanceDisplayName(record)) — t() echoes unknown keys, so custom names pass through.
 */
export function maintenanceDisplayName(record: Pick<MaintenanceRecord, 'maintenanceType' | 'customTypeName'>): string {
  return record.maintenanceType === 'Other' && record.customTypeName
    ? record.customTypeName
    : MAINTENANCE_TYPE_LABELS[record.maintenanceType]
}
