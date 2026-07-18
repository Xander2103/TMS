import type { MaintenanceType } from '../maintenance/types'
import type { InspectionType, InspectionUrgency } from '../inspections/types'
import type { FleetDocumentStatus, FleetDocumentType } from '../fleet-documents/types'
import type { DamageSeverity, DamageStatus } from '../damage/types'
import type { FuelWarning } from '../fuel/types'

export interface FleetAssetCounts {
  total: number
  active: number
  inMaintenance: number
  outOfService: number
  decommissioned: number
  inactive: number
}

export interface DueMaintenanceItem {
  id: string
  vehicleId: string | null
  trailerId: string | null
  ownerNumber: string
  ownerLicensePlate: string
  maintenanceType: MaintenanceType
  customTypeName: string | null
  description: string
  scheduledDate: string | null
  isOverdue: boolean
}

export interface DueInspectionItem {
  id: string
  vehicleId: string | null
  trailerId: string | null
  ownerNumber: string
  ownerLicensePlate: string
  inspectionType: InspectionType
  customTypeName: string | null
  dueDate: string
  urgency: InspectionUrgency
}

export interface ExpiringDocumentItem {
  id: string
  vehicleId: string | null
  trailerId: string | null
  ownerNumber: string
  ownerLicensePlate: string
  documentType: FleetDocumentType
  customTypeName: string | null
  expiryDate: string
  status: FleetDocumentStatus
}

export interface RecentDamageItem {
  id: string
  vehicleId: string | null
  trailerId: string | null
  ownerNumber: string
  ownerLicensePlate: string
  incidentDate: string
  severity: DamageSeverity
  status: DamageStatus
  description: string
}

export interface FleetDashboard {
  vehicles: FleetAssetCounts
  trailers: FleetAssetCounts
  maintenanceDueCount: number
  maintenanceDue: DueMaintenanceItem[]
  inspectionsDueCount: number
  inspectionsDue: DueInspectionItem[]
  documentsExpiringCount: number
  documentsExpiring: ExpiringDocumentItem[]
  openDamageCount: number
  recentDamage: RecentDamageItem[]
  fuelWarnings: FuelWarning[]
}
