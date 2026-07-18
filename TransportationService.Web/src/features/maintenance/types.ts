export type MaintenanceType = 'PeriodicService' | 'Repair' | 'TireService' | 'BrakeService' | 'Revision' | 'Other'
export type MaintenanceStatus = 'Planned' | 'InProgress' | 'Completed' | 'Cancelled'

export const MAINTENANCE_TYPE_LABELS: Record<MaintenanceType, string> = {
  PeriodicService: 'Periodiek onderhoud',
  Repair: 'Herstelling',
  TireService: 'Bandenservice',
  BrakeService: 'Remmenservice',
  Revision: 'Revisie',
  Other: 'Overig',
}

export const MAINTENANCE_TYPES = Object.keys(MAINTENANCE_TYPE_LABELS) as MaintenanceType[]

export const MAINTENANCE_STATUS_LABELS: Record<MaintenanceStatus, string> = {
  Planned: 'Gepland',
  InProgress: 'In uitvoering',
  Completed: 'Afgerond',
  Cancelled: 'Geannuleerd',
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

export function maintenanceDisplayName(record: Pick<MaintenanceRecord, 'maintenanceType' | 'customTypeName'>): string {
  return record.maintenanceType === 'Other' && record.customTypeName
    ? record.customTypeName
    : MAINTENANCE_TYPE_LABELS[record.maintenanceType]
}
